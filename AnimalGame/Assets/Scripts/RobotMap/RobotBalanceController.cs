using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public enum RobotBalanceLevel
    {
        Stable,
        Loaded,
        Critical,
        OutsideSupport
    }

    public readonly struct RobotBalanceState
    {
        public Vector2 NormalizedLocalOffset { get; }
        public Vector2 NormalizedWorldOffset { get; }
        public Vector2 PlayerCounterbalanceLocal { get; }
        public float Magnitude { get; }
        public float Risk01 { get; }
        public RobotBalanceLevel Level { get; }

        public RobotBalanceState(
            Vector2 normalizedLocalOffset,
            Vector2 normalizedWorldOffset,
            Vector2 playerCounterbalanceLocal,
            float magnitude,
            float risk01,
            RobotBalanceLevel level)
        {
            NormalizedLocalOffset = normalizedLocalOffset;
            NormalizedWorldOffset = normalizedWorldOffset;
            PlayerCounterbalanceLocal = playerCounterbalanceLocal;
            Magnitude = magnitude;
            Risk01 = risk01;
            Level = level;
        }
    }

    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMover))]
    public sealed class RobotBalanceController : MonoBehaviour
    {
        private const string BalanceHorizontalAxis = "Gamepad Balance Horizontal";
        private const string BalanceVerticalAxis = "Gamepad Balance Vertical";

        [Header("Physical Balance Model")]
        [Tooltip("Height of the robot centre of mass above its support plane, in logical map metres. A higher centre of mass creates more displacement on the same slope.")]
        [SerializeField, Min(0.05f)] private float centerOfMassHeightMeters = 0.9f;

        [Tooltip("Fraction of the fitted robot footprint treated as safely controllable support. Lower values make the balance point reach the ring sooner.")]
        [SerializeField, Range(0.2f, 1f)] private float usableSupportFraction = 0.98f;

        [Tooltip("Multiplier applied to the static centre-of-mass projection caused by the fitted ground plane.")]
        [SerializeField, Min(0f)] private float slopeBalanceInfluence = 0.65f;

        [Header("Motion Inertia")]
        [Tooltip("Robot acceleration, in Unity world units per second squared, that produces a full-radius inertia offset before the influence multiplier is applied.")]
        [SerializeField, Min(0.1f)] private float accelerationForFullOffset = 8f;

        [Tooltip("Strength of acceleration, braking and turning inertia in the balance result.")]
        [SerializeField, Min(0f)] private float inertiaInfluence = 0.08f;

        [Tooltip("Time used to filter measured acceleration. Higher values remove more frame-to-frame noise but react more slowly to impacts.")]
        [SerializeField, Min(0.01f)] private float accelerationSmoothingTime = 0.22f;

        [Tooltip("Maximum measured acceleration accepted by the balance model. This prevents a teleport or frame hitch from throwing the point infinitely far away.")]
        [SerializeField, Min(0.1f)] private float maximumMeasuredAcceleration = 30f;

        [Header("Player Counterbalance")]
        [Tooltip("Maximum normalized centre-of-mass correction produced by fully holding the right stick or an arrow key.")]
        [SerializeField, Range(0f, 1f)] private float maximumCounterbalance = 0.6f;

        [Tooltip("Dead zone applied to the right stick used for balance control.")]
        [SerializeField, Range(0f, 0.9f)] private float rightStickDeadZone = 0.2f;

        [Tooltip("Response curve applied after the right-stick dead zone. Values above one make small stick movements gentler while preserving the configured maximum range.")]
        [SerializeField, Range(1f, 3f)] private float counterbalanceInputExponent = 1.6f;

        [Tooltip("Time used for the adjustable counterweight to reach the requested right-stick or arrow-key position.")]
        [SerializeField, Min(0.01f)] private float counterbalanceResponseTime = 0.16f;

        [Tooltip("Time used for the adjustable counterweight to return to neutral after balance input is released.")]
        [SerializeField, Min(0.01f)] private float counterbalanceReturnTime = 0.34f;

        [Header("Balance Dynamics")]
        [Tooltip("Natural response frequency of the displayed and gameplay balance point. Higher values follow the target more quickly.")]
        [SerializeField, Range(0.1f, 8f)] private float balanceSpringFrequency = 2f;

        [Tooltip("Damping ratio of the balance point. Values below one allow natural overshoot; one is critically damped.")]
        [SerializeField, Range(0.1f, 2f)] private float balanceDampingRatio = 1.3f;

        [Tooltip("Maximum distance the simulated balance point can travel beyond the control ring.")]
        [SerializeField, Range(1f, 2f)] private float maximumNormalizedOffset = 1.2f;

        [Header("Edge Stability Reserve")]
        [Tooltip("Normalized distance at which the support system begins resisting further outward movement. This gives the player time to counterbalance before tipping.")]
        [SerializeField, Range(0f, 0.95f)] private float edgeResistanceStart = 0.72f;

        [Tooltip("Strength of the nonlinear resistance near the edge. It does not make tipping impossible; only sustained extreme forces can overcome it.")]
        [SerializeField, Min(0f)] private float edgeResistanceStrength = 1.8f;

        [Header("Gameplay Response")]
        [SerializeField] private bool influenceMovement = true;

        [Tooltip("Normalized balance distance at which drive and steering authority begin to decrease.")]
        [SerializeField, Range(0f, 1f)] private float movementInfluenceStart = 0.75f;

        [Tooltip("Smallest remaining forward/reverse authority at or beyond the support boundary.")]
        [SerializeField, Range(0f, 1f)] private float minimumDriveAuthority = 0.68f;

        [Tooltip("Smallest remaining steering authority at or beyond the support boundary.")]
        [SerializeField, Range(0f, 1f)] private float minimumSteeringAuthority = 0.45f;

        [Header("Camera Balance Target")]
        [Tooltip("Maximum local offset, in Unity world units, between the robot body and the balance target followed by the camera when the point reaches the ring.")]
        [SerializeField, Min(0f)] private float cameraFollowOffsetAtRing = 0.3f;

        public RobotBalanceState CurrentState { get; private set; }
        public Transform CameraFollowTarget
        {
            get
            {
                EnsureCameraFollowTarget();
                return cameraFollowTarget;
            }
        }

        public float DriveAuthority => influenceMovement
            ? Mathf.Lerp(
                1f,
                minimumDriveAuthority,
                CurrentState.Risk01)
            : 1f;

        public float SteeringAuthority => influenceMovement
            ? Mathf.Lerp(
                1f,
                minimumSteeringAuthority,
                CurrentState.Risk01)
            : 1f;

        private RobotMover mover;
        private HeightMapTraversalEvaluator traversalEvaluator;
        private Transform cameraFollowTarget;
        private Vector2 currentBalanceLocal;
        private Vector2 balanceVelocityLocal;
        private Vector2 currentCounterbalanceLocal;
        private Vector2 counterbalanceVelocityLocal;
        private Vector2 previousWorldVelocity;
        private Vector2 filteredWorldAcceleration;
        private bool velocityInitialized;
        private static bool balanceAxisMissing;
        private static bool balanceAxisWarningShown;

        private void Awake()
        {
            mover = GetComponent<RobotMover>();
            EnsureCameraFollowTarget();
            PublishState();
        }

        public void SetTraversalEvaluator(HeightMapTraversalEvaluator evaluator)
        {
            traversalEvaluator = evaluator;
        }

        private void LateUpdate()
        {
            if (mover == null)
                return;

            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            if (deltaTime <= 0f)
                return;

            UpdateMeasuredAcceleration(deltaTime);
            UpdatePlayerCounterbalance(deltaTime);

            Vector2 targetBalanceLocal = CalculateSlopeBalanceLocal()
                                         + CalculateInertiaBalanceLocal()
                                         + currentCounterbalanceLocal;
            targetBalanceLocal = ApplyEdgeResistance(
                Vector2.ClampMagnitude(targetBalanceLocal, 4f));

            IntegrateBalanceSpring(targetBalanceLocal, deltaTime);
            UpdateCameraFollowTarget();
            PublishState();
        }

        private Vector2 ApplyEdgeResistance(Vector2 target)
        {
            float magnitude = target.magnitude;
            if (magnitude <= edgeResistanceStart || magnitude <= 0.0001f)
                return target;

            float excess = magnitude - edgeResistanceStart;
            float compressedExcess = excess
                                     / (1f + edgeResistanceStrength * excess);
            return target / magnitude * (edgeResistanceStart + compressedExcess);
        }

        private Vector2 CalculateSlopeBalanceLocal()
        {
            SlopeTraversalResult surface = mover.CurrentTraversalResult;
            if (!surface.HasData
                || surface.DownhillWorldDirection.sqrMagnitude < 0.000001f
                || traversalEvaluator == null)
            {
                return Vector2.zero;
            }

            float angle = Mathf.Clamp(surface.MaximumSurfaceSlopeAngle, 0f, 85f);
            float projectedDistanceMeters = centerOfMassHeightMeters
                                            * Mathf.Tan(angle * Mathf.Deg2Rad)
                                            * slopeBalanceInfluence;
            Vector2 projectedWorld = surface.DownhillWorldDirection.normalized
                                     * projectedDistanceMeters;
            float supportHalfWidth = Mathf.Max(
                0.05f,
                traversalEvaluator.RobotFootprintWidthMeters
                * 0.5f
                * usableSupportFraction);
            float supportHalfLength = Mathf.Max(
                0.05f,
                traversalEvaluator.RobotFootprintLengthMeters
                * 0.5f
                * usableSupportFraction);
            Vector2 bodyRight = transform.right;
            Vector2 bodyForward = transform.up;
            return new Vector2(
                Vector2.Dot(projectedWorld, bodyRight) / supportHalfWidth,
                Vector2.Dot(projectedWorld, bodyForward) / supportHalfLength);
        }

        private Vector2 CalculateInertiaBalanceLocal()
        {
            float divisor = Mathf.Max(0.1f, accelerationForFullOffset);
            Vector2 bodyRight = transform.right;
            Vector2 bodyForward = transform.up;
            return new Vector2(
                       -Vector2.Dot(filteredWorldAcceleration, bodyRight) / divisor,
                       -Vector2.Dot(filteredWorldAcceleration, bodyForward) / divisor)
                   * inertiaInfluence;
        }

        private void UpdateMeasuredAcceleration(float deltaTime)
        {
            Vector2 worldVelocity = (Vector2)transform.up * mover.CurrentSpeed
                                    + mover.CurrentTerrainVelocity;
            if (!velocityInitialized)
            {
                previousWorldVelocity = worldVelocity;
                velocityInitialized = true;
                return;
            }

            Vector2 measuredAcceleration = (worldVelocity - previousWorldVelocity)
                                           / Mathf.Max(0.0001f, deltaTime);
            measuredAcceleration = Vector2.ClampMagnitude(
                measuredAcceleration,
                maximumMeasuredAcceleration);
            float response = 1f - Mathf.Exp(
                -deltaTime / Mathf.Max(0.01f, accelerationSmoothingTime));
            filteredWorldAcceleration = Vector2.Lerp(
                filteredWorldAcceleration,
                measuredAcceleration,
                response);
            previousWorldVelocity = worldVelocity;
        }

        private void UpdatePlayerCounterbalance(float deltaTime)
        {
            Vector2 keyboard = new Vector2(
                (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));
            Vector2 gamepad = ReadRightStickSafely();
            gamepad = ApplyRadialDeadZone(gamepad, rightStickDeadZone);
            if (gamepad.sqrMagnitude > 0.0001f)
            {
                gamepad = gamepad.normalized
                          * Mathf.Pow(
                              Mathf.Clamp01(gamepad.magnitude),
                              counterbalanceInputExponent);
            }
            Vector2 input = keyboard.sqrMagnitude >= gamepad.sqrMagnitude
                ? Vector2.ClampMagnitude(keyboard, 1f)
                : gamepad;
            Vector2 target = input * maximumCounterbalance;
            float smoothingTime = input.sqrMagnitude > 0.0001f
                ? counterbalanceResponseTime
                : counterbalanceReturnTime;
            currentCounterbalanceLocal = Vector2.SmoothDamp(
                currentCounterbalanceLocal,
                target,
                ref counterbalanceVelocityLocal,
                Mathf.Max(0.01f, smoothingTime),
                Mathf.Infinity,
                deltaTime);
        }

        private void IntegrateBalanceSpring(Vector2 target, float deltaTime)
        {
            int substepCount = Mathf.Max(1, Mathf.CeilToInt(deltaTime / (1f / 120f)));
            float step = deltaTime / substepCount;
            float angularFrequency = 2f
                                     * Mathf.PI
                                     * Mathf.Max(0.1f, balanceSpringFrequency);
            float stiffness = angularFrequency * angularFrequency;
            float damping = 2f
                            * Mathf.Max(0.1f, balanceDampingRatio)
                            * angularFrequency;

            for (int i = 0; i < substepCount; i++)
            {
                Vector2 acceleration = (target - currentBalanceLocal) * stiffness
                                       - balanceVelocityLocal * damping;
                balanceVelocityLocal += acceleration * step;
                currentBalanceLocal += balanceVelocityLocal * step;
            }

            if (currentBalanceLocal.magnitude > maximumNormalizedOffset)
            {
                currentBalanceLocal = currentBalanceLocal.normalized
                                      * maximumNormalizedOffset;
                float outwardVelocity = Vector2.Dot(
                    balanceVelocityLocal,
                    currentBalanceLocal.normalized);
                if (outwardVelocity > 0f)
                {
                    balanceVelocityLocal -= currentBalanceLocal.normalized
                                            * outwardVelocity;
                }
            }
        }

        private void PublishState()
        {
            Vector2 worldOffset = (Vector2)transform.right * currentBalanceLocal.x
                                  + (Vector2)transform.up * currentBalanceLocal.y;
            float magnitude = currentBalanceLocal.magnitude;
            float risk = Mathf.InverseLerp(
                movementInfluenceStart,
                1f,
                magnitude);
            RobotBalanceLevel level = magnitude >= 1f
                ? RobotBalanceLevel.OutsideSupport
                : magnitude >= 0.72f
                    ? RobotBalanceLevel.Critical
                    : magnitude >= 0.35f
                        ? RobotBalanceLevel.Loaded
                        : RobotBalanceLevel.Stable;
            CurrentState = new RobotBalanceState(
                currentBalanceLocal,
                worldOffset,
                currentCounterbalanceLocal,
                magnitude,
                risk,
                level);
        }

        private void UpdateCameraFollowTarget()
        {
            EnsureCameraFollowTarget();
            if (cameraFollowTarget == null)
                return;

            cameraFollowTarget.localPosition = new Vector3(
                currentBalanceLocal.x,
                currentBalanceLocal.y,
                0f) * cameraFollowOffsetAtRing;
            cameraFollowTarget.localRotation = Quaternion.identity;
        }

        private void EnsureCameraFollowTarget()
        {
            if (cameraFollowTarget != null)
                return;

            Transform existing = transform.Find("Balance Camera Target");
            if (existing != null)
            {
                cameraFollowTarget = existing;
                return;
            }

            var targetObject = new GameObject("Balance Camera Target");
            cameraFollowTarget = targetObject.transform;
            cameraFollowTarget.SetParent(transform, false);
        }

        private static Vector2 ReadRightStickSafely()
        {
            if (balanceAxisMissing)
                return Vector2.zero;

            try
            {
                return new Vector2(
                    Input.GetAxisRaw(BalanceHorizontalAxis),
                    Input.GetAxisRaw(BalanceVerticalAxis));
            }
            catch (System.ArgumentException)
            {
                balanceAxisMissing = true;
                if (!balanceAxisWarningShown)
                {
                    Debug.LogWarning(
                        "The right-stick balance axes are missing from the legacy Input Manager. "
                        + "Run Animal Game/Repair Gamepad Input Axes after returning to Edit Mode.");
                    balanceAxisWarningShown = true;
                }

                return Vector2.zero;
            }
        }

        private static Vector2 ApplyRadialDeadZone(Vector2 value, float deadZone)
        {
            float magnitude = value.magnitude;
            if (magnitude <= deadZone)
                return Vector2.zero;

            float normalizedMagnitude = Mathf.InverseLerp(deadZone, 1f, magnitude);
            return value.normalized * Mathf.Clamp01(normalizedMagnitude);
        }

        private void OnValidate()
        {
            centerOfMassHeightMeters = Mathf.Max(0.05f, centerOfMassHeightMeters);
            usableSupportFraction = Mathf.Clamp(usableSupportFraction, 0.2f, 1f);
            slopeBalanceInfluence = Mathf.Max(0f, slopeBalanceInfluence);
            accelerationForFullOffset = Mathf.Max(0.1f, accelerationForFullOffset);
            inertiaInfluence = Mathf.Max(0f, inertiaInfluence);
            accelerationSmoothingTime = Mathf.Max(0.01f, accelerationSmoothingTime);
            maximumMeasuredAcceleration = Mathf.Max(0.1f, maximumMeasuredAcceleration);
            maximumCounterbalance = Mathf.Clamp01(maximumCounterbalance);
            rightStickDeadZone = Mathf.Clamp(rightStickDeadZone, 0f, 0.9f);
            counterbalanceInputExponent = Mathf.Clamp(
                counterbalanceInputExponent,
                1f,
                3f);
            counterbalanceResponseTime = Mathf.Max(0.01f, counterbalanceResponseTime);
            counterbalanceReturnTime = Mathf.Max(0.01f, counterbalanceReturnTime);
            balanceSpringFrequency = Mathf.Clamp(balanceSpringFrequency, 0.1f, 8f);
            balanceDampingRatio = Mathf.Clamp(balanceDampingRatio, 0.1f, 2f);
            maximumNormalizedOffset = Mathf.Clamp(maximumNormalizedOffset, 1f, 2f);
            edgeResistanceStart = Mathf.Clamp(edgeResistanceStart, 0f, 0.95f);
            edgeResistanceStrength = Mathf.Max(0f, edgeResistanceStrength);
            movementInfluenceStart = Mathf.Clamp01(movementInfluenceStart);
            minimumDriveAuthority = Mathf.Clamp01(minimumDriveAuthority);
            minimumSteeringAuthority = Mathf.Clamp01(minimumSteeringAuthority);
            cameraFollowOffsetAtRing = Mathf.Max(0f, cameraFollowOffsetAtRing);
        }
    }
}
