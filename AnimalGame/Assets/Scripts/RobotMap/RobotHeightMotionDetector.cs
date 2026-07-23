using System;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public readonly struct RobotLandingImpact
    {
        public float RelativeImpactSpeedMetersPerSecond { get; }
        public float Strength01 { get; }
        public float AirborneDurationSeconds { get; }
        public float GroundDropMeters { get; }
        public Vector2 TravelWorldDirection { get; }

        public RobotLandingImpact(
            float relativeImpactSpeedMetersPerSecond,
            float strength01,
            float airborneDurationSeconds,
            float groundDropMeters,
            Vector2 travelWorldDirection)
        {
            RelativeImpactSpeedMetersPerSecond =
                relativeImpactSpeedMetersPerSecond;
            Strength01 = strength01;
            AirborneDurationSeconds = airborneDurationSeconds;
            GroundDropMeters = groundDropMeters;
            TravelWorldDirection = travelWorldDirection;
        }
    }

    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMover))]
    public sealed class RobotHeightMotionDetector : MonoBehaviour
    {
        [Header("Height Motion Detection")]
        [SerializeField] private bool enableHeightMotionDetection = true;

        [Tooltip("Gravity used by the virtual vertical body while the sampled ground falls away, in logical map metres per second squared.")]
        [SerializeField, Min(0.1f)] private float virtualGravityMetersPerSecondSquared = 9.81f;

        [Tooltip("Minimum planar robot speed required before a sudden loss of ground support can count as takeoff.")]
        [SerializeField, Min(0f)] private float minimumTakeoffPlanarSpeed = 0.65f;

        [Tooltip("Minimum speed at which the ground must fall away relative to the virtual body before takeoff is possible.")]
        [SerializeField, Min(0f)] private float minimumGroundFallAwaySpeed = 1.25f;

        [Tooltip("Minimum vertical gap between the virtual body and sampled ground required to enter the airborne state.")]
        [SerializeField, Min(0f)] private float takeoffClearanceMeters = 0.025f;

        [Tooltip("Vertical tolerance used when the falling virtual body reconnects with the sampled ground.")]
        [SerializeField, Min(0f)] private float landingClearanceMeters = 0.02f;

        [Tooltip("Airborne periods shorter than this are treated as ordinary suspension travel and do not create landing impacts.")]
        [SerializeField, Min(0f)] private float minimumAirborneDurationSeconds = 0.055f;

        [Tooltip("How long Was Airborne Recently remains true after ground contact is restored.")]
        [SerializeField, Min(0f)] private float recentAirborneMemorySeconds = 0.6f;

        [Header("Landing Impact Scale")]
        [Tooltip("Relative landing speed that begins producing a camera landing impact.")]
        [SerializeField, Min(0f)] private float minimumLandingImpactSpeed = 0.45f;

        [Tooltip("Relative landing speed mapped to a full-strength camera landing impact.")]
        [SerializeField, Min(0.01f)] private float fullLandingImpactSpeed = 3.6f;

        [Tooltip("Shapes landing strength after speed normalization. Values below one make medium landings stronger while preserving zero and full-strength endpoints.")]
        [SerializeField, Range(0.1f, 3f)] private float landingImpactStrengthExponent = 0.7f;

        [Header("Signal Filtering")]
        [Tooltip("Smoothing time applied to sampled ground vertical speed. This affects telemetry while takeoff still reacts to the unfiltered ground change.")]
        [SerializeField, Min(0.001f)] private float groundVerticalSpeedSmoothingTime = 0.06f;

        [Tooltip("Maximum absolute sampled ground vertical speed accepted from one frame. Prevents teleports and frame hitches from becoming impacts.")]
        [SerializeField, Min(0.1f)] private float maximumGroundVerticalSpeed = 20f;

        [Tooltip("Maximum absolute vertical acceleration exposed by the detector.")]
        [SerializeField, Min(0.1f)] private float maximumVerticalAcceleration = 60f;

        public bool HasData { get; private set; }
        public bool IsAirborne { get; private set; }
        public bool LandedThisFrame { get; private set; }
        public bool WasAirborneRecently => HasData
            && lastAirborneTime > float.NegativeInfinity
            && Time.time - lastAirborneTime <= recentAirborneMemorySeconds;
        public float CurrentGroundHeightMeters { get; private set; }
        public float VirtualBodyHeightMeters { get; private set; }
        public float HeightChangeThisFrameMeters { get; private set; }
        public float GroundVerticalSpeedMetersPerSecond { get; private set; }
        public float VerticalSpeedMetersPerSecond { get; private set; }
        public float VerticalAccelerationMetersPerSecondSquared { get; private set; }
        public float AirborneDurationSeconds { get; private set; }
        public float CurrentAirGapMeters => HasData
            ? Mathf.Max(0f, VirtualBodyHeightMeters - CurrentGroundHeightMeters)
            : 0f;
        public float TimeSinceLastAirborne => lastAirborneTime > float.NegativeInfinity
            ? Mathf.Max(0f, Time.time - lastAirborneTime)
            : float.PositiveInfinity;
        public RobotLandingImpact LastLandingImpact { get; private set; }

        public event Action<RobotLandingImpact> Landed;

        private RobotMover mover;
        private MapTestSceneController map;
        private bool initialized;
        private float previousGroundHeight;
        private float filteredGroundVerticalSpeed;
        private float airborneStartGroundHeight;
        private float lastAirborneTime = float.NegativeInfinity;
        private Vector2 lastTravelWorldDirection;

        private void Awake()
        {
            mover = GetComponent<RobotMover>();
        }

        public void Initialize(MapTestSceneController mapController)
        {
            map = mapController;
            ResetDetectorState();
        }

        private void LateUpdate()
        {
            LandedThisFrame = false;
            if (!enableHeightMotionDetection || map == null || mover == null)
                return;

            if (!map.TrySampleWorldPosition(
                    transform.position,
                    out _,
                    out float sampledGroundHeight))
            {
                HasData = false;
                initialized = false;
                IsAirborne = false;
                return;
            }

            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            if (deltaTime <= 0.000001f)
                return;

            Vector2 planarVelocity = (Vector2)transform.up * mover.CurrentSpeed
                                     + mover.CurrentTerrainVelocity;
            if (planarVelocity.sqrMagnitude > 0.000001f)
                lastTravelWorldDirection = planarVelocity.normalized;

            if (!initialized)
            {
                InitializeAtHeight(sampledGroundHeight);
                return;
            }

            float previousVerticalSpeed = VerticalSpeedMetersPerSecond;
            HeightChangeThisFrameMeters = sampledGroundHeight
                                          - previousGroundHeight;
            float rawGroundVerticalSpeed = Mathf.Clamp(
                HeightChangeThisFrameMeters / deltaTime,
                -maximumGroundVerticalSpeed,
                maximumGroundVerticalSpeed);
            float filterResponse = 1f - Mathf.Exp(
                -deltaTime / Mathf.Max(0.001f, groundVerticalSpeedSmoothingTime));
            filteredGroundVerticalSpeed = Mathf.Lerp(
                filteredGroundVerticalSpeed,
                rawGroundVerticalSpeed,
                filterResponse);
            GroundVerticalSpeedMetersPerSecond = filteredGroundVerticalSpeed;
            CurrentGroundHeightMeters = sampledGroundHeight;

            if (IsAirborne)
            {
                UpdateAirborneMotion(deltaTime, rawGroundVerticalSpeed);
            }
            else
            {
                UpdateSupportedMotion(
                    deltaTime,
                    rawGroundVerticalSpeed,
                    planarVelocity.magnitude);
            }

            VerticalAccelerationMetersPerSecondSquared = Mathf.Clamp(
                (VerticalSpeedMetersPerSecond - previousVerticalSpeed)
                / deltaTime,
                -maximumVerticalAcceleration,
                maximumVerticalAcceleration);
            previousGroundHeight = sampledGroundHeight;
            HasData = true;
        }

        private void UpdateSupportedMotion(
            float deltaTime,
            float rawGroundVerticalSpeed,
            float planarSpeed)
        {
            float predictedVerticalSpeed = VerticalSpeedMetersPerSecond
                                           - virtualGravityMetersPerSecondSquared
                                           * deltaTime;
            float predictedHeight = VirtualBodyHeightMeters
                                    + VerticalSpeedMetersPerSecond * deltaTime
                                    - 0.5f
                                    * virtualGravityMetersPerSecondSquared
                                    * deltaTime
                                    * deltaTime;
            float separation = predictedHeight - CurrentGroundHeightMeters;
            float relativeFallAwaySpeed = VerticalSpeedMetersPerSecond
                                          - rawGroundVerticalSpeed;
            bool groundActuallyFell = HeightChangeThisFrameMeters < 0f;
            bool shouldTakeOff = groundActuallyFell
                                 && planarSpeed >= minimumTakeoffPlanarSpeed
                                 && relativeFallAwaySpeed
                                 >= minimumGroundFallAwaySpeed
                                 && separation >= takeoffClearanceMeters;

            if (shouldTakeOff)
            {
                IsAirborne = true;
                AirborneDurationSeconds = deltaTime;
                airborneStartGroundHeight = previousGroundHeight;
                VirtualBodyHeightMeters = predictedHeight;
                VerticalSpeedMetersPerSecond = predictedVerticalSpeed;
                lastAirborneTime = Time.time;
                return;
            }

            VirtualBodyHeightMeters = CurrentGroundHeightMeters;
            VerticalSpeedMetersPerSecond = filteredGroundVerticalSpeed;
            AirborneDurationSeconds = 0f;
        }

        private void UpdateAirborneMotion(
            float deltaTime,
            float rawGroundVerticalSpeed)
        {
            VerticalSpeedMetersPerSecond -=
                virtualGravityMetersPerSecondSquared * deltaTime;
            VirtualBodyHeightMeters +=
                VerticalSpeedMetersPerSecond * deltaTime;
            AirborneDurationSeconds += deltaTime;
            lastAirborneTime = Time.time;

            float relativeApproachSpeed = rawGroundVerticalSpeed
                                          - VerticalSpeedMetersPerSecond;
            bool reachedGround = VirtualBodyHeightMeters
                                 <= CurrentGroundHeightMeters
                                 + landingClearanceMeters;
            if (!reachedGround || relativeApproachSpeed <= 0f)
                return;

            float completedAirborneDuration = AirborneDurationSeconds;
            float groundDrop = Mathf.Max(
                0f,
                airborneStartGroundHeight - CurrentGroundHeightMeters);
            IsAirborne = false;
            VirtualBodyHeightMeters = CurrentGroundHeightMeters;
            VerticalSpeedMetersPerSecond = filteredGroundVerticalSpeed;
            AirborneDurationSeconds = 0f;

            if (completedAirborneDuration < minimumAirborneDurationSeconds)
                return;

            float impactSpeed = Mathf.Max(0f, relativeApproachSpeed);
            float linearStrength = Mathf.InverseLerp(
                minimumLandingImpactSpeed,
                Mathf.Max(minimumLandingImpactSpeed + 0.01f,
                    fullLandingImpactSpeed),
                impactSpeed);
            float strength = Mathf.Pow(
                linearStrength,
                landingImpactStrengthExponent);
            LastLandingImpact = new RobotLandingImpact(
                impactSpeed,
                strength,
                completedAirborneDuration,
                groundDrop,
                lastTravelWorldDirection);
            LandedThisFrame = true;
            Landed?.Invoke(LastLandingImpact);
        }

        private void ResetDetectorState()
        {
            HasData = false;
            initialized = false;
            IsAirborne = false;
            LandedThisFrame = false;
            AirborneDurationSeconds = 0f;
            HeightChangeThisFrameMeters = 0f;
            GroundVerticalSpeedMetersPerSecond = 0f;
            VerticalSpeedMetersPerSecond = 0f;
            VerticalAccelerationMetersPerSecondSquared = 0f;
            filteredGroundVerticalSpeed = 0f;
            lastAirborneTime = float.NegativeInfinity;
            lastTravelWorldDirection = Vector2.zero;
            LastLandingImpact = default;
        }

        private void InitializeAtHeight(float heightMeters)
        {
            initialized = true;
            HasData = true;
            previousGroundHeight = heightMeters;
            CurrentGroundHeightMeters = heightMeters;
            VirtualBodyHeightMeters = heightMeters;
            HeightChangeThisFrameMeters = 0f;
            GroundVerticalSpeedMetersPerSecond = 0f;
            VerticalSpeedMetersPerSecond = 0f;
            VerticalAccelerationMetersPerSecondSquared = 0f;
        }

        private void OnValidate()
        {
            virtualGravityMetersPerSecondSquared = Mathf.Max(
                0.1f,
                virtualGravityMetersPerSecondSquared);
            minimumTakeoffPlanarSpeed = Mathf.Max(0f, minimumTakeoffPlanarSpeed);
            minimumGroundFallAwaySpeed = Mathf.Max(0f, minimumGroundFallAwaySpeed);
            takeoffClearanceMeters = Mathf.Max(0f, takeoffClearanceMeters);
            landingClearanceMeters = Mathf.Max(0f, landingClearanceMeters);
            minimumAirborneDurationSeconds = Mathf.Max(
                0f,
                minimumAirborneDurationSeconds);
            recentAirborneMemorySeconds = Mathf.Max(0f, recentAirborneMemorySeconds);
            minimumLandingImpactSpeed = Mathf.Max(0f, minimumLandingImpactSpeed);
            fullLandingImpactSpeed = Mathf.Max(
                minimumLandingImpactSpeed + 0.01f,
                fullLandingImpactSpeed);
            landingImpactStrengthExponent = Mathf.Clamp(
                landingImpactStrengthExponent,
                0.1f,
                3f);
            groundVerticalSpeedSmoothingTime = Mathf.Max(
                0.001f,
                groundVerticalSpeedSmoothingTime);
            maximumGroundVerticalSpeed = Mathf.Max(0.1f, maximumGroundVerticalSpeed);
            maximumVerticalAcceleration = Mathf.Max(0.1f, maximumVerticalAcceleration);
        }
    }
}
