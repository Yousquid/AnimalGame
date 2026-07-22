using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [RequireComponent(typeof(RobotCameraFollow))]
    public sealed class RobotCameraShake : MonoBehaviour
    {
        [Header("General")]
        [SerializeField] private bool enableCameraShake = true;

        [Tooltip("Global multiplier applied once to all continuous vibration and event impacts.")]
        [SerializeField, Range(0f, 3f)] private float globalIntensity = 1f;

        [Tooltip("Maximum camera displacement in Unity world units after all shake sources are mixed.")]
        [SerializeField, Min(0f)] private float maximumPositionOffset = 0.28f;

        [Tooltip("Maximum clockwise or counter-clockwise shake angle.")]
        [SerializeField, Range(0f, 12f)] private float maximumRotationDegrees = 4f;

        [Tooltip("Maximum orthographic-size change as a fraction of the base camera size. 0.03 means three percent.")]
        [SerializeField, Range(0f, 0.1f)] private float maximumZoomFraction = 0.03f;

        [Header("Continuous Chassis Vibration")]
        [Tooltip("Planar speed at which ordinary drive vibration reaches full strength.")]
        [SerializeField, Min(0.1f)] private float fullVibrationSpeed = 4f;

        [Tooltip("Minimum fraction of normal chassis vibration retained once the robot is actually moving. This keeps low-speed travel perceptible without increasing major impact strength.")]
        [SerializeField, Range(0f, 1f)] private float minimumMovingVibrationStrength = 0.35f;

        [Tooltip("Amount of slow chassis vibration caused by balance displacement even when planar speed is very low.")]
        [SerializeField, Range(0f, 1f)] private float stationaryBalanceVibrationStrength = 0.28f;

        [Tooltip("Position amplitude of normal powered movement, in world units.")]
        [SerializeField, Min(0f)] private float drivePositionAmplitude = 0.03f;

        [Tooltip("Rotation amplitude of normal powered movement, in degrees.")]
        [SerializeField, Min(0f)] private float driveRotationAmplitude = 0.24f;

        [Tooltip("Base frequency of the smooth mechanical vibration.")]
        [SerializeField, Min(0.1f)] private float driveVibrationFrequency = 3.4f;

        [Tooltip("Continuous-vibration multiplier while travelling on a Level Two slope.")]
        [SerializeField, Min(0f)] private float levelTwoVibrationMultiplier = 1.8f;

        [Tooltip("Continuous-vibration multiplier while travelling or slipping on a Level Three slope.")]
        [SerializeField, Min(0f)] private float levelThreeVibrationMultiplier = 2.8f;

        [Tooltip("Additional vibration multiplier at full balance displacement.")]
        [SerializeField, Min(0f)] private float balanceVibrationInfluence = 1.05f;

        [Tooltip("Surface roughness value at which its additional vibration reaches full strength.")]
        [SerializeField, Min(0.001f)] private float roughnessAtFullVibration = 0.35f;

        [Tooltip("Additional multiplier contributed by fully rough terrain.")]
        [SerializeField, Min(0f)] private float roughnessVibrationInfluence = 0.75f;

        [Tooltip("Fraction of ground vibration retained while the height detector considers the robot airborne.")]
        [SerializeField, Range(0f, 1f)] private float airborneVibrationMultiplier = 0.18f;

        [Header("Step Collision Impact")]
        [SerializeField, Min(0f)] private float stepPositionImpact = 0.15f;
        [SerializeField, Min(0f)] private float stepRotationImpactDegrees = 1.7f;
        [SerializeField, Range(0f, 0.1f)] private float stepZoomImpactFraction = 0.01f;

        [Header("Unsafe Downhill Impact")]
        [SerializeField, Min(0f)] private float unsafeDownhillPositionImpact = 0.18f;
        [SerializeField, Min(0f)] private float unsafeDownhillRotationImpactDegrees = 1.8f;
        [SerializeField, Range(0f, 0.1f)] private float unsafeDownhillZoomImpactFraction = 0.016f;

        [Header("Level Three Grip Failure Impact")]
        [SerializeField, Min(0f)] private float levelThreeSlipPositionImpact = 0.2f;
        [SerializeField, Min(0f)] private float levelThreeSlipRotationImpactDegrees = 2.8f;
        [SerializeField, Range(0f, 0.1f)] private float levelThreeSlipZoomImpactFraction = 0.008f;

        [Header("Landing Impact")]
        [SerializeField, Min(0f)] private float landingPositionImpact = 0.24f;
        [SerializeField, Min(0f)] private float landingRotationImpactDegrees = 3.2f;
        [SerializeField, Range(0f, 0.1f)] private float landingZoomImpactFraction = 0.026f;

        [Header("Supported Terrain Height Impact")]
        [Tooltip("Upward logical-height acceleration that begins producing a small suspension/chassis compression impact while the robot remains grounded.")]
        [SerializeField, Min(0.1f)] private float minimumHeightImpactAcceleration = 3.5f;

        [Tooltip("Upward logical-height acceleration mapped to a full suspension/chassis compression impact.")]
        [SerializeField, Min(0.1f)] private float fullHeightImpactAcceleration = 16f;

        [SerializeField, Min(0f)] private float heightPositionImpact = 0.075f;
        [SerializeField, Min(0f)] private float heightRotationImpactDegrees = 0.65f;
        [SerializeField, Range(0f, 0.1f)] private float heightZoomImpactFraction = 0.006f;

        [Header("Sudden Deceleration Impact")]
        [Tooltip("Planar deceleration that begins producing a smaller generic inertia impact.")]
        [SerializeField, Min(0.1f)] private float minimumImpactDeceleration = 3.25f;

        [Tooltip("Planar deceleration mapped to full generic inertia impact strength.")]
        [SerializeField, Min(0.1f)] private float fullImpactDeceleration = 10f;

        [SerializeField, Min(0f)] private float decelerationPositionImpact = 0.085f;
        [SerializeField, Min(0f)] private float decelerationRotationImpactDegrees = 0.95f;
        [SerializeField, Range(0f, 0.1f)] private float decelerationZoomImpactFraction = 0.004f;

        [Tooltip("Minimum delay between discrete collision/deceleration impacts.")]
        [SerializeField, Min(0f)] private float impactCooldownSeconds = 0.22f;

        [Tooltip("Converts an impact amplitude into spring velocity. Higher values make the eventual peak stronger without reintroducing a one-frame camera jump.")]
        [SerializeField, Range(0.5f, 3f)] private float impactVelocityMultiplier = 1.8f;

        [Header("Directional Balance Coupling")]
        [Tooltip("How strongly the current centre-of-mass direction biases impact rotation.")]
        [SerializeField, Min(0f)] private float balanceImpactRotationInfluence = 0.75f;

        [Header("Impact Spring - Position")]
        [SerializeField, Range(0.1f, 20f)] private float positionSpringFrequency = 3f;
        [SerializeField, Range(0.05f, 2f)] private float positionSpringDamping = 0.62f;

        [Header("Impact Spring - Rotation")]
        [SerializeField, Range(0.1f, 20f)] private float rotationSpringFrequency = 2.4f;
        [SerializeField, Range(0.05f, 2f)] private float rotationSpringDamping = 0.58f;

        [Header("Impact Spring - Zoom")]
        [SerializeField, Range(0.1f, 20f)] private float zoomSpringFrequency = 3.4f;
        [SerializeField, Range(0.05f, 2f)] private float zoomSpringDamping = 0.68f;

        public Vector2 CurrentLocalPositionOffset { get; private set; }
        public float CurrentRotationOffsetDegrees { get; private set; }
        public float CurrentZoomOffsetFraction { get; private set; }

        private Camera attachedCamera;
        private RobotMover mover;
        private RobotBalanceController balance;
        private RobotHeightMotionDetector heightMotion;
        private float baseOrthographicSize;
        private Vector2 springPosition;
        private Vector2 springPositionVelocity;
        private float springRotation;
        private float springRotationVelocity;
        private float springZoom;
        private float springZoomVelocity;
        private Vector2 continuousPosition;
        private float continuousRotation;
        private Vector2 previousWorldVelocity;
        private bool previousMotionInitialized;
        private bool previousBlocked;
        private TraversalBlockReason previousBlockReason;
        private LevelThreeClimbFailurePhase previousLevelThreePhase;
        private float nextDiscreteImpactTime;
        private float noiseSeedX;
        private float noiseSeedY;
        private float noiseSeedRotation;

        private void Awake()
        {
            attachedCamera = GetComponent<Camera>();
            baseOrthographicSize = attachedCamera != null
                ? attachedCamera.orthographicSize
                : 1f;
            noiseSeedX = Random.Range(10f, 1000f);
            noiseSeedY = Random.Range(10f, 1000f);
            noiseSeedRotation = Random.Range(10f, 1000f);
        }

        public void Initialize(
            RobotMover robotMover,
            RobotBalanceController balanceController,
            RobotHeightMotionDetector heightMotionDetector)
        {
            mover = robotMover;
            balance = balanceController;
            heightMotion = heightMotionDetector;
            previousMotionInitialized = false;
            previousBlocked = mover != null && mover.IsSlopeBlocked;
            previousBlockReason = mover != null
                ? mover.CurrentTraversalResult.BlockReason
                : TraversalBlockReason.None;
            previousLevelThreePhase = mover != null
                ? mover.CurrentLevelThreeClimbPhase
                : LevelThreeClimbFailurePhase.None;
        }

        private void LateUpdate()
        {
            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            if (deltaTime <= 0.000001f || attachedCamera == null)
                return;

            if (!enableCameraShake)
            {
                ResetShakeState();
                attachedCamera.orthographicSize = baseOrthographicSize;
                StorePreviousMotionState();
                return;
            }

            DetectDiscreteImpacts(deltaTime);
            UpdateContinuousVibration();
            IntegrateSprings(deltaTime);
            ApplyShakeToCamera();
            StorePreviousMotionState();
        }

        private void DetectDiscreteImpacts(float deltaTime)
        {
            if (mover == null)
                return;

            Vector2 currentVelocity = GetCurrentWorldVelocity();
            bool triggeredMajorImpact = false;
            SlopeTraversalResult traversal = mover.CurrentTraversalResult;
            bool blocked = mover.IsSlopeBlocked && traversal.HasData;
            if (blocked
                && (!previousBlocked || traversal.BlockReason != previousBlockReason)
                && Time.time >= nextDiscreteImpactTime)
            {
                if (traversal.BlockReason == TraversalBlockReason.Step)
                {
                    Vector2 direction = GetReliableTravelDirection(
                        previousWorldVelocity,
                        transform.up);
                    float speedStrength = Mathf.InverseLerp(
                        0.25f,
                        Mathf.Max(0.5f, fullVibrationSpeed),
                        previousWorldVelocity.magnitude);
                    float stepStrength = Mathf.InverseLerp(
                        0.15f,
                        1.25f,
                        traversal.MaximumStepHeight);
                    float strength = Mathf.Clamp01(
                        Mathf.Max(0.25f, speedStrength * 0.65f + stepStrength * 0.35f));
                    AddDirectionalImpact(
                        direction,
                        strength,
                        stepPositionImpact,
                        stepRotationImpactDegrees,
                        stepZoomImpactFraction);
                    triggeredMajorImpact = true;
                }
                else if (traversal.BlockReason
                         == TraversalBlockReason.UnsafeDownhill)
                {
                    Vector2 direction = GetReliableTravelDirection(
                        traversal.DownhillWorldDirection,
                        previousWorldVelocity);
                    float angleStrength = Mathf.InverseLerp(
                        30f,
                        75f,
                        traversal.MaximumDownhillAngle);
                    float speedStrength = Mathf.InverseLerp(
                        0.25f,
                        Mathf.Max(0.5f, fullVibrationSpeed),
                        previousWorldVelocity.magnitude);
                    AddDirectionalImpact(
                        direction,
                        Mathf.Clamp01(Mathf.Max(0.3f,
                            angleStrength * 0.6f + speedStrength * 0.4f)),
                        unsafeDownhillPositionImpact,
                        unsafeDownhillRotationImpactDegrees,
                        unsafeDownhillZoomImpactFraction);
                    triggeredMajorImpact = true;
                }
            }

            if (mover.CurrentLevelThreeClimbPhase
                    == LevelThreeClimbFailurePhase.Slip
                && previousLevelThreePhase
                    != LevelThreeClimbFailurePhase.Slip
                && Time.time >= nextDiscreteImpactTime)
            {
                Vector2 direction = GetReliableTravelDirection(
                    traversal.DownhillWorldDirection,
                    mover.CurrentTerrainVelocity);
                float balanceStrength = balance != null
                    ? Mathf.Clamp01(balance.CurrentState.Magnitude)
                    : 0f;
                AddDirectionalImpact(
                    direction,
                    Mathf.Lerp(0.65f, 1f, balanceStrength),
                    levelThreeSlipPositionImpact,
                    levelThreeSlipRotationImpactDegrees,
                    levelThreeSlipZoomImpactFraction);
                triggeredMajorImpact = true;
            }

            if (heightMotion != null && heightMotion.LandedThisFrame)
            {
                RobotLandingImpact landing = heightMotion.LastLandingImpact;
                Vector2 direction = GetReliableTravelDirection(
                    landing.TravelWorldDirection,
                    traversal.DownhillWorldDirection);
                AddDirectionalImpact(
                    direction,
                    landing.Strength01,
                    landingPositionImpact,
                    landingRotationImpactDegrees,
                    landingZoomImpactFraction);
                triggeredMajorImpact = landing.Strength01 > 0f
                                       || triggeredMajorImpact;
            }

            if (!triggeredMajorImpact
                && heightMotion != null
                && heightMotion.HasData
                && !heightMotion.IsAirborne
                && heightMotion.VerticalAccelerationMetersPerSecondSquared
                >= minimumHeightImpactAcceleration
                && Time.time >= nextDiscreteImpactTime)
            {
                float strength = Mathf.InverseLerp(
                    minimumHeightImpactAcceleration,
                    Mathf.Max(minimumHeightImpactAcceleration + 0.1f,
                        fullHeightImpactAcceleration),
                    heightMotion.VerticalAccelerationMetersPerSecondSquared);
                AddDirectionalImpact(
                    GetReliableTravelDirection(
                        currentVelocity,
                        traversal.DownhillWorldDirection),
                    strength,
                    heightPositionImpact,
                    heightRotationImpactDegrees,
                    heightZoomImpactFraction);
                triggeredMajorImpact = strength > 0f;
            }

            if (!triggeredMajorImpact
                && previousMotionInitialized
                && Time.time >= nextDiscreteImpactTime)
            {
                Vector2 velocityChange = currentVelocity - previousWorldVelocity;
                float deceleration = Mathf.Max(
                    0f,
                    previousWorldVelocity.magnitude - currentVelocity.magnitude)
                                     / deltaTime;
                if (deceleration >= minimumImpactDeceleration
                    && velocityChange.sqrMagnitude > 0.000001f)
                {
                    float strength = Mathf.InverseLerp(
                        minimumImpactDeceleration,
                        Mathf.Max(minimumImpactDeceleration + 0.1f,
                            fullImpactDeceleration),
                        deceleration);
                    AddDirectionalImpact(
                        GetReliableTravelDirection(
                            previousWorldVelocity,
                            -velocityChange),
                        strength,
                        decelerationPositionImpact,
                        decelerationRotationImpactDegrees,
                        decelerationZoomImpactFraction);
                    triggeredMajorImpact = true;
                }
            }

            if (triggeredMajorImpact)
                nextDiscreteImpactTime = Time.time + impactCooldownSeconds;
        }

        private void UpdateContinuousVibration()
        {
            continuousPosition = Vector2.zero;
            continuousRotation = 0f;
            if (mover == null)
                return;

            Vector2 velocity = GetCurrentWorldVelocity();
            float speed = velocity.magnitude;
            float speedProgress = Mathf.InverseLerp(
                0.08f,
                Mathf.Max(0.1f, fullVibrationSpeed),
                speed);
            float movementPresence = Mathf.InverseLerp(
                0.02f,
                0.35f,
                speed);
            speedProgress = movementPresence
                            * Mathf.Lerp(
                                minimumMovingVibrationStrength,
                                1f,
                                speedProgress);

            float balanceMagnitude = balance != null
                ? Mathf.Clamp01(balance.CurrentState.Magnitude)
                : 0f;
            float baseStrength = Mathf.Max(
                speedProgress,
                balanceMagnitude * stationaryBalanceVibrationStrength);
            if (baseStrength <= 0f)
                return;

            SlopeTraversalResult traversal = mover.CurrentTraversalResult;
            float slopeMultiplier = 1f;
            if (traversal.HasData)
            {
                slopeMultiplier = traversal.UphillLevel switch
                {
                    UphillSlopeLevel.LevelTwo => levelTwoVibrationMultiplier,
                    UphillSlopeLevel.LevelThree => levelThreeVibrationMultiplier,
                    _ => 1f
                };

                float roughness = Mathf.InverseLerp(
                    0f,
                    Mathf.Max(0.001f, roughnessAtFullVibration),
                    traversal.SurfaceRoughness);
                slopeMultiplier *= 1f + roughness * roughnessVibrationInfluence;
            }

            if (mover.CurrentLevelThreeClimbPhase
                == LevelThreeClimbFailurePhase.Strain)
            {
                slopeMultiplier *= 1.25f;
            }
            else if (mover.CurrentLevelThreeClimbPhase
                     == LevelThreeClimbFailurePhase.Slip)
            {
                slopeMultiplier *= 1.45f;
            }

            float balanceMultiplier = 1f
                                      + balanceMagnitude
                                      * balanceVibrationInfluence;
            float airborneMultiplier = heightMotion != null
                                       && heightMotion.IsAirborne
                ? airborneVibrationMultiplier
                : 1f;
            float amplitude = baseStrength
                              * slopeMultiplier
                              * balanceMultiplier
                              * airborneMultiplier;
            float time = Time.time * driveVibrationFrequency;
            float noiseX = SignedPerlin(noiseSeedX, time);
            float noiseY = SignedPerlin(noiseSeedY, time * 0.83f);
            float noiseRotation = SignedPerlin(
                noiseSeedRotation,
                time * 0.67f);
            continuousPosition = new Vector2(noiseX, noiseY)
                                 * drivePositionAmplitude
                                 * amplitude;
            continuousRotation = noiseRotation
                                 * driveRotationAmplitude
                                 * amplitude;
        }

        private void AddDirectionalImpact(
            Vector2 worldDirection,
            float strength01,
            float positionAmplitude,
            float rotationAmplitudeDegrees,
            float zoomAmplitudeFraction)
        {
            float strength = Mathf.Clamp01(strength01);
            if (strength <= 0f)
                return;

            Vector2 localDirection = WorldToCameraLocalDirection(worldDirection);
            if (localDirection.sqrMagnitude < 0.000001f)
                localDirection = Vector2.up;

            float balanceRoll = 0f;
            if (balance != null
                && balance.CurrentState.NormalizedWorldOffset.sqrMagnitude
                > 0.000001f)
            {
                Vector2 localBalance = WorldToCameraLocalDirection(
                    balance.CurrentState.NormalizedWorldOffset);
                balanceRoll = -localBalance.x
                              * Mathf.Clamp01(balance.CurrentState.Magnitude)
                              * balanceImpactRotationInfluence;
            }

            // Feed impacts into spring velocity instead of changing the camera
            // displacement immediately. The camera now takes a short, readable
            // time to reach the impact peak and no longer jumps in one frame.
            float positionAngularFrequency = Mathf.PI
                                             * 2f
                                             * Mathf.Max(
                                                 0.1f,
                                                 positionSpringFrequency);
            float rotationAngularFrequency = Mathf.PI
                                             * 2f
                                             * Mathf.Max(
                                                 0.1f,
                                                 rotationSpringFrequency);
            float zoomAngularFrequency = Mathf.PI
                                         * 2f
                                         * Mathf.Max(
                                             0.1f,
                                             zoomSpringFrequency);
            springPositionVelocity += localDirection
                                      * positionAmplitude
                                      * strength
                                      * positionAngularFrequency
                                      * impactVelocityMultiplier;
            springRotationVelocity += (-localDirection.x + balanceRoll)
                                      * rotationAmplitudeDegrees
                                      * strength
                                      * rotationAngularFrequency
                                      * impactVelocityMultiplier;
            springZoomVelocity += zoomAmplitudeFraction
                                  * strength
                                  * zoomAngularFrequency
                                  * impactVelocityMultiplier;
        }

        private void IntegrateSprings(float deltaTime)
        {
            int substeps = Mathf.Max(1, Mathf.CeilToInt(deltaTime / (1f / 120f)));
            float step = deltaTime / substeps;
            for (int i = 0; i < substeps; i++)
            {
                IntegrateSpring(
                    ref springPosition,
                    ref springPositionVelocity,
                    positionSpringFrequency,
                    positionSpringDamping,
                    step);
                IntegrateSpring(
                    ref springRotation,
                    ref springRotationVelocity,
                    rotationSpringFrequency,
                    rotationSpringDamping,
                    step);
                IntegrateSpring(
                    ref springZoom,
                    ref springZoomVelocity,
                    zoomSpringFrequency,
                    zoomSpringDamping,
                    step);
            }

            springPosition = Vector2.ClampMagnitude(
                springPosition,
                maximumPositionOffset);
            springRotation = Mathf.Clamp(
                springRotation,
                -maximumRotationDegrees,
                maximumRotationDegrees);
            springZoom = Mathf.Clamp(
                springZoom,
                -maximumZoomFraction,
                maximumZoomFraction);
        }

        private void ApplyShakeToCamera()
        {
            Vector2 localOffset = Vector2.ClampMagnitude(
                (springPosition + continuousPosition) * globalIntensity,
                maximumPositionOffset);
            float rotationOffset = Mathf.Clamp(
                (springRotation + continuousRotation) * globalIntensity,
                -maximumRotationDegrees,
                maximumRotationDegrees);
            float zoomOffset = Mathf.Clamp(
                springZoom * globalIntensity,
                -maximumZoomFraction,
                maximumZoomFraction);

            CurrentLocalPositionOffset = localOffset;
            CurrentRotationOffsetDegrees = rotationOffset;
            CurrentZoomOffsetFraction = zoomOffset;

            Quaternion baseRotation = transform.rotation;
            transform.position += baseRotation
                                  * new Vector3(localOffset.x, localOffset.y, 0f);
            transform.rotation = baseRotation
                                 * Quaternion.Euler(0f, 0f, rotationOffset);
            attachedCamera.orthographicSize = Mathf.Max(
                0.01f,
                baseOrthographicSize * (1f + zoomOffset));
        }

        private Vector2 GetCurrentWorldVelocity()
        {
            return mover == null
                ? Vector2.zero
                : (Vector2)mover.transform.up * mover.CurrentSpeed
                  + mover.CurrentTerrainVelocity;
        }

        private void StorePreviousMotionState()
        {
            if (mover == null)
                return;

            previousWorldVelocity = GetCurrentWorldVelocity();
            previousMotionInitialized = true;
            previousBlocked = mover.IsSlopeBlocked;
            previousBlockReason = mover.CurrentTraversalResult.BlockReason;
            previousLevelThreePhase = mover.CurrentLevelThreeClimbPhase;
        }

        private Vector2 WorldToCameraLocalDirection(Vector2 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 0.000001f)
                return Vector2.zero;

            Vector3 local = Quaternion.Inverse(transform.rotation)
                            * new Vector3(worldDirection.x, worldDirection.y, 0f);
            return new Vector2(local.x, local.y).normalized;
        }

        private static Vector2 GetReliableTravelDirection(
            Vector2 preferred,
            Vector2 fallback)
        {
            if (preferred.sqrMagnitude > 0.000001f)
                return preferred.normalized;
            return fallback.sqrMagnitude > 0.000001f
                ? fallback.normalized
                : Vector2.up;
        }

        private static float SignedPerlin(float seed, float time)
        {
            return Mathf.PerlinNoise(seed, time) * 2f - 1f;
        }

        private static void IntegrateSpring(
            ref Vector2 value,
            ref Vector2 velocity,
            float frequency,
            float dampingRatio,
            float deltaTime)
        {
            float angularFrequency = Mathf.PI * 2f * Mathf.Max(0.1f, frequency);
            Vector2 acceleration = -value * angularFrequency * angularFrequency
                                   - velocity
                                   * (2f * Mathf.Max(0.05f, dampingRatio)
                                      * angularFrequency);
            velocity += acceleration * deltaTime;
            value += velocity * deltaTime;
        }

        private static void IntegrateSpring(
            ref float value,
            ref float velocity,
            float frequency,
            float dampingRatio,
            float deltaTime)
        {
            float angularFrequency = Mathf.PI * 2f * Mathf.Max(0.1f, frequency);
            float acceleration = -value * angularFrequency * angularFrequency
                                 - velocity
                                 * (2f * Mathf.Max(0.05f, dampingRatio)
                                    * angularFrequency);
            velocity += acceleration * deltaTime;
            value += velocity * deltaTime;
        }

        private void ResetShakeState()
        {
            springPosition = Vector2.zero;
            springPositionVelocity = Vector2.zero;
            springRotation = 0f;
            springRotationVelocity = 0f;
            springZoom = 0f;
            springZoomVelocity = 0f;
            continuousPosition = Vector2.zero;
            continuousRotation = 0f;
            CurrentLocalPositionOffset = Vector2.zero;
            CurrentRotationOffsetDegrees = 0f;
            CurrentZoomOffsetFraction = 0f;
        }

        private void OnDisable()
        {
            ResetShakeState();
            if (attachedCamera != null)
                attachedCamera.orthographicSize = baseOrthographicSize;
        }

        private void OnValidate()
        {
            globalIntensity = Mathf.Clamp(globalIntensity, 0f, 3f);
            maximumPositionOffset = Mathf.Max(0f, maximumPositionOffset);
            maximumRotationDegrees = Mathf.Clamp(maximumRotationDegrees, 0f, 12f);
            maximumZoomFraction = Mathf.Clamp(maximumZoomFraction, 0f, 0.1f);
            fullVibrationSpeed = Mathf.Max(0.1f, fullVibrationSpeed);
            minimumMovingVibrationStrength = Mathf.Clamp01(
                minimumMovingVibrationStrength);
            stationaryBalanceVibrationStrength = Mathf.Clamp01(
                stationaryBalanceVibrationStrength);
            drivePositionAmplitude = Mathf.Max(0f, drivePositionAmplitude);
            driveRotationAmplitude = Mathf.Max(0f, driveRotationAmplitude);
            driveVibrationFrequency = Mathf.Max(0.1f, driveVibrationFrequency);
            roughnessAtFullVibration = Mathf.Max(0.001f, roughnessAtFullVibration);
            minimumImpactDeceleration = Mathf.Max(0.1f, minimumImpactDeceleration);
            fullImpactDeceleration = Mathf.Max(
                minimumImpactDeceleration + 0.1f,
                fullImpactDeceleration);
            impactCooldownSeconds = Mathf.Max(0f, impactCooldownSeconds);
            impactVelocityMultiplier = Mathf.Clamp(
                impactVelocityMultiplier,
                0.5f,
                3f);
            minimumHeightImpactAcceleration = Mathf.Max(
                0.1f,
                minimumHeightImpactAcceleration);
            fullHeightImpactAcceleration = Mathf.Max(
                minimumHeightImpactAcceleration + 0.1f,
                fullHeightImpactAcceleration);
        }
    }
}
