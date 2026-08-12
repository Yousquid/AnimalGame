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

        [Header("Safety Limits")]
        [Tooltip("An abrupt unsmoothed downward detail step above this size is treated as an unsupported ledge. Continuous downhill slope is never blocked by this limit.")]
        [SerializeField, Min(0f)] private float maximumGroundedDetailStepMeters = 1.8f;

        [SerializeField, Min(1)] private int maximumStepCount = 12;
        [SerializeField, Min(0.1f)] private float maximumTumbleDuration = 10f;

        public RobotTumbleState State { get; private set; } =
            RobotTumbleState.Upright;
        public RobotTumbleAxis Axis { get; private set; }
        public Vector2 DirectionWorld { get; private set; }
        public int CompletedStepCount { get; private set; }
        public int ActiveStepIndex => CompletedStepCount;
        public float StepProgress01 { get; private set; }
        public float CurrentSpecificEnergy { get; private set; }
        public float CurrentStepDistanceMeters { get; private set; }
        public float CurrentVerticalSizeMeters { get; private set; }
        public float NextVerticalSizeMeters { get; private set; }
        public float ContinuousQuarterTurnProgress =>
            CompletedStepCount
            + (activeStep ? StepProgress01 : 0f);

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
            maximumGroundedDetailStepMeters = Mathf.Max(
                0f,
                maximumGroundedDetailStepMeters);
            maximumStepCount = Mathf.Max(1, maximumStepCount);
            maximumTumbleDuration = Mathf.Max(0.1f, maximumTumbleDuration);
        }
    }
}
