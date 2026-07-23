using System;
using System.Runtime.InteropServices;
using AnimalGame.MapTest;
using UnityEngine;
using Random = UnityEngine.Random;

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

        [Header("Gamepad Rumble")]
        [Tooltip("Sends vibration to an XInput-compatible gamepad using the final, visible screen-shake amplitude.")]
        [SerializeField] private bool enableGamepadRumble = true;

        [Tooltip("XInput controller slot. Zero is the first connected gamepad.")]
        [SerializeField, Range(0, 3)] private int gamepadIndex;

        [Tooltip("Camera position offset that maps to full controller vibration.")]
        [SerializeField, Min(0.001f)] private float positionOffsetAtFullRumble = 0.16f;

        [Tooltip("Camera rotation offset that maps to full controller vibration.")]
        [SerializeField, Min(0.01f)] private float rotationDegreesAtFullRumble = 2.2f;

        [Tooltip("Camera zoom offset fraction that maps to full controller vibration.")]
        [SerializeField, Min(0.0001f)] private float zoomFractionAtFullRumble = 0.018f;

        [Tooltip("Strength multiplier for the low-frequency motor, mainly driven by camera displacement and zoom impact.")]
        [SerializeField, Range(0f, 2f)] private float lowFrequencyMotorMultiplier = 0.55f;

        [Tooltip("Strength multiplier for the high-frequency motor, mainly driven by camera rotation and fine vibration.")]
        [SerializeField, Range(0f, 2f)] private float highFrequencyMotorMultiplier = 0.45f;

        [Tooltip("Fraction of camera position shake also sent to the high-frequency motor.")]
        [SerializeField, Range(0f, 1f)] private float positionToHighFrequencyMotor = 0.35f;

        [Tooltip("Non-linear response applied to visible screen-shake strength before driving the motors. Values above one suppress small everyday vibration while preserving large impacts.")]
        [SerializeField, Range(0.5f, 3f)] private float rumbleResponseExponent = 1.45f;

        [Tooltip("Additional motor-strength multiplier when the centre of mass reaches maximum imbalance.")]
        [SerializeField, Range(1f, 5f)] private float fullImbalanceRumbleMultiplier = 2.6f;

        [Tooltip("Shapes when the imbalance boost becomes prominent. Values above one reserve most of the boost for severe imbalance.")]
        [SerializeField, Range(0.5f, 4f)] private float imbalanceRumbleExponent = 1.6f;

        [Tooltip("Temporary controller-vibration multiplier applied at the moment of a detected airborne landing.")]
        [SerializeField, Range(1f, 4f)] private float landingRumbleMultiplier = 1.8f;

        [Tooltip("Time for the landing-specific controller boost to fade while the screen impact spring settles.")]
        [SerializeField, Min(0.01f)] private float landingRumbleBoostDuration = 0.45f;

        [Tooltip("How quickly controller vibration rises toward the current screen-shake strength.")]
        [SerializeField, Min(0.1f)] private float rumbleAttackSpeed = 12f;

        [Tooltip("How quickly controller vibration fades after the screen shake subsides.")]
        [SerializeField, Min(0.1f)] private float rumbleReleaseSpeed = 6f;

        [Tooltip("Motor values below this threshold are sent as zero to prevent faint residual buzzing.")]
        [SerializeField, Range(0f, 0.2f)] private float minimumRumbleOutput = 0.025f;

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
        [SerializeField, Min(0f)] private float landingPositionImpact = 0.36f;
        [SerializeField, Min(0f)] private float landingRotationImpactDegrees = 5.2f;
        [SerializeField, Range(0f, 0.1f)] private float landingZoomImpactFraction = 0.042f;

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
        private float currentLowFrequencyRumble;
        private float currentHighFrequencyRumble;
        private float lastSentLowFrequencyRumble;
        private float lastSentHighFrequencyRumble;
        private float nextRumbleRefreshTime;
        private bool rumbleWasSent;
        private float lastLandingRumbleTime = float.NegativeInfinity;
        private float lastLandingRumbleStrength;

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
                StopGamepadRumble();
                attachedCamera.orthographicSize = baseOrthographicSize;
                StorePreviousMotionState();
                return;
            }

            DetectDiscreteImpacts(deltaTime);
            UpdateContinuousVibration();
            IntegrateSprings(deltaTime);
            ApplyShakeToCamera();
            UpdateGamepadRumble(deltaTime);
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
                if (landing.Strength01 > 0f)
                {
                    lastLandingRumbleTime = Time.time;
                    lastLandingRumbleStrength = landing.Strength01;
                }
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

        private void UpdateGamepadRumble(float deltaTime)
        {
            if (!enableGamepadRumble
                || !Application.isFocused
                || Time.timeScale <= 0.0001f)
            {
                StopGamepadRumble();
                return;
            }

            float positionStrength = Mathf.Clamp01(
                CurrentLocalPositionOffset.magnitude
                / Mathf.Max(0.001f, positionOffsetAtFullRumble));
            float rotationStrength = Mathf.Clamp01(
                Mathf.Abs(CurrentRotationOffsetDegrees)
                / Mathf.Max(0.01f, rotationDegreesAtFullRumble));
            float zoomStrength = Mathf.Clamp01(
                Mathf.Abs(CurrentZoomOffsetFraction)
                / Mathf.Max(0.0001f, zoomFractionAtFullRumble));
            positionStrength = Mathf.Pow(
                positionStrength,
                rumbleResponseExponent);
            rotationStrength = Mathf.Pow(
                rotationStrength,
                rumbleResponseExponent);
            zoomStrength = Mathf.Pow(
                zoomStrength,
                rumbleResponseExponent);

            float balanceMagnitude = balance != null
                ? Mathf.Clamp01(balance.CurrentState.Magnitude)
                : 0f;
            float imbalanceBoost = Mathf.Lerp(
                1f,
                fullImbalanceRumbleMultiplier,
                Mathf.Pow(balanceMagnitude, imbalanceRumbleExponent));
            float landingEnvelope = 1f - Mathf.Clamp01(
                (Time.time - lastLandingRumbleTime)
                / Mathf.Max(0.01f, landingRumbleBoostDuration));
            float landingBoost = Mathf.Lerp(
                1f,
                landingRumbleMultiplier,
                landingEnvelope * lastLandingRumbleStrength);

            float targetLow = Mathf.Clamp01(
                Mathf.Max(positionStrength, zoomStrength)
                * lowFrequencyMotorMultiplier
                * imbalanceBoost
                * landingBoost);
            float targetHigh = Mathf.Clamp01(
                Mathf.Max(
                    rotationStrength,
                    positionStrength * positionToHighFrequencyMotor)
                * highFrequencyMotorMultiplier
                * imbalanceBoost
                * landingBoost);
            currentLowFrequencyRumble = MoveRumbleTowards(
                currentLowFrequencyRumble,
                targetLow,
                deltaTime);
            currentHighFrequencyRumble = MoveRumbleTowards(
                currentHighFrequencyRumble,
                targetHigh,
                deltaTime);

            float outputLow = currentLowFrequencyRumble
                              >= minimumRumbleOutput
                ? currentLowFrequencyRumble
                : 0f;
            float outputHigh = currentHighFrequencyRumble
                               >= minimumRumbleOutput
                ? currentHighFrequencyRumble
                : 0f;
            bool changed = Mathf.Abs(
                               outputLow - lastSentLowFrequencyRumble)
                           >= 0.005f
                           || Mathf.Abs(
                               outputHigh - lastSentHighFrequencyRumble)
                           >= 0.005f;
            if (!changed
                && rumbleWasSent
                && Time.unscaledTime < nextRumbleRefreshTime)
            {
                return;
            }

            rumbleWasSent = WindowsXInputRumble.SetMotorSpeeds(
                gamepadIndex,
                outputLow,
                outputHigh);
            lastSentLowFrequencyRumble = outputLow;
            lastSentHighFrequencyRumble = outputHigh;
            nextRumbleRefreshTime = Time.unscaledTime + 0.25f;
        }

        private float MoveRumbleTowards(
            float current,
            float target,
            float deltaTime)
        {
            float speed = target > current
                ? rumbleAttackSpeed
                : rumbleReleaseSpeed;
            return Mathf.MoveTowards(
                current,
                target,
                Mathf.Max(0f, speed) * deltaTime);
        }

        private void StopGamepadRumble()
        {
            if (rumbleWasSent
                || currentLowFrequencyRumble > 0f
                || currentHighFrequencyRumble > 0f)
            {
                WindowsXInputRumble.SetMotorSpeeds(gamepadIndex, 0f, 0f);
            }

            currentLowFrequencyRumble = 0f;
            currentHighFrequencyRumble = 0f;
            lastSentLowFrequencyRumble = 0f;
            lastSentHighFrequencyRumble = 0f;
            rumbleWasSent = false;
            lastLandingRumbleTime = float.NegativeInfinity;
            lastLandingRumbleStrength = 0f;
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
            StopGamepadRumble();
            if (attachedCamera != null)
                attachedCamera.orthographicSize = baseOrthographicSize;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                StopGamepadRumble();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
                StopGamepadRumble();
        }

        private void OnDestroy()
        {
            StopGamepadRumble();
        }

        private void OnValidate()
        {
            globalIntensity = Mathf.Clamp(globalIntensity, 0f, 3f);
            maximumPositionOffset = Mathf.Max(0f, maximumPositionOffset);
            maximumRotationDegrees = Mathf.Clamp(maximumRotationDegrees, 0f, 12f);
            maximumZoomFraction = Mathf.Clamp(maximumZoomFraction, 0f, 0.1f);
            gamepadIndex = Mathf.Clamp(gamepadIndex, 0, 3);
            positionOffsetAtFullRumble = Mathf.Max(
                0.001f,
                positionOffsetAtFullRumble);
            rotationDegreesAtFullRumble = Mathf.Max(
                0.01f,
                rotationDegreesAtFullRumble);
            zoomFractionAtFullRumble = Mathf.Max(
                0.0001f,
                zoomFractionAtFullRumble);
            lowFrequencyMotorMultiplier = Mathf.Clamp(
                lowFrequencyMotorMultiplier,
                0f,
                2f);
            highFrequencyMotorMultiplier = Mathf.Clamp(
                highFrequencyMotorMultiplier,
                0f,
                2f);
            positionToHighFrequencyMotor = Mathf.Clamp01(
                positionToHighFrequencyMotor);
            rumbleResponseExponent = Mathf.Clamp(
                rumbleResponseExponent,
                0.5f,
                3f);
            fullImbalanceRumbleMultiplier = Mathf.Clamp(
                fullImbalanceRumbleMultiplier,
                1f,
                5f);
            imbalanceRumbleExponent = Mathf.Clamp(
                imbalanceRumbleExponent,
                0.5f,
                4f);
            landingRumbleMultiplier = Mathf.Clamp(
                landingRumbleMultiplier,
                1f,
                4f);
            landingRumbleBoostDuration = Mathf.Max(
                0.01f,
                landingRumbleBoostDuration);
            rumbleAttackSpeed = Mathf.Max(0.1f, rumbleAttackSpeed);
            rumbleReleaseSpeed = Mathf.Max(0.1f, rumbleReleaseSpeed);
            minimumRumbleOutput = Mathf.Clamp(
                minimumRumbleOutput,
                0f,
                0.2f);
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

    internal static class WindowsXInputRumble
    {
        private const uint Success = 0;

        private enum Backend
        {
            Unknown,
            XInput14,
            XInput13,
            XInput910,
            Unavailable
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputVibration
        {
            public ushort LeftMotorSpeed;
            public ushort RightMotorSpeed;
        }

        private static Backend backend;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        private static extern uint XInputSetState14(
            uint userIndex,
            ref XInputVibration vibration);

        [DllImport("xinput1_3.dll", EntryPoint = "XInputSetState")]
        private static extern uint XInputSetState13(
            uint userIndex,
            ref XInputVibration vibration);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
        private static extern uint XInputSetState910(
            uint userIndex,
            ref XInputVibration vibration);
#endif

        public static bool SetMotorSpeeds(
            int gamepadIndex,
            float lowFrequency,
            float highFrequency)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            XInputVibration vibration = new()
            {
                LeftMotorSpeed = ToMotorSpeed(lowFrequency),
                RightMotorSpeed = ToMotorSpeed(highFrequency)
            };
            uint index = (uint)Mathf.Clamp(gamepadIndex, 0, 3);
            try
            {
                return SetState(index, ref vibration) == Success;
            }
            catch (DllNotFoundException)
            {
                backend = Backend.Unavailable;
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                backend = Backend.Unavailable;
                return false;
            }
#else
            return false;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private static uint SetState(
            uint index,
            ref XInputVibration vibration)
        {
            switch (backend)
            {
                case Backend.XInput14:
                    return XInputSetState14(index, ref vibration);
                case Backend.XInput13:
                    return XInputSetState13(index, ref vibration);
                case Backend.XInput910:
                    return XInputSetState910(index, ref vibration);
                case Backend.Unavailable:
                    return 1;
            }

            try
            {
                backend = Backend.XInput14;
                return XInputSetState14(index, ref vibration);
            }
            catch (DllNotFoundException)
            {
                // Older Windows/Unity installations may only include another
                // XInput redistributable, so try each known ABI once.
            }
            catch (EntryPointNotFoundException)
            {
            }

            try
            {
                backend = Backend.XInput13;
                return XInputSetState13(index, ref vibration);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            backend = Backend.XInput910;
            return XInputSetState910(index, ref vibration);
        }
#endif

        private static ushort ToMotorSpeed(float strength)
        {
            return (ushort)Mathf.RoundToInt(
                Mathf.Clamp01(strength) * ushort.MaxValue);
        }
    }
}
