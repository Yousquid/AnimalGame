using System;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public enum RobotTumbleState
    {
        Upright,
        Tumbling,
        Fallen
    }

    public enum RobotTumbleAxis
    {
        ForwardBack,
        LeftRight
    }

    public enum RobotTumbleSettleReason
    {
        None,
        EnergyDepleted,
        MissingTerrainData,
        Boundary,
        UpwardObstacle,
        DownwardLedge,
        MaximumStepCount,
        MaximumDuration,
        ExternalControlLost
    }

    public readonly struct RobotTumbleStartedInfo
    {
        public RobotTipOverInfo TipOver { get; }
        public RobotTumbleAxis Axis { get; }
        public Vector2 WorldDirection { get; }
        public float InitialMapSpeed { get; }
        public float InitialSpecificEnergy { get; }

        public RobotTumbleStartedInfo(
            RobotTipOverInfo tipOver,
            RobotTumbleAxis axis,
            Vector2 worldDirection,
            float initialMapSpeed,
            float initialSpecificEnergy)
        {
            TipOver = tipOver;
            Axis = axis;
            WorldDirection = worldDirection;
            InitialMapSpeed = initialMapSpeed;
            InitialSpecificEnergy = initialSpecificEnergy;
        }
    }

    public readonly struct RobotTumbleStepInfo
    {
        public int StepIndex { get; }
        public float DistanceMeters { get; }
        public Vector2 StartWorldPosition { get; }
        public Vector2 EndWorldPosition { get; }
        public float StartSurfaceHeightMeters { get; }
        public float EndSurfaceHeightMeters { get; }
        public float RequiredSpecificEnergy { get; }
        public float SpecificEnergyBeforeImpact { get; }
        public float RemainingSpecificEnergy { get; }
        public float ImpactLostSpecificEnergy { get; }

        public RobotTumbleStepInfo(
            int stepIndex,
            float distanceMeters,
            Vector2 startWorldPosition,
            Vector2 endWorldPosition,
            float startSurfaceHeightMeters,
            float endSurfaceHeightMeters,
            float requiredSpecificEnergy,
            float specificEnergyBeforeImpact,
            float remainingSpecificEnergy,
            float impactLostSpecificEnergy)
        {
            StepIndex = stepIndex;
            DistanceMeters = distanceMeters;
            StartWorldPosition = startWorldPosition;
            EndWorldPosition = endWorldPosition;
            StartSurfaceHeightMeters = startSurfaceHeightMeters;
            EndSurfaceHeightMeters = endSurfaceHeightMeters;
            RequiredSpecificEnergy = requiredSpecificEnergy;
            SpecificEnergyBeforeImpact = specificEnergyBeforeImpact;
            RemainingSpecificEnergy = remainingSpecificEnergy;
            ImpactLostSpecificEnergy = impactLostSpecificEnergy;
        }
    }

    public readonly struct RobotTumbleSettledInfo
    {
        public RobotTumbleSettleReason Reason { get; }
        public int CompletedStepCount { get; }
        public Vector2 WorldPosition { get; }
        public float RemainingSpecificEnergy { get; }

        public RobotTumbleSettledInfo(
            RobotTumbleSettleReason reason,
            int completedStepCount,
            Vector2 worldPosition,
            float remainingSpecificEnergy)
        {
            Reason = reason;
            CompletedStepCount = completedStepCount;
            WorldPosition = worldPosition;
            RemainingSpecificEnergy = remainingSpecificEnergy;
        }
    }

    /// <summary>
    /// Owns the authoritative planar displacement after a balance failure.
    /// Each completed step represents one discrete 90-degree rigid-body tumble.
    /// The robot root is translated but never rotated by this controller.
    /// </summary>
    [DefaultExecutionOrder(125)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMover))]
    [RequireComponent(typeof(RobotBalanceController))]
    public sealed class RobotTumbleController : MonoBehaviour
    {
        [Header("Robot Dimensions")]
        [Tooltip("Full upright robot height in logical map metres. This is deliberately separate from centre-of-mass height.")]
        [SerializeField, Min(0.1f)] private float robotHeightMeters = 1.8f;

        [Header("Specific Energy Model")]
        [Tooltip("Gravity used by the tumble energy calculation, in logical map metres per second squared.")]
        [SerializeField, Min(0.01f)] private float gravityMetersPerSecondSquared = 9.81f;

        [Tooltip("Effective rotational inertia multiplier in e = 0.5 * factor * speed squared.")]
        [SerializeField, Min(0.01f)] private float effectiveInertiaFactor = 1.3f;

        [Tooltip("Fraction of kinetic energy retained after each 90-degree landing impact.")]
        [SerializeField, Range(0f, 1f)] private float impactEnergyRetention = 0.42f;

        [Tooltip("Dimensionless rolling and deformation resistance applied over each requested displacement.")]
        [SerializeField, Min(0f)] private float rollingResistanceCoefficient = 0.05f;

        [Tooltip("Additional specific-energy reserve required to begin a later tumble step.")]
        [SerializeField, Min(0f)] private float energySafetyMargin = 0.05f;

        [Tooltip("Minimum equivalent speed required to begin any step after the committed first fall.")]
        [SerializeField, Min(0f)] private float minimumContinuationSpeed = 0.25f;

        [Tooltip("Small committed speed reserve injected into the mandatory first fall after its terrain profile is accepted.")]
        [SerializeField, Min(0f)] private float firstTipCommitSpeed = 0.3f;

        [Tooltip("Converts centre-of-mass overflow beyond the support boundary into extra initial specific energy.")]
        [SerializeField, Min(0f)] private float balanceOverflowEnergyScale = 0.25f;

        [Header("Tumble Motion")]
        [Tooltip("Slowest visualized planar speed used to keep a committed step moving smoothly.")]
        [SerializeField, Min(0.01f)] private float minimumStepMotionSpeed = 0.8f;

        [SerializeField, Min(0.01f)] private float minimumStepDuration = 0.18f;
        [SerializeField, Min(0.01f)] private float maximumStepDuration = 0.8f;

        [Header("Tumble Balance Display")]
        [Tooltip("Preferred normalized centre-of-mass distance while tumbling. Values above one place the point outside the support ring.")]
        [SerializeField, Range(1f, 1.5f)] private float outerBalanceMagnitude = 1.1f;

        [Tooltip("Closest normalized distance reached during the brief inward part of a strong tumble swing.")]
        [SerializeField, Range(0.9f, 1f)] private float minimumSwingMagnitude = 0.98f;

        [Tooltip("Higher values shorten the part of each tumble during which the centre-of-mass point moves close to or inside the support ring.")]
        [SerializeField, Range(2f, 16f)] private float inwardSwingSharpness = 8f;

        [Tooltip("Maximum alternating sideways arc of the simulated centre-of-mass point during each tumble step.")]
        [SerializeField, Range(0f, 30f)] private float lateralSwingDegrees = 12f;

        [Tooltip("Additional visual swing reduction per completed tumble, on top of the remaining-energy reduction.")]
        [SerializeField, Range(0f, 0.5f)] private float laterStepSwingDamping = 0.08f;

        [Header("Safety Limits")]
        [Tooltip("An abrupt unsmoothed downward detail step above this size is treated as an unsupported ledge. Continuous downhill slope is never blocked by this limit.")]
        [SerializeField, Min(0f)] private float maximumGroundedDetailStepMeters = 1.8f;

        [SerializeField, Min(1)] private int maximumStepCount = 12;
        [SerializeField, Min(0.1f)] private float maximumTumbleDuration = 10f;

        public RobotTumbleState State { get; private set; } =
            RobotTumbleState.Upright;
        public RobotTumbleAxis Axis { get; private set; }
        public Vector2 DirectionWorld { get; private set; }
        public int QuarterTurnSign { get; private set; } = 1;
        public int CompletedStepCount { get; private set; }
        public int ActiveStepIndex => CompletedStepCount;
        public float StepProgress01 { get; private set; }
        public float CurrentSpecificEnergy { get; private set; }
        public float CurrentStepDistanceMeters { get; private set; }
        public float CurrentVerticalSizeMeters { get; private set; }
        public float NextVerticalSizeMeters { get; private set; }
        public RobotBalanceState TumbleBalanceState { get; private set; }
        public bool HasTumbleBalanceState => State != RobotTumbleState.Upright;
        public float ContinuousQuarterTurnProgress =>
            CompletedStepCount
            + (activeStep ? StepProgress01 : 0f);
        public float SignedContinuousQuarterTurnProgress =>
            QuarterTurnSign * ContinuousQuarterTurnProgress;

        public event Action<RobotTumbleStartedInfo> Started;
        public event Action<RobotTumbleStepInfo> StepCompleted;
        public event Action<RobotTumbleSettledInfo> Settled;

        private RobotMover mover;
        private RobotBalanceController balance;
        private HeightMapTraversalEvaluator evaluator;
        private RobotTipOverInfo triggerInfo;
        private float axisSpanMeters;
        private float sweepWidthMeters;
        private float tumbleStartTime;
        private float initialMapSpeedAtTrigger;
        private float initialSpecificEnergyAtTrigger;
        private bool activeStep;
        private TumbleTerrainSegment activeTerrain;
        private Vector3 stepStartWorld;
        private Vector3 stepEndWorld;
        private float stepElapsed;
        private float stepDuration;
        private float stepRequiredSpecificEnergy;
        private float triggerBalanceMagnitude;
        private float tumbleBalanceOuterMagnitude;
        private float tumbleBalanceEnergyReference;

        private void Awake()
        {
            mover = GetComponent<RobotMover>();
            balance = GetComponent<RobotBalanceController>();
        }

        private void OnEnable()
        {
            if (balance == null)
                balance = GetComponent<RobotBalanceController>();
            if (balance != null)
                balance.TippedOver += HandleTippedOver;
        }

        private void Start()
        {
            if (balance != null && balance.IsTippedOver)
                HandleTippedOver(balance.CurrentTipOver);
        }

        private void OnDisable()
        {
            if (balance != null)
                balance.TippedOver -= HandleTippedOver;
        }

        public void Initialize(HeightMapTraversalEvaluator traversalEvaluator)
        {
            evaluator = traversalEvaluator;
        }

        private void Update()
        {
            if (State != RobotTumbleState.Tumbling)
                return;

            if (mover == null || !mover.IsExternallyTumbling)
            {
                Settle(RobotTumbleSettleReason.ExternalControlLost);
                return;
            }

            if (Time.time - tumbleStartTime >= maximumTumbleDuration)
            {
                Settle(RobotTumbleSettleReason.MaximumDuration);
                return;
            }

            if (!activeStep)
                return;

            stepElapsed += Mathf.Max(0f, Time.deltaTime);
            StepProgress01 = Mathf.Clamp01(
                stepElapsed / Mathf.Max(0.01f, stepDuration));
            float easedProgress = StepProgress01 * StepProgress01
                                  * (3f - 2f * StepProgress01);
            Vector3 nextPosition = Vector3.LerpUnclamped(
                stepStartWorld,
                stepEndWorld,
                easedProgress);
            nextPosition.z = stepStartWorld.z;
            transform.position = nextPosition;
            UpdateTumbleBalanceState(StepProgress01);

            if (StepProgress01 >= 1f)
                CompleteActiveStep();
        }

        private void HandleTippedOver(RobotTipOverInfo tipOverInfo)
        {
            if (State != RobotTumbleState.Upright || mover == null)
                return;

            Vector2 capturedWorldVelocity = mover.BeginExternalTumble();
            if (!mover.IsExternallyTumbling)
                return;

            triggerInfo = tipOverInfo;
            ChooseTumbleAxisAndDirection(tipOverInfo);
            State = RobotTumbleState.Tumbling;
            tumbleStartTime = Time.time;
            CompletedStepCount = 0;
            StepProgress01 = 0f;
            triggerBalanceMagnitude = Mathf.Max(
                1.001f,
                tipOverInfo.TriggerState.Magnitude);
            tumbleBalanceOuterMagnitude = Mathf.Max(
                triggerBalanceMagnitude,
                outerBalanceMagnitude);
            tumbleBalanceEnergyReference = 0f;
            UpdateTumbleBalanceState(0f);

            if (evaluator == null || !evaluator.IsInitialized)
            {
                Settle(RobotTumbleSettleReason.MissingTerrainData);
                return;
            }

            axisSpanMeters = Axis == RobotTumbleAxis.ForwardBack
                ? evaluator.RobotFootprintLengthMeters
                : evaluator.RobotFootprintWidthMeters;
            sweepWidthMeters = Axis == RobotTumbleAxis.ForwardBack
                ? evaluator.RobotFootprintWidthMeters
                : evaluator.RobotFootprintLengthMeters;

            float forwardWorldSpeed = Mathf.Max(
                0f,
                Vector2.Dot(capturedWorldVelocity, DirectionWorld));
            initialMapSpeedAtTrigger = evaluator.WorldSpeedToMapSpeed(
                DirectionWorld * forwardWorldSpeed);
            float overflow = Mathf.Max(
                0f,
                tipOverInfo.TriggerState.Magnitude - 1f);
            CurrentSpecificEnergy = CalculateKineticSpecificEnergy(
                                        initialMapSpeedAtTrigger)
                                    + gravityMetersPerSecondSquared
                                    * robotHeightMeters
                                    * balanceOverflowEnergyScale
                                    * overflow;
            initialSpecificEnergyAtTrigger = CurrentSpecificEnergy;

            TryStartNextStep(true);
        }

        private void ChooseTumbleAxisAndDirection(RobotTipOverInfo tipOverInfo)
        {
            Vector2 localDirection = tipOverInfo.LocalDirection;
            bool forwardBack = Mathf.Abs(localDirection.y)
                               >= Mathf.Abs(localDirection.x);
            Axis = forwardBack
                ? RobotTumbleAxis.ForwardBack
                : RobotTumbleAxis.LeftRight;

            float sign = forwardBack
                ? Mathf.Sign(localDirection.y)
                : Mathf.Sign(localDirection.x);
            if (Mathf.Approximately(sign, 0f))
                sign = 1f;
            // This is the deterministic fallback used by screen-space effects
            // when the world tumble direction projects almost vertically.
            QuarterTurnSign = sign > 0f ? -1 : 1;

            DirectionWorld = tipOverInfo.WorldDirection;
            if (DirectionWorld.sqrMagnitude >= 0.000001f)
                DirectionWorld.Normalize();
            else
            {
                DirectionWorld = (forwardBack
                        ? (Vector2)transform.up
                        : (Vector2)transform.right)
                    * sign;
                if (DirectionWorld.sqrMagnitude < 0.000001f)
                    DirectionWorld = Vector2.right;
                else
                    DirectionWorld.Normalize();
            }
        }

        private void TryStartNextStep(bool forceCommittedFirstStep)
        {
            if (State != RobotTumbleState.Tumbling)
                return;

            if (CompletedStepCount >= maximumStepCount)
            {
                Settle(RobotTumbleSettleReason.MaximumStepCount);
                return;
            }

            bool uprightPhase = (CompletedStepCount & 1) == 0;
            CurrentVerticalSizeMeters = uprightPhase
                ? robotHeightMeters
                : axisSpanMeters;
            NextVerticalSizeMeters = uprightPhase
                ? axisSpanMeters
                : robotHeightMeters;
            CurrentStepDistanceMeters = uprightPhase
                ? robotHeightMeters
                : axisSpanMeters;

            if (!evaluator.TryEvaluateTumbleSegment(
                    transform.position,
                    DirectionWorld,
                    CurrentStepDistanceMeters,
                    sweepWidthMeters,
                    out TumbleTerrainSegment terrain)
                || !terrain.HasData)
            {
                Settle(RobotTumbleSettleReason.MissingTerrainData);
                return;
            }

            if (!terrain.IsComplete || terrain.HitBoundary)
            {
                Settle(RobotTumbleSettleReason.Boundary);
                return;
            }

            if (terrain.HasUpwardObstacle)
            {
                Settle(RobotTumbleSettleReason.UpwardObstacle);
                return;
            }

            // Only an abrupt detail-channel ledge can defer this grounded model.
            // The total surface drop is deliberately unrestricted so even very
            // steep continuous downhill terrain never re-enters slope blocking.
            if (terrain.MaximumDownwardDetailStepMeters
                > maximumGroundedDetailStepMeters)
            {
                Settle(RobotTumbleSettleReason.DownwardLedge);
                return;
            }

            stepRequiredSpecificEnergy = CalculateRequiredSpecificEnergy(
                terrain,
                CurrentVerticalSizeMeters,
                NextVerticalSizeMeters,
                CurrentStepDistanceMeters);

            if (forceCommittedFirstStep)
            {
                float commitReserve = CalculateKineticSpecificEnergy(
                    firstTipCommitSpeed);
                CurrentSpecificEnergy = Mathf.Max(
                    CurrentSpecificEnergy,
                    stepRequiredSpecificEnergy + commitReserve);
                tumbleBalanceEnergyReference = Mathf.Max(
                    0.0001f,
                    CurrentSpecificEnergy);
            }
            else
            {
                float minimumKineticEnergy = CalculateKineticSpecificEnergy(
                    minimumContinuationSpeed);
                float requiredToContinue = Mathf.Max(
                    stepRequiredSpecificEnergy,
                    minimumKineticEnergy);
                if (CurrentSpecificEnergy + 0.00001f < requiredToContinue)
                {
                    Settle(RobotTumbleSettleReason.EnergyDepleted);
                    return;
                }
            }

            activeTerrain = terrain;
            stepStartWorld = transform.position;
            stepEndWorld = new Vector3(
                terrain.EndWorldPosition.x,
                terrain.EndWorldPosition.y,
                stepStartWorld.z);
            stepElapsed = 0f;
            StepProgress01 = 0f;

            float equivalentSpeed = Mathf.Sqrt(
                2f * Mathf.Max(0f, CurrentSpecificEnergy)
                / Mathf.Max(0.01f, effectiveInertiaFactor));
            float planarSpeed = Mathf.Max(minimumStepMotionSpeed, equivalentSpeed);
            stepDuration = Mathf.Clamp(
                CurrentStepDistanceMeters / planarSpeed,
                minimumStepDuration,
                maximumStepDuration);
            activeStep = true;
            UpdateTumbleBalanceState(0f);

            if (forceCommittedFirstStep)
            {
                Started?.Invoke(new RobotTumbleStartedInfo(
                    triggerInfo,
                    Axis,
                    DirectionWorld,
                    initialMapSpeedAtTrigger,
                    initialSpecificEnergyAtTrigger));
            }
        }

        private float CalculateRequiredSpecificEnergy(
            TumbleTerrainSegment terrain,
            float currentVerticalSize,
            float nextVerticalSize,
            float distanceMeters)
        {
            float rotationRadius = 0.5f * Mathf.Sqrt(
                currentVerticalSize * currentVerticalSize
                + nextVerticalSize * nextVerticalSize);
            float bodyCentreOfMassBarrier = Mathf.Max(
                0f,
                rotationRadius - currentVerticalSize * 0.5f);
            float effectiveRise = Mathf.Max(
                0f,
                terrain.MaximumPositiveRiseMeters
                + bodyCentreOfMassBarrier);
            float rollingWork = rollingResistanceCoefficient
                                * gravityMetersPerSecondSquared
                                * distanceMeters;
            return gravityMetersPerSecondSquared * effectiveRise
                   + rollingWork
                   + energySafetyMargin;
        }

        private void CompleteActiveStep()
        {
            activeStep = false;
            StepProgress01 = 1f;
            transform.position = stepEndWorld;

            float terrainHeightDelta = activeTerrain.EndSurfaceHeightMeters
                                       - activeTerrain.StartSurfaceHeightMeters;
            float bodyCentreOfMassHeightDelta =
                (NextVerticalSizeMeters - CurrentVerticalSizeMeters) * 0.5f;
            float rollingWork = rollingResistanceCoefficient
                                * gravityMetersPerSecondSquared
                                * CurrentStepDistanceMeters;
            float energyBeforeImpact = Mathf.Max(
                0f,
                CurrentSpecificEnergy
                - gravityMetersPerSecondSquared
                * (terrainHeightDelta + bodyCentreOfMassHeightDelta)
                - rollingWork);
            float remainingEnergy = energyBeforeImpact
                                    * impactEnergyRetention;
            float impactLostEnergy = energyBeforeImpact - remainingEnergy;

            int completedIndex = CompletedStepCount;
            CurrentSpecificEnergy = remainingEnergy;
            CompletedStepCount++;

            StepCompleted?.Invoke(new RobotTumbleStepInfo(
                completedIndex,
                CurrentStepDistanceMeters,
                stepStartWorld,
                stepEndWorld,
                activeTerrain.StartSurfaceHeightMeters,
                activeTerrain.EndSurfaceHeightMeters,
                stepRequiredSpecificEnergy,
                energyBeforeImpact,
                remainingEnergy,
                impactLostEnergy));

            if (CompletedStepCount >= maximumStepCount)
            {
                Settle(RobotTumbleSettleReason.MaximumStepCount);
                return;
            }

            if (Time.time - tumbleStartTime >= maximumTumbleDuration)
            {
                Settle(RobotTumbleSettleReason.MaximumDuration);
                return;
            }

            TryStartNextStep(false);
        }

        private float CalculateKineticSpecificEnergy(float mapSpeed)
        {
            return 0.5f
                   * effectiveInertiaFactor
                   * mapSpeed
                   * mapSpeed;
        }

        private void UpdateTumbleBalanceState(float stepProgress01)
        {
            float progress = Mathf.Clamp01(stepProgress01);
            float smoothedProgress = progress * progress
                                     * (3f - 2f * progress);
            float baseMagnitude = CompletedStepCount == 0
                ? Mathf.Lerp(
                    triggerBalanceMagnitude,
                    tumbleBalanceOuterMagnitude,
                    smoothedProgress)
                : tumbleBalanceOuterMagnitude;

            float energyStrength = tumbleBalanceEnergyReference > 0.0001f
                ? Mathf.Sqrt(Mathf.Clamp01(
                    CurrentSpecificEnergy / tumbleBalanceEnergyReference))
                : 1f;
            float laterStepStrength = 1f
                                      / (1f
                                         + CompletedStepCount
                                         * laterStepSwingDamping);
            float swingStrength = Mathf.Clamp01(
                energyStrength * laterStepStrength);
            float halfTurnWave = Mathf.Sin(progress * Mathf.PI);
            float narrowInwardWindow = Mathf.Pow(
                Mathf.Max(0f, halfTurnWave),
                inwardSwingSharpness);
            float inwardMagnitude = Mathf.Lerp(
                baseMagnitude,
                Mathf.Min(baseMagnitude, minimumSwingMagnitude),
                swingStrength);
            float displayedMagnitude = Mathf.Lerp(
                baseMagnitude,
                inwardMagnitude,
                narrowInwardWindow);

            float alternatingSign = (CompletedStepCount & 1) == 0
                ? 1f
                : -1f;
            float lateralAngleRadians = lateralSwingDegrees
                                        * Mathf.Deg2Rad
                                        * halfTurnWave
                                        * swingStrength
                                        * alternatingSign;
            Vector2 baseDirection = DirectionWorld.sqrMagnitude > 0.000001f
                ? DirectionWorld.normalized
                : Vector2.right;
            float cosine = Mathf.Cos(lateralAngleRadians);
            float sine = Mathf.Sin(lateralAngleRadians);
            Vector2 displayedWorldDirection = new Vector2(
                baseDirection.x * cosine - baseDirection.y * sine,
                baseDirection.x * sine + baseDirection.y * cosine);
            Vector2 displayedWorldOffset = displayedWorldDirection
                                           * displayedMagnitude;
            Vector2 displayedLocalDirection = new Vector2(
                Vector2.Dot(displayedWorldDirection, transform.right),
                Vector2.Dot(displayedWorldDirection, transform.up));
            if (displayedLocalDirection.sqrMagnitude > 0.000001f)
                displayedLocalDirection.Normalize();

            RobotBalanceLevel level = displayedMagnitude >= 1f
                ? RobotBalanceLevel.OutsideSupport
                : RobotBalanceLevel.Critical;
            TumbleBalanceState = new RobotBalanceState(
                displayedLocalDirection * displayedMagnitude,
                displayedWorldOffset,
                Vector2.zero,
                displayedMagnitude,
                Mathf.InverseLerp(0.75f, 1f, displayedMagnitude),
                level);
        }

        private void Settle(RobotTumbleSettleReason reason)
        {
            if (State == RobotTumbleState.Fallen)
                return;

            activeStep = false;
            State = RobotTumbleState.Fallen;
            mover?.MarkFallenPermanently();
            Settled?.Invoke(new RobotTumbleSettledInfo(
                reason,
                CompletedStepCount,
                transform.position,
                CurrentSpecificEnergy));
        }

        private void OnValidate()
        {
            robotHeightMeters = Mathf.Max(0.1f, robotHeightMeters);
            gravityMetersPerSecondSquared = Mathf.Max(
                0.01f,
                gravityMetersPerSecondSquared);
            effectiveInertiaFactor = Mathf.Max(0.01f, effectiveInertiaFactor);
            impactEnergyRetention = Mathf.Clamp01(impactEnergyRetention);
            rollingResistanceCoefficient = Mathf.Max(
                0f,
                rollingResistanceCoefficient);
            energySafetyMargin = Mathf.Max(0f, energySafetyMargin);
            minimumContinuationSpeed = Mathf.Max(0f, minimumContinuationSpeed);
            firstTipCommitSpeed = Mathf.Max(0f, firstTipCommitSpeed);
            balanceOverflowEnergyScale = Mathf.Max(0f, balanceOverflowEnergyScale);
            minimumStepMotionSpeed = Mathf.Max(0.01f, minimumStepMotionSpeed);
            minimumStepDuration = Mathf.Max(0.01f, minimumStepDuration);
            maximumStepDuration = Mathf.Max(
                minimumStepDuration,
                maximumStepDuration);
            outerBalanceMagnitude = Mathf.Clamp(
                outerBalanceMagnitude,
                1.001f,
                1.5f);
            minimumSwingMagnitude = Mathf.Clamp(
                minimumSwingMagnitude,
                0.9f,
                1f);
            inwardSwingSharpness = Mathf.Clamp(
                inwardSwingSharpness,
                2f,
                16f);
            lateralSwingDegrees = Mathf.Clamp(
                lateralSwingDegrees,
                0f,
                30f);
            laterStepSwingDamping = Mathf.Clamp(
                laterStepSwingDamping,
                0f,
                0.5f);
            maximumGroundedDetailStepMeters = Mathf.Max(
                0f,
                maximumGroundedDetailStepMeters);
            maximumStepCount = Mathf.Max(1, maximumStepCount);
            maximumTumbleDuration = Mathf.Max(0.1f, maximumTumbleDuration);
        }
    }
}
