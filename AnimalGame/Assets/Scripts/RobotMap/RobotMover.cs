using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public enum LevelThreeClimbFailurePhase
    {
        None,
        Grip,
        Strain,
        Slip
    }

    public sealed class RobotMover : MonoBehaviour
    {
        [Header("Base Movement")]
        [Tooltip("Global scale applied once to every absolute movement speed, linear/angular acceleration, terrain slide rate and recovery rate. Relative slope multipliers and phase durations are intentionally unchanged. Set to 0.85 for an overall 15 percent reduction.")]
        [SerializeField, Range(0f, 2f)] private float overallMotionScale = 1f;

        [Tooltip("Maximum forward speed on Level One terrain, in Unity world units per second.")]
        [SerializeField, Min(0f)] private float forwardSpeed = 5f;

        [Tooltip("Maximum commanded reverse speed on Level One terrain, in Unity world units per second.")]
        [SerializeField, Min(0f)] private float reverseSpeed = 3f;

        [Tooltip("Maximum turning speed, in degrees per second.")]
        [SerializeField, Min(0f)] private float turnSpeed = 100f;

        [Header("Turning Feel")]
        [Tooltip("How quickly angular speed builds while holding a turn input, in degrees per second squared.")]
        [SerializeField, Min(0f)] private float turnAcceleration = 180f;

        [Tooltip("How quickly angular speed returns to zero after releasing the turn input.")]
        [SerializeField, Min(0f)] private float turnDeceleration = 240f;

        [Header("Input")]
        [Tooltip("Ignores small gamepad stick movement while keeping keyboard input unchanged.")]
        [SerializeField, Range(0f, 0.9f)] private float stickDeadZone = 0.15f;

        [Header("Acceleration Feel")]
        [Tooltip("Acceleration from rest. Lower values make the initial launch feel heavier.")]
        [SerializeField, Min(0f)] private float launchAcceleration = 2.2f;

        [Tooltip("Acceleration after the robot has started moving.")]
        [SerializeField, Min(0f)] private float runningAcceleration = 4.8f;

        [Tooltip("Natural deceleration after releasing the movement input. Terrain sliding is applied separately and can still move the robot downhill.")]
        [SerializeField, Min(0f)] private float coastDeceleration = 2.6f;

        [Tooltip("Deceleration when pressing the opposite movement direction.")]
        [SerializeField, Min(0f)] private float brakingDeceleration = 8f;

        [Header("Slope Level II")]
        [Tooltip("Smallest top-speed multiplier reached at the top of Level Two. A value of 0.5 means the commanded uphill top speed falls to 50 percent before Level Three begins.")]
        [SerializeField, Range(0.05f, 1f)] private float levelTwoMinimumTopSpeedMultiplier = 0.55f;

        [Tooltip("Maximum natural downhill slide speed at the end of Level Two, expressed as a fraction of base forward speed.")]
        [SerializeField, Range(0f, 1f)] private float levelTwoMaximumSlideSpeedFraction = 0.18f;

        [Tooltip("How quickly the terrain velocity builds toward the Level Two slide speed, in Unity world units per second squared.")]
        [SerializeField, Min(0f)] private float levelTwoSlideAcceleration = 2.2f;

        [Tooltip("How much Level Two sliding follows the terrain's physical steepest-downhill direction. The remaining weight follows the robot's fore/aft traction axis.")]
        [SerializeField, Range(0f, 1f)] private float levelTwoPhysicalDownhillWeight = 0.3f;

        [Tooltip("Fraction of Level Two slide perpendicular to the robot's fore/aft axis that remains after lateral traction. Lower values reduce sideways drifting.")]
        [SerializeField, Range(0f, 1f)] private float levelTwoLateralSlipRetention = 0.2f;

        [Tooltip("How quickly old Level Two lateral velocity is pulled toward the traction-limited target, in Unity world units per second squared.")]
        [SerializeField, Min(0f)] private float levelTwoLateralGripAcceleration = 12f;

        [Header("Slope Level III")]
        [Tooltip("Commanded uphill top-speed multiplier on Level Three. A moderate value lets the robot struggle forward near the Level Three entrance, while increasingly strong slide can still overpower it on steeper terrain.")]
        [SerializeField, Range(0f, 1f)] private float levelThreeForwardSpeedMultiplier = 0.5f;

        [Tooltip("Natural downhill slide speed on an almost vertical Level Three surface, expressed as a fraction of base forward speed.")]
        [SerializeField, Range(0f, 2f)] private float levelThreeSlideSpeedFraction = 0.55f;

        [Tooltip("How much Level Three sliding follows the terrain's physical steepest-downhill direction. The remaining weight follows the robot's fore/aft axis; high values represent a major loss of grip.")]
        [SerializeField, Range(0f, 1f)] private float levelThreePhysicalDownhillWeight = 0.85f;

        [Tooltip("How quickly the terrain velocity builds toward the Level Three downhill and lateral target, in Unity world units per second squared.")]
        [SerializeField, Min(0f)] private float levelThreeSlideAcceleration = 6f;

        [Tooltip("Maximum uncontrolled sideways speed while the player tries to climb Level Three, expressed as a fraction of base forward speed.")]
        [SerializeField, Range(0f, 2f)] private float levelThreeLateralDriftSpeedFraction = 0.65f;

        [Tooltip("Fraction of the chosen left or right drift applied immediately when Level Three instability begins. This prevents the first response from looking like pure backward sliding.")]
        [SerializeField, Range(0f, 1f)] private float levelThreeInitialLateralDriftStrength = 0.3f;

        [Tooltip("Average time in seconds before Level Three chooses a new random left or right drift direction.")]
        [SerializeField, Min(0.1f)] private float levelThreeDriftDirectionChangeInterval = 0.55f;

        [Tooltip("Time in seconds used to blend between random left and right drift targets. Higher values make the instability smoother.")]
        [SerializeField, Min(0.01f)] private float levelThreeDriftSmoothing = 0.12f;

        [Header("Slope Level III Climb Failure Sequence")]
        [Tooltip("Uses a Grip -> Strain -> Slip sequence instead of immediately balancing uphill drive against downhill slide on Level Three.")]
        [SerializeField] private bool useLevelThreeClimbFailureSequence = true;

        [Tooltip("Time for which the robot initially grips Level Three and makes visible uphill progress.")]
        [SerializeField, Min(0.05f)] private float levelThreeGripDuration = 0.35f;

        [Tooltip("Commanded uphill top-speed multiplier during the initial Grip phase.")]
        [SerializeField, Range(0f, 1f)] private float levelThreeGripForwardSpeedMultiplier = 0.75f;

        [Tooltip("Fraction of normal Level Three downhill slide applied during the initial Grip phase.")]
        [SerializeField, Range(0f, 1f)] private float levelThreeGripSlideMultiplier = 0.25f;

        [Tooltip("Time over which drive fades and slide/instability build after the initial Grip phase.")]
        [SerializeField, Min(0.05f)] private float levelThreeStrainDuration = 0.45f;

        [Tooltip("Commanded uphill top-speed multiplier reached at the end of Strain, immediately before grip failure.")]
        [SerializeField, Range(0f, 1f)] private float levelThreeStrainEndForwardSpeedMultiplier = 0.25f;

        [Tooltip("Fixed time for which player drive remains disabled while full Level Three slide and instability take over. The existing downhill recovery event begins afterwards.")]
        [SerializeField, Min(0.05f)] private float levelThreeSlipDuration = 0.45f;

        [Header("Slope Level III Uncontrolled Rotation")]
        [Tooltip("Maximum uncontrolled angular speed while slipping on Level Three, in degrees per second. This rotates the actual robot transform, so the Indicator shows the forced change in facing direction.")]
        [SerializeField, Min(0f)] private float levelThreeAngularDriftSpeed = 180f;

        [Tooltip("How quickly Level Three builds toward its uncontrolled angular speed, in degrees per second squared. Raise this when the Indicator should be snapped sideways more violently.")]
        [SerializeField, Min(0f)] private float levelThreeAngularDriftAcceleration = 720f;

        [Tooltip("How quickly uncontrolled angular velocity returns to zero after the robot stops trying to climb Level Three or leaves that surface.")]
        [SerializeField, Min(0f)] private float levelThreeAngularRecoveryAcceleration = 420f;

        [Header("Downhill Heading Recovery")]
        [Tooltip("After a Level Three climbing failure, automatically turns the robot and its Indicator toward the measured downhill direction instead of leaving it sliding sideways.")]
        [SerializeField] private bool automaticallyAlignDownhillAfterLevelThreeSlip = true;

        [Tooltip("Automatic downhill-facing rotation speed after a Level Three climbing failure, in degrees per second.")]
        [SerializeField, Min(0f)] private float downhillHeadingAlignmentSpeed = 260f;

        [Tooltip("Fixed duration of the downhill-recovery event. Steering is returned when this time expires, even if the visual heading has not completely aligned.")]
        [SerializeField, Min(0.05f)] private float downhillHeadingRecoveryDuration = 0.45f;

        [Tooltip("How quickly velocity perpendicular to the measured downhill direction is removed during recovery, in Unity world units per second squared. This can be high while visual rotation remains slow.")]
        [SerializeField, Min(0f)] private float downhillSlideLateralDamping = 18f;

        [Header("Terrain Velocity Recovery")]
        [Tooltip("How quickly old sliding velocity returns to zero after the robot reaches Level One terrain or loses valid terrain data, in Unity world units per second squared.")]
        [SerializeField, Min(0f)] private float terrainVelocityRecoveryAcceleration = 4f;

        [Header("Downhill Response")]
        [Tooltip("Downhill angle at which the speed bonus begins.")]
        [SerializeField, Range(0f, 89f)] private float downhillBoostStartAngle = 4f;

        [Tooltip("Downhill angle at which the maximum speed bonus is reached. The traversal evaluator can still hard-stop a steeper unsafe descent.")]
        [SerializeField, Range(0.1f, 89f)] private float downhillFullBoostAngle = 35f;

        [Tooltip("Maximum downhill top-speed multiplier. A value of 1.5 allows 150 percent of normal top speed.")]
        [SerializeField, Range(1f, 3f)] private float downhillMaximumSpeedMultiplier = 1.5f;

        [Tooltip("Additional acceleration applied while travelling downhill, scaled by the downhill-angle progress.")]
        [SerializeField, Min(0f)] private float downhillAccelerationBonus = 3f;

        public float CurrentSpeed { get; private set; }
        public float CurrentTurnSpeed { get; private set; }
        public float CurrentTerrainTurnSpeed { get; private set; }
        public Vector2 CurrentTerrainVelocity { get; private set; }
        public Vector2 MapPosition => transform.position;
        public Vector2 Forward => transform.up;
        public bool IsSlopeBlocked { get; private set; }
        public bool IsLevelThreeUnstable { get; private set; }
        public bool IsAutoAligningDownhill { get; private set; }
        public bool IsDownhillBoosted { get; private set; }
        public LevelThreeClimbFailurePhase CurrentLevelThreeClimbPhase
        {
            get;
            private set;
        }
        public SlopeTraversalResult CurrentTraversalResult { get; private set; }

        private HeightMapTraversalEvaluator traversalEvaluator;
        private float unstableLateralTarget;
        private float unstableLateralBlend;
        private float unstableLateralBlendVelocity;
        private float nextUnstableDirectionChangeTime;
        private float downhillHeadingRecoveryEndTime;
        private Vector2 downhillHeadingRecoveryDirection;
        private float levelThreeClimbPhaseStartTime;
        private bool pendingDownhillRecoveryFromLevelThreeSlip;

        public void SetTraversalEvaluator(HeightMapTraversalEvaluator evaluator)
        {
            traversalEvaluator = evaluator;
            IsSlopeBlocked = false;
            IsLevelThreeUnstable = false;
            IsAutoAligningDownhill = false;
            IsDownhillBoosted = false;
            ResetLevelThreeClimbFailureSequence();
            CurrentTerrainTurnSpeed = 0f;
            CurrentTerrainVelocity = Vector2.zero;
            downhillHeadingRecoveryEndTime = 0f;
            downhillHeadingRecoveryDirection = Vector2.zero;
            pendingDownhillRecoveryFromLevelThreeSlip = false;
            CurrentTraversalResult = SlopeTraversalResult.NoData;
        }

        private void Update()
        {
            float keyboardThrottle = ReadKeyboardAxis(
                KeyCode.S,
                KeyCode.DownArrow,
                KeyCode.W,
                KeyCode.UpArrow);
            float keyboardSteering = ReadKeyboardAxis(
                KeyCode.A,
                KeyCode.LeftArrow,
                KeyCode.D,
                KeyCode.RightArrow);
            float gamepadThrottle = ApplyDeadZone(Input.GetAxisRaw("Gamepad Move"));
            float gamepadSteering = ApplyDeadZone(Input.GetAxisRaw("Gamepad Turn"));

            float throttle = SelectStrongerInput(keyboardThrottle, gamepadThrottle);
            float steering = SelectStrongerInput(keyboardSteering, gamepadSteering);

            ExpireDownhillHeadingRecoveryIfNeeded();

            float probeSign = DetermineProbeSign(throttle);
            Vector2 probeDirection = IsAutoAligningDownhill
                                     && downhillHeadingRecoveryDirection
                                         .sqrMagnitude > 0.000001f
                ? downhillHeadingRecoveryDirection
                : (Vector2)transform.up * probeSign;
            SlopeTraversalResult pathResult = traversalEvaluator != null
                ? traversalEvaluator.EvaluateImmediateSafety(
                    transform.position,
                    probeDirection)
                : SlopeTraversalResult.NoData;
            SlopeTraversalResult groundResult = traversalEvaluator != null
                ? traversalEvaluator.EvaluateCurrentSurface(transform.position, probeDirection)
                : SlopeTraversalResult.NoData;

            UpdateLevelThreeClimbFailureSequence(groundResult, throttle);

            bool steeringLocked = IsAutoAligningDownhill
                                  || CurrentLevelThreeClimbPhase
                                  != LevelThreeClimbFailurePhase.None
                                  || (!useLevelThreeClimbFailureSequence
                                      && IsTryingToClimbLevelThree(
                                          groundResult,
                                          throttle));
            UpdateTurning(throttle, steering, steeringLocked);

            if (pathResult.HasData && pathResult.RequiresHardStop)
            {
                HardStop(pathResult);
                return;
            }

            IsSlopeBlocked = false;
            CurrentTraversalResult = groundResult.HasData ? groundResult : pathResult;

            float topSpeedMultiplier = 1f;
            float accelerationBonus = 0f;
            float terrainVelocityAcceleration = ScaleMotion(terrainVelocityRecoveryAcceleration);
            float targetTerrainTurnSpeed;
            bool wasLevelThreeUnstable = IsLevelThreeUnstable;
            bool suppressPlayerDrive = IsAutoAligningDownhill
                                       || CurrentLevelThreeClimbPhase
                                       == LevelThreeClimbFailurePhase.Slip;
            float terrainThrottle = suppressPlayerDrive ? 0f : throttle;
            Vector2 targetTerrainVelocity = CalculateTerrainTargetVelocity(
                groundResult,
                terrainThrottle,
                ref topSpeedMultiplier,
                ref accelerationBonus,
                ref terrainVelocityAcceleration,
                out targetTerrainTurnSpeed);
            bool shouldStartDownhillRecovery = useLevelThreeClimbFailureSequence
                ? pendingDownhillRecoveryFromLevelThreeSlip
                : wasLevelThreeUnstable;
            UpdateDownhillHeadingRecoveryState(
                shouldStartDownhillRecovery,
                groundResult);
            pendingDownhillRecoveryFromLevelThreeSlip = false;
            if (IsAutoAligningDownhill
                && downhillHeadingRecoveryDirection.sqrMagnitude > 0.000001f)
            {
                targetTerrainVelocity = downhillHeadingRecoveryDirection.normalized
                                        * targetTerrainVelocity.magnitude;
            }
            CurrentTerrainVelocity = Vector2.MoveTowards(
                CurrentTerrainVelocity,
                targetTerrainVelocity,
                terrainVelocityAcceleration * Time.deltaTime);
            ApplyLevelTwoLateralGrip(groundResult, targetTerrainVelocity);
            ApplyDownhillSlideDirectionRecovery();
            float terrainTurnAcceleration = IsLevelThreeUnstable
                ? ScaleMotion(levelThreeAngularDriftAcceleration)
                : ScaleMotion(levelThreeAngularRecoveryAcceleration);
            CurrentTerrainTurnSpeed = Mathf.MoveTowards(
                CurrentTerrainTurnSpeed,
                targetTerrainTurnSpeed,
                terrainTurnAcceleration * Time.deltaTime);

            float movementThrottle = suppressPlayerDrive ? 0f : throttle;
            if (suppressPlayerDrive)
                CurrentSpeed = 0f;
            float baseTargetSpeed = movementThrottle >= 0f
                ? movementThrottle * ScaleMotion(forwardSpeed)
                : movementThrottle * ScaleMotion(reverseSpeed);
            float targetSpeed = baseTargetSpeed * topSpeedMultiplier;
            float speedChangeRate = GetSpeedChangeRate(
                                        movementThrottle,
                                        targetSpeed)
                                    + accelerationBonus;
            CurrentSpeed = Mathf.MoveTowards(
                CurrentSpeed,
                targetSpeed,
                speedChangeRate * Time.deltaTime);

            Vector2 desiredVelocity = (Vector2)transform.up * CurrentSpeed
                                      + CurrentTerrainVelocity;
            if (desiredVelocity.sqrMagnitude > 0.000001f && traversalEvaluator != null)
            {
                SlopeTraversalResult actualPathResult =
                    traversalEvaluator.EvaluateImmediateSafety(
                        transform.position,
                        desiredVelocity.normalized);
                bool hardObstacle = actualPathResult.HasData
                                    && actualPathResult.RequiresHardStop
                                    && actualPathResult.BlockReason
                                    != TraversalBlockReason.UnsafeDownhill;
                if (hardObstacle)
                {
                    HardStop(actualPathResult);
                    return;
                }
            }

            transform.position += (Vector3)(desiredVelocity * Time.deltaTime);
            transform.Rotate(
                0f,
                0f,
                -CurrentTerrainTurnSpeed * Time.deltaTime);
            ApplyDownhillHeadingRecovery();
        }

        private void UpdateTurning(
            float throttle,
            float steering,
            bool steeringLocked)
        {
            float targetTurnSpeed = steeringLocked
                ? 0f
                : steering * ScaleMotion(turnSpeed);
            float turnChangeRate = steeringLocked
                                   || Mathf.Approximately(steering, 0f)
                ? ScaleMotion(turnDeceleration)
                : ScaleMotion(turnAcceleration);
            CurrentTurnSpeed = Mathf.MoveTowards(
                CurrentTurnSpeed,
                targetTurnSpeed,
                turnChangeRate * Time.deltaTime);

            float reverseDirection = Mathf.Abs(CurrentSpeed) > 0.05f
                ? Mathf.Sign(CurrentSpeed)
                : Mathf.Abs(throttle) > 0.01f
                    ? Mathf.Sign(throttle)
                    : 1f;
            transform.Rotate(
                0f,
                0f,
                -CurrentTurnSpeed * reverseDirection * Time.deltaTime);
        }

        private Vector2 CalculateTerrainTargetVelocity(
            SlopeTraversalResult groundResult,
            float throttle,
            ref float topSpeedMultiplier,
            ref float accelerationBonus,
            ref float terrainVelocityAcceleration,
            out float targetTerrainTurnSpeed)
        {
            targetTerrainTurnSpeed = 0f;
            IsLevelThreeUnstable = false;
            IsDownhillBoosted = false;
            if (!groundResult.HasData || traversalEvaluator == null)
            {
                RelaxUnstableDrift();
                terrainVelocityAcceleration = ScaleMotion(
                    terrainVelocityRecoveryAcceleration);
                return Vector2.zero;
            }

            Vector2 terrainVelocity = Vector2.zero;
            float levelOneAngle = traversalEvaluator.LevelOneMaximumUphillAngle;
            float levelThreeAngle = traversalEvaluator.LevelThreeUphillAngle;
            float surfaceAngle = groundResult.MaximumSurfaceSlopeAngle;
            float directionalAngle = Mathf.Abs(groundResult.SignedSlopeAngle);
            UphillSlopeLevel surfaceLevel =
                traversalEvaluator.ClassifyUphillSlope(surfaceAngle);

            if (surfaceLevel == UphillSlopeLevel.LevelTwo)
            {
                terrainVelocityAcceleration = ScaleMotion(
                    levelTwoSlideAcceleration);
                float levelTwoProgress = Mathf.InverseLerp(
                    levelOneAngle,
                    levelThreeAngle,
                    directionalAngle);
                float slideSpeed = ScaleMotion(forwardSpeed)
                                   * levelTwoMaximumSlideSpeedFraction
                                   * levelTwoProgress;
                Vector2 slideVector = CalculateTractionAdjustedSlideVector(
                    groundResult.DownhillWorldDirection,
                    levelTwoPhysicalDownhillWeight,
                    levelTwoLateralSlipRetention);
                terrainVelocity += slideVector * slideSpeed;
            }
            else if (surfaceLevel == UphillSlopeLevel.LevelThree)
            {
                terrainVelocityAcceleration = ScaleMotion(
                    levelThreeSlideAcceleration);
                float effectiveLevelThreeAngle = Mathf.Max(
                    levelThreeAngle,
                    directionalAngle);
                float levelThreeProgress = Mathf.InverseLerp(
                    levelThreeAngle,
                    89f,
                    effectiveLevelThreeAngle);
                float slideFraction = Mathf.Lerp(
                    levelTwoMaximumSlideSpeedFraction,
                    levelThreeSlideSpeedFraction,
                    levelThreeProgress);
                Vector2 slideVector = CalculateTractionAdjustedSlideVector(
                    groundResult.DownhillWorldDirection,
                    levelThreePhysicalDownhillWeight,
                    1f);
                terrainVelocity += slideVector
                                   * ScaleMotion(forwardSpeed)
                                   * slideFraction
                                   * GetLevelThreeSlideMultiplier();
            }

            if (groundResult.SignedSlopeAngle > levelOneAngle)
            {
                if (groundResult.UphillLevel == UphillSlopeLevel.LevelTwo)
                {
                    float levelTwoProgress = Mathf.InverseLerp(
                        levelOneAngle,
                        levelThreeAngle,
                        groundResult.MaximumUphillAngle);
                    topSpeedMultiplier = Mathf.Lerp(
                        1f,
                        levelTwoMinimumTopSpeedMultiplier,
                        levelTwoProgress);
                }
                else if (groundResult.UphillLevel == UphillSlopeLevel.LevelThree)
                {
                    topSpeedMultiplier = GetLevelThreeForwardSpeedMultiplier();
                }
            }

            float levelThreeInstability = GetLevelThreeInstability(
                groundResult,
                throttle);
            if (levelThreeInstability > 0f)
            {
                IsLevelThreeUnstable = true;
                UpdateUnstableDriftTarget();
                Vector2 crossSlopeDirection = new Vector2(
                    -groundResult.DownhillWorldDirection.y,
                    groundResult.DownhillWorldDirection.x);
                terrainVelocity += crossSlopeDirection
                                   * unstableLateralBlend
                                   * ScaleMotion(forwardSpeed)
                                   * levelThreeLateralDriftSpeedFraction
                                   * levelThreeInstability;
                targetTerrainTurnSpeed = unstableLateralBlend
                                         * ScaleMotion(levelThreeAngularDriftSpeed)
                                         * levelThreeInstability;
            }
            else
            {
                RelaxUnstableDrift();
            }

            if (surfaceLevel == UphillSlopeLevel.LevelOne)
            {
                terrainVelocityAcceleration = ScaleMotion(
                    terrainVelocityRecoveryAcceleration);
            }

            if (groundResult.SignedSlopeAngle < -downhillBoostStartAngle)
            {
                float downhillProgress = Mathf.InverseLerp(
                    downhillBoostStartAngle,
                    downhillFullBoostAngle,
                    groundResult.MaximumDownhillAngle);
                topSpeedMultiplier = Mathf.Max(
                    topSpeedMultiplier,
                    Mathf.Lerp(1f, downhillMaximumSpeedMultiplier, downhillProgress));
                accelerationBonus += ScaleMotion(downhillAccelerationBonus)
                                     * downhillProgress;
                IsDownhillBoosted = downhillProgress > 0f;
            }

            return terrainVelocity;
        }

        private float GetLevelThreeForwardSpeedMultiplier()
        {
            if (!useLevelThreeClimbFailureSequence)
                return levelThreeForwardSpeedMultiplier;

            return CurrentLevelThreeClimbPhase switch
            {
                LevelThreeClimbFailurePhase.Grip =>
                    levelThreeGripForwardSpeedMultiplier,
                LevelThreeClimbFailurePhase.Strain => Mathf.Lerp(
                    levelThreeGripForwardSpeedMultiplier,
                    levelThreeStrainEndForwardSpeedMultiplier,
                    GetCurrentLevelThreePhaseProgress(levelThreeStrainDuration)),
                LevelThreeClimbFailurePhase.Slip => 0f,
                _ => levelThreeForwardSpeedMultiplier
            };
        }

        private float GetLevelThreeSlideMultiplier()
        {
            if (!useLevelThreeClimbFailureSequence)
                return 1f;

            return CurrentLevelThreeClimbPhase switch
            {
                LevelThreeClimbFailurePhase.Grip =>
                    levelThreeGripSlideMultiplier,
                LevelThreeClimbFailurePhase.Strain => Mathf.Lerp(
                    levelThreeGripSlideMultiplier,
                    1f,
                    GetCurrentLevelThreePhaseProgress(levelThreeStrainDuration)),
                _ => 1f
            };
        }

        private float GetLevelThreeInstability(
            SlopeTraversalResult groundResult,
            float throttle)
        {
            if (!useLevelThreeClimbFailureSequence)
            {
                return IsTryingToClimbLevelThree(groundResult, throttle)
                    ? 1f
                    : 0f;
            }

            return CurrentLevelThreeClimbPhase switch
            {
                LevelThreeClimbFailurePhase.Strain => Mathf.Lerp(
                    0.15f,
                    1f,
                    GetCurrentLevelThreePhaseProgress(levelThreeStrainDuration)),
                LevelThreeClimbFailurePhase.Slip => 1f,
                _ => 0f
            };
        }

        private float GetCurrentLevelThreePhaseProgress(float duration)
        {
            return Mathf.Clamp01(
                (Time.time - levelThreeClimbPhaseStartTime)
                / Mathf.Max(0.05f, duration));
        }

        private Vector2 CalculateTractionAdjustedSlideVector(
            Vector2 physicalDownhillDirection,
            float physicalDownhillWeight,
            float lateralSlipRetention)
        {
            if (physicalDownhillDirection.sqrMagnitude < 0.000001f)
                return Vector2.zero;

            Vector2 downhill = physicalDownhillDirection.normalized;
            Vector2 bodyForward = ((Vector2)transform.up).normalized;
            Vector2 bodyAxisDownhill = bodyForward
                                       * Vector2.Dot(downhill, bodyForward);
            float physicalWeight = Mathf.Clamp01(physicalDownhillWeight);
            Vector2 blendedSlide = bodyAxisDownhill * (1f - physicalWeight)
                                   + downhill * physicalWeight;

            Vector2 longitudinal = bodyForward
                                   * Vector2.Dot(blendedSlide, bodyForward);
            Vector2 lateral = blendedSlide - longitudinal;
            return longitudinal + lateral * Mathf.Clamp01(lateralSlipRetention);
        }

        private void ApplyLevelTwoLateralGrip(
            SlopeTraversalResult groundResult,
            Vector2 targetTerrainVelocity)
        {
            if (IsAutoAligningDownhill
                || !groundResult.HasData
                || traversalEvaluator == null
                || traversalEvaluator.ClassifyUphillSlope(
                    groundResult.MaximumSurfaceSlopeAngle)
                != UphillSlopeLevel.LevelTwo)
            {
                return;
            }

            Vector2 bodyForward = ((Vector2)transform.up).normalized;
            Vector2 currentLongitudinal = bodyForward
                                          * Vector2.Dot(
                                              CurrentTerrainVelocity,
                                              bodyForward);
            Vector2 currentLateral = CurrentTerrainVelocity
                                     - currentLongitudinal;
            Vector2 targetLongitudinal = bodyForward
                                         * Vector2.Dot(
                                             targetTerrainVelocity,
                                             bodyForward);
            Vector2 targetLateral = targetTerrainVelocity
                                    - targetLongitudinal;
            Vector2 correctedLateral = Vector2.MoveTowards(
                currentLateral,
                targetLateral,
                ScaleMotion(levelTwoLateralGripAcceleration) * Time.deltaTime);
            CurrentTerrainVelocity = currentLongitudinal + correctedLateral;
        }

        private void UpdateLevelThreeClimbFailureSequence(
            SlopeTraversalResult groundResult,
            float throttle)
        {
            if (!useLevelThreeClimbFailureSequence
                || IsAutoAligningDownhill
                || traversalEvaluator == null
                || !groundResult.HasData)
            {
                ResetLevelThreeClimbFailureSequence();
                return;
            }

            bool onLevelThreeSurface = traversalEvaluator.ClassifyUphillSlope(
                                           groundResult.MaximumSurfaceSlopeAngle)
                                       == UphillSlopeLevel.LevelThree;
            bool tryingToClimb = IsTryingToClimbLevelThree(
                groundResult,
                throttle);

            switch (CurrentLevelThreeClimbPhase)
            {
                case LevelThreeClimbFailurePhase.None:
                    if (tryingToClimb)
                    {
                        EnterLevelThreeClimbPhase(
                            LevelThreeClimbFailurePhase.Grip);
                    }
                    break;

                case LevelThreeClimbFailurePhase.Grip:
                    if (!onLevelThreeSurface)
                    {
                        ResetLevelThreeClimbFailureSequence();
                    }
                    else if (!tryingToClimb
                             || GetCurrentLevelThreePhaseProgress(
                                 levelThreeGripDuration) >= 1f)
                    {
                        EnterLevelThreeClimbPhase(
                            tryingToClimb
                                ? LevelThreeClimbFailurePhase.Strain
                                : LevelThreeClimbFailurePhase.Slip);
                    }
                    break;

                case LevelThreeClimbFailurePhase.Strain:
                    if (!onLevelThreeSurface)
                    {
                        ResetLevelThreeClimbFailureSequence();
                    }
                    else if (!tryingToClimb
                             || GetCurrentLevelThreePhaseProgress(
                                 levelThreeStrainDuration) >= 1f)
                    {
                        EnterLevelThreeClimbPhase(
                            LevelThreeClimbFailurePhase.Slip);
                    }
                    break;

                case LevelThreeClimbFailurePhase.Slip:
                    if (GetCurrentLevelThreePhaseProgress(
                            levelThreeSlipDuration) >= 1f)
                    {
                        pendingDownhillRecoveryFromLevelThreeSlip = true;
                        ResetLevelThreeClimbFailureSequence();
                    }
                    break;
            }
        }

        private void EnterLevelThreeClimbPhase(
            LevelThreeClimbFailurePhase phase)
        {
            CurrentLevelThreeClimbPhase = phase;
            levelThreeClimbPhaseStartTime = Time.time;
            CurrentTurnSpeed = 0f;
            if (phase == LevelThreeClimbFailurePhase.Slip)
                CurrentSpeed = 0f;
        }

        private void ResetLevelThreeClimbFailureSequence()
        {
            CurrentLevelThreeClimbPhase = LevelThreeClimbFailurePhase.None;
            levelThreeClimbPhaseStartTime = 0f;
        }

        private static bool IsTryingToClimbLevelThree(
            SlopeTraversalResult groundResult,
            float throttle)
        {
            return groundResult.HasData
                   && Mathf.Abs(throttle) > 0.01f
                   && groundResult.SignedSlopeAngle > 0f
                   && groundResult.UphillLevel == UphillSlopeLevel.LevelThree;
        }

        private void UpdateDownhillHeadingRecoveryState(
            bool shouldStartRecovery,
            SlopeTraversalResult groundResult)
        {
            if (!automaticallyAlignDownhillAfterLevelThreeSlip)
            {
                IsAutoAligningDownhill = false;
                return;
            }

            if (IsLevelThreeUnstable)
            {
                IsAutoAligningDownhill = false;
                return;
            }

            bool hasUsableDownhillDirection = groundResult.HasData
                                              && groundResult.DownhillWorldDirection
                                                  .sqrMagnitude > 0.000001f;

            if (shouldStartRecovery
                && hasUsableDownhillDirection)
            {
                IsAutoAligningDownhill = true;
                downhillHeadingRecoveryEndTime = Time.time
                                                   + downhillHeadingRecoveryDuration;
                downhillHeadingRecoveryDirection =
                    groundResult.DownhillWorldDirection.normalized;
                CurrentSpeed = 0f;
                CurrentTurnSpeed = 0f;
                CurrentTerrainTurnSpeed = 0f;
            }

            if (IsAutoAligningDownhill && hasUsableDownhillDirection)
            {
                downhillHeadingRecoveryDirection = Vector2.Lerp(
                        downhillHeadingRecoveryDirection,
                        groundResult.DownhillWorldDirection.normalized,
                        1f - Mathf.Exp(-12f * Time.deltaTime))
                    .normalized;
            }
            else if (IsAutoAligningDownhill)
            {
                IsAutoAligningDownhill = false;
            }
        }

        private void ExpireDownhillHeadingRecoveryIfNeeded()
        {
            if (IsAutoAligningDownhill
                && Time.time >= downhillHeadingRecoveryEndTime)
            {
                IsAutoAligningDownhill = false;
            }
        }

        private void ApplyDownhillSlideDirectionRecovery()
        {
            if (!IsAutoAligningDownhill
                || downhillHeadingRecoveryDirection.sqrMagnitude < 0.000001f)
            {
                return;
            }

            Vector2 downhill = downhillHeadingRecoveryDirection.normalized;
            float downhillSpeed = Mathf.Max(
                0f,
                Vector2.Dot(CurrentTerrainVelocity, downhill));
            Vector2 downhillVelocity = downhill * downhillSpeed;
            Vector2 lateralVelocity = CurrentTerrainVelocity - downhillVelocity;
            lateralVelocity = Vector2.MoveTowards(
                lateralVelocity,
                Vector2.zero,
                ScaleMotion(downhillSlideLateralDamping) * Time.deltaTime);
            CurrentTerrainVelocity = downhillVelocity + lateralVelocity;
        }

        private void ApplyDownhillHeadingRecovery()
        {
            if (!IsAutoAligningDownhill
                || downhillHeadingRecoveryDirection.sqrMagnitude < 0.000001f)
            {
                return;
            }

            Vector2 downhill = downhillHeadingRecoveryDirection.normalized;
            float targetAngle = Mathf.Atan2(downhill.y, downhill.x)
                                * Mathf.Rad2Deg
                                - 90f;
            float currentAngle = transform.eulerAngles.z;
            float alignedAngle = Mathf.MoveTowardsAngle(
                currentAngle,
                targetAngle,
                ScaleMotion(downhillHeadingAlignmentSpeed) * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, alignedAngle);
        }

        private void UpdateUnstableDriftTarget()
        {
            if (Time.time >= nextUnstableDirectionChangeTime)
            {
                bool instabilityJustStarted = Mathf.Approximately(
                    unstableLateralTarget,
                    0f);
                unstableLateralTarget = Random.value < 0.5f ? -1f : 1f;
                if (instabilityJustStarted
                    && Mathf.Abs(unstableLateralBlend)
                    < levelThreeInitialLateralDriftStrength)
                {
                    unstableLateralBlend = unstableLateralTarget
                                           * levelThreeInitialLateralDriftStrength;
                }
                float intervalVariation = Random.Range(0.65f, 1.35f);
                nextUnstableDirectionChangeTime = Time.time
                                                  + levelThreeDriftDirectionChangeInterval
                                                  * intervalVariation;
            }

            unstableLateralBlend = Mathf.SmoothDamp(
                unstableLateralBlend,
                unstableLateralTarget,
                ref unstableLateralBlendVelocity,
                levelThreeDriftSmoothing);
        }

        private void RelaxUnstableDrift()
        {
            unstableLateralTarget = 0f;
            unstableLateralBlend = Mathf.SmoothDamp(
                unstableLateralBlend,
                0f,
                ref unstableLateralBlendVelocity,
                levelThreeDriftSmoothing);
        }

        private void HardStop(SlopeTraversalResult result)
        {
            CurrentSpeed = 0f;
            CurrentTerrainTurnSpeed = 0f;
            CurrentTerrainVelocity = Vector2.zero;
            IsSlopeBlocked = true;
            IsLevelThreeUnstable = false;
            IsAutoAligningDownhill = false;
            IsDownhillBoosted = false;
            ResetLevelThreeClimbFailureSequence();
            downhillHeadingRecoveryEndTime = 0f;
            downhillHeadingRecoveryDirection = Vector2.zero;
            pendingDownhillRecoveryFromLevelThreeSlip = false;
            CurrentTraversalResult = result;
            RelaxUnstableDrift();
        }

        private float DetermineProbeSign(float throttle)
        {
            if (Mathf.Abs(throttle) > 0.01f)
                return Mathf.Sign(throttle);
            if (Mathf.Abs(CurrentSpeed) > 0.05f)
                return Mathf.Sign(CurrentSpeed);
            return 1f;
        }

        private static float ReadKeyboardAxis(
            KeyCode negativeKey,
            KeyCode negativeAlternate,
            KeyCode positiveKey,
            KeyCode positiveAlternate)
        {
            bool negative = Input.GetKey(negativeKey) || Input.GetKey(negativeAlternate);
            bool positive = Input.GetKey(positiveKey) || Input.GetKey(positiveAlternate);
            return (positive ? 1f : 0f) - (negative ? 1f : 0f);
        }

        private static float SelectStrongerInput(float first, float second)
        {
            return Mathf.Abs(first) >= Mathf.Abs(second) ? first : second;
        }

        private float ApplyDeadZone(float value)
        {
            float magnitude = Mathf.Abs(value);
            if (magnitude <= stickDeadZone)
                return 0f;

            float remappedMagnitude = Mathf.InverseLerp(stickDeadZone, 1f, magnitude);
            return Mathf.Sign(value) * remappedMagnitude;
        }

        private float GetSpeedChangeRate(float throttle, float targetSpeed)
        {
            if (Mathf.Approximately(throttle, 0f))
                return ScaleMotion(coastDeceleration);

            bool isBraking = !Mathf.Approximately(CurrentSpeed, 0f)
                             && Mathf.Sign(targetSpeed) != Mathf.Sign(CurrentSpeed);
            if (isBraking)
                return ScaleMotion(brakingDeceleration);

            float relevantTopSpeed = targetSpeed >= 0f
                ? ScaleMotion(forwardSpeed)
                : ScaleMotion(reverseSpeed);
            float normalizedSpeed = relevantTopSpeed > 0f
                ? Mathf.Clamp01(Mathf.Abs(CurrentSpeed) / relevantTopSpeed)
                : 0f;

            return ScaleMotion(
                Mathf.Lerp(launchAcceleration, runningAcceleration, normalizedSpeed));
        }

        private float ScaleMotion(float value)
        {
            return value * overallMotionScale;
        }

        private void OnValidate()
        {
            overallMotionScale = Mathf.Clamp(overallMotionScale, 0f, 2f);
            forwardSpeed = Mathf.Max(0f, forwardSpeed);
            reverseSpeed = Mathf.Max(0f, reverseSpeed);
            turnSpeed = Mathf.Max(0f, turnSpeed);
            turnAcceleration = Mathf.Max(0f, turnAcceleration);
            turnDeceleration = Mathf.Max(0f, turnDeceleration);
            launchAcceleration = Mathf.Max(0f, launchAcceleration);
            runningAcceleration = Mathf.Max(0f, runningAcceleration);
            coastDeceleration = Mathf.Max(0f, coastDeceleration);
            brakingDeceleration = Mathf.Max(0f, brakingDeceleration);
            levelTwoMinimumTopSpeedMultiplier = Mathf.Clamp(
                levelTwoMinimumTopSpeedMultiplier,
                0.05f,
                1f);
            levelThreeForwardSpeedMultiplier = Mathf.Clamp01(
                levelThreeForwardSpeedMultiplier);
            levelTwoMaximumSlideSpeedFraction = Mathf.Clamp01(
                levelTwoMaximumSlideSpeedFraction);
            levelTwoSlideAcceleration = Mathf.Max(0f, levelTwoSlideAcceleration);
            levelTwoPhysicalDownhillWeight = Mathf.Clamp01(
                levelTwoPhysicalDownhillWeight);
            levelTwoLateralSlipRetention = Mathf.Clamp01(
                levelTwoLateralSlipRetention);
            levelTwoLateralGripAcceleration = Mathf.Max(
                0f,
                levelTwoLateralGripAcceleration);
            levelThreeSlideSpeedFraction = Mathf.Max(
                levelTwoMaximumSlideSpeedFraction,
                levelThreeSlideSpeedFraction);
            levelThreePhysicalDownhillWeight = Mathf.Clamp01(
                levelThreePhysicalDownhillWeight);
            levelThreeSlideAcceleration = Mathf.Max(0f, levelThreeSlideAcceleration);
            levelThreeLateralDriftSpeedFraction = Mathf.Max(
                0f,
                levelThreeLateralDriftSpeedFraction);
            levelThreeInitialLateralDriftStrength = Mathf.Clamp01(
                levelThreeInitialLateralDriftStrength);
            levelThreeDriftDirectionChangeInterval = Mathf.Max(
                0.1f,
                levelThreeDriftDirectionChangeInterval);
            levelThreeDriftSmoothing = Mathf.Max(0.01f, levelThreeDriftSmoothing);
            levelThreeGripDuration = Mathf.Max(0.05f, levelThreeGripDuration);
            levelThreeGripForwardSpeedMultiplier = Mathf.Clamp01(
                levelThreeGripForwardSpeedMultiplier);
            levelThreeGripSlideMultiplier = Mathf.Clamp01(
                levelThreeGripSlideMultiplier);
            levelThreeStrainDuration = Mathf.Max(0.05f, levelThreeStrainDuration);
            levelThreeStrainEndForwardSpeedMultiplier = Mathf.Clamp01(
                levelThreeStrainEndForwardSpeedMultiplier);
            levelThreeSlipDuration = Mathf.Max(0.05f, levelThreeSlipDuration);
            levelThreeAngularDriftSpeed = Mathf.Max(
                0f,
                levelThreeAngularDriftSpeed);
            levelThreeAngularDriftAcceleration = Mathf.Max(
                0f,
                levelThreeAngularDriftAcceleration);
            levelThreeAngularRecoveryAcceleration = Mathf.Max(
                0f,
                levelThreeAngularRecoveryAcceleration);
            downhillHeadingAlignmentSpeed = Mathf.Max(
                0f,
                downhillHeadingAlignmentSpeed);
            downhillHeadingRecoveryDuration = Mathf.Max(
                0.05f,
                downhillHeadingRecoveryDuration);
            downhillSlideLateralDamping = Mathf.Max(
                0f,
                downhillSlideLateralDamping);
            terrainVelocityRecoveryAcceleration = Mathf.Max(
                0f,
                terrainVelocityRecoveryAcceleration);
            downhillBoostStartAngle = Mathf.Clamp(downhillBoostStartAngle, 0f, 88f);
            downhillFullBoostAngle = Mathf.Clamp(
                downhillFullBoostAngle,
                downhillBoostStartAngle + 0.1f,
                89f);
            downhillMaximumSpeedMultiplier = Mathf.Max(
                1f,
                downhillMaximumSpeedMultiplier);
            downhillAccelerationBonus = Mathf.Max(0f, downhillAccelerationBonus);
        }
    }
}
