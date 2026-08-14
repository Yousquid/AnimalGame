using System;
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public enum RobotTumbleState
    {
        Upright,
        Tumbling,
        FinalRocking,
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
        public bool RecoveredUpright { get; }

        public RobotTumbleSettledInfo(
            RobotTumbleSettleReason reason,
            int completedStepCount,
            Vector2 worldPosition,
            float remainingSpecificEnergy,
            bool recoveredUpright)
        {
            Reason = reason;
            CompletedStepCount = completedStepCount;
            WorldPosition = worldPosition;
            RemainingSpecificEnergy = remainingSpecificEnergy;
            RecoveredUpright = recoveredUpright;
        }
    }

    public readonly struct RobotTumbleFinalRockInfo
    {
        public float Duration { get; }
        public float MaximumAngleDegrees { get; }
        public float EnergyCloseness { get; }

        public RobotTumbleFinalRockInfo(
            float duration,
            float maximumAngleDegrees,
            float energyCloseness)
        {
            Duration = duration;
            MaximumAngleDegrees = maximumAngleDegrees;
            EnergyCloseness = energyCloseness;
        }
    }

    public readonly struct RobotTumbleRockImpactInfo
    {
        public int ImpactIndex { get; }
        public Vector2 WorldDirection { get; }
        public float Strength { get; }

        public RobotTumbleRockImpactInfo(
            int impactIndex,
            Vector2 worldDirection,
            float strength)
        {
            ImpactIndex = impactIndex;
            WorldDirection = worldDirection;
            Strength = strength;
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
        private const int StepsPerFullRotation = 4;

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

        [Header("Final Settling Rock")]
        [Tooltip("Time spent rocking around the final resting face after continuation energy is depleted.")]
        [SerializeField, Min(0.1f)] private float finalRockDuration = 1.2f;

        [Tooltip("Smallest first rebound angle after the last committed tumble landing.")]
        [SerializeField, Range(0f, 30f)] private float finalRockMinimumAngleDegrees = 6f;

        [Tooltip("Largest first rebound angle when the robot nearly had enough energy for another tumble.")]
        [SerializeField, Range(0f, 30f)] private float finalRockMaximumAngleDegrees = 18f;

        [Tooltip("Maximum sideways arc used by the centre-of-mass display while the chassis settles.")]
        [SerializeField, Range(0f, 30f)] private float finalRockBalanceSwingDegrees = 14f;

        [Header("Tumble Balance Display")]
        [Tooltip("Preferred normalized centre-of-mass distance while tumbling. Values above one place the point outside the support ring.")]
        [SerializeField, Range(1f, 2f)] private float outerBalanceMagnitude = 1.66f;

        [Tooltip("Closest normalized distance reached during the brief inward part of a strong tumble swing.")]
        [SerializeField, Range(0.9f, 1f)] private float minimumSwingMagnitude = 0.98f;

        [Tooltip("Higher values shorten the part of each tumble during which the centre-of-mass point moves close to or inside the support ring.")]
        [SerializeField, Range(2f, 16f)] private float inwardSwingSharpness = 8f;

        [Tooltip("Maximum alternating sideways arc of the simulated centre-of-mass point during each tumble step.")]
        [SerializeField, Range(0f, 30f)] private float lateralSwingDegrees = 12f;

        [Tooltip("Additional visual swing reduction per completed tumble, on top of the remaining-energy reduction.")]
        [SerializeField, Range(0f, 0.5f)] private float laterStepSwingDamping = 0.08f;

        [Tooltip("Extra outward arc produced by rotating the centre of mass around the active contact edge. Robot height and the active length/width axis shape this amount.")]
        [SerializeField, Range(0f, 0.35f)] private float tumbleBalanceArcOutwardScale = 0.14f;

        [Tooltip("Maximum outward centre-of-mass kick injected by each completed 90-degree landing.")]
        [SerializeField, Range(0f, 0.35f)] private float tumbleBalanceLandingKick = 0.12f;

        [Tooltip("Time used by the landing kick to rebound and settle into the following tumble arc.")]
        [SerializeField, Range(0.05f, 0.5f)] private float tumbleBalanceLandingReboundDuration = 0.18f;

        [Header("Upright Balance Recovery")]
        [Tooltip("Visual-only time used to carry the final tumble balance point back into live upright balance after a complete 360-degree rotation.")]
        [SerializeField, Range(0.1f, 1.5f)] private float uprightBalanceRecoveryDuration = 0.58f;

        [Tooltip("Maximum opposite-side overshoot relative to the balance displacement captured at the end of the tumble.")]
        [SerializeField, Range(0f, 0.35f)] private float uprightBalanceRecoveryOvershootRatio = 0.12f;

        [Tooltip("Number of damped visual oscillations made before the recovered point hands off to live balance.")]
        [SerializeField, Range(0.5f, 2f)] private float uprightBalanceRecoveryOscillationCount = 1f;

        [Tooltip("Seconds of final displayed balance velocity carried into the upright recovery curve.")]
        [SerializeField, Range(0f, 0.25f)] private float uprightBalanceVelocityCarrySeconds = 0.08f;

        [Tooltip("How strongly the final rocking angle increases the recovery overshoot.")]
        [SerializeField, Range(0f, 1f)] private float finalRockRecoveryInfluence = 0.5f;

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
        public float FinalRockProgress01 { get; private set; }
        public float FinalRockOffsetDegrees { get; private set; }
        public float FinalRockMaximumAngleDegrees { get; private set; }
        public float FinalRockNormalizedOffset =>
            FinalRockMaximumAngleDegrees > 0.0001f
                ? FinalRockOffsetDegrees / FinalRockMaximumAngleDegrees
                : 0f;
        public RobotBalanceState TumbleBalanceState { get; private set; }
        public bool IsBalanceRecoveryActive { get; private set; }
        public float BalanceRecoveryProgress01 { get; private set; }
        public bool IsSelfRightingVisualActive { get; private set; }
        public float SelfRightingVisualProgress01 { get; private set; }
        public bool HasTumbleBalanceState => State != RobotTumbleState.Upright
                                             || IsBalanceRecoveryActive;
        public float ContinuousQuarterTurnProgress
        {
            get
            {
                if (IsSelfRightingVisualActive)
                {
                    return Mathf.Lerp(
                        selfRightingStartQuarterTurns,
                        selfRightingTargetQuarterTurns,
                        Mathf.SmoothStep(
                            0f,
                            1f,
                            SelfRightingVisualProgress01));
                }

                return CompletedStepCount
                       + (activeStep
                           ? Mathf.SmoothStep(0f, 1f, StepProgress01)
                           : 0f)
                       + (State == RobotTumbleState.FinalRocking
                           ? FinalRockOffsetDegrees / 90f
                           : 0f);
            }
        }
        public float SignedContinuousQuarterTurnProgress =>
            QuarterTurnSign * ContinuousQuarterTurnProgress;

        public event Action<RobotTumbleStartedInfo> Started;
        public event Action<RobotTumbleStepInfo> StepCompleted;
        public event Action<RobotTumbleFinalRockInfo> FinalRockingStarted;
        public event Action<RobotTumbleRockImpactInfo> RockImpact;
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
        private float lastStepImpactLostSpecificEnergy;
        private float finalRockElapsed;
        private int nextFinalRockImpactIndex;
        private float balanceLandingImpactStartTime = float.NegativeInfinity;
        private float balanceLandingImpactStrength;
        private float balanceRecoveryElapsed;
        private float balanceRecoveryOvershootScale;
        private Vector2 balanceRecoveryStartWorldOffset;
        private Vector2 balanceRecoveryStartVelocity;
        private Vector2 lastPublishedBalanceWorldOffset;
        private Vector2 publishedBalanceVelocity;
        private int lastBalancePublishFrame = -1;
        private bool finalRockWillRecoverUpright;
        private float selfRightingStartQuarterTurns;
        private float selfRightingTargetQuarterTurns;

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

        internal bool BeginSelfRightingVisual()
        {
            if (State != RobotTumbleState.Fallen)
                return false;

            selfRightingStartQuarterTurns = CompletedStepCount;
            float lowerUprightQuarterTurns = Mathf.Floor(
                selfRightingStartQuarterTurns / StepsPerFullRotation)
                * StepsPerFullRotation;
            float upperUprightQuarterTurns = lowerUprightQuarterTurns
                                             + StepsPerFullRotation;
            selfRightingTargetQuarterTurns =
                Mathf.Abs(selfRightingStartQuarterTurns
                          - lowerUprightQuarterTurns)
                <= Mathf.Abs(upperUprightQuarterTurns
                             - selfRightingStartQuarterTurns)
                    ? lowerUprightQuarterTurns
                    : upperUprightQuarterTurns;
            SelfRightingVisualProgress01 = 0f;
            IsSelfRightingVisualActive = true;
            return true;
        }

        internal void UpdateSelfRightingVisualProgress(float progress01)
        {
            if (!IsSelfRightingVisualActive)
                return;
            SelfRightingVisualProgress01 = Mathf.Clamp01(progress01);
        }

        internal void CancelSelfRightingVisual()
        {
            IsSelfRightingVisualActive = false;
            SelfRightingVisualProgress01 = 0f;
        }

        internal bool CompleteSelfRighting(
            Vector2 landingBalanceWorldDirection,
            Vector2 inertiaWorldDirection,
            float landingBalanceMagnitude,
            float inertiaMagnitude,
            float inertiaDuration)
        {
            if (State != RobotTumbleState.Fallen
                || !IsSelfRightingVisualActive)
            {
                return false;
            }

            IsSelfRightingVisualActive = false;
            SelfRightingVisualProgress01 = 0f;
            IsBalanceRecoveryActive = false;
            BalanceRecoveryProgress01 = 0f;
            State = RobotTumbleState.Upright;
            CompletedStepCount = 0;
            CurrentSpecificEnergy = 0f;
            StepProgress01 = 0f;
            FinalRockProgress01 = 0f;
            FinalRockOffsetDegrees = 0f;
            FinalRockMaximumAngleDegrees = 0f;
            balance?.RestoreUprightFromSelfRighting(
                landingBalanceWorldDirection,
                inertiaWorldDirection,
                landingBalanceMagnitude,
                inertiaMagnitude,
                inertiaDuration);
            mover?.RestoreDrivenAfterCompletedTumble();
            return true;
        }

        private void Update()
        {
            if (IsBalanceRecoveryActive)
            {
                UpdateUprightBalanceRecovery();
                return;
            }

            if (State == RobotTumbleState.FinalRocking)
            {
                if (mover == null || !mover.IsExternallyTumbling)
                {
                    Settle(RobotTumbleSettleReason.ExternalControlLost);
                    return;
                }

                UpdateFinalRocking();
                return;
            }

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

            IsBalanceRecoveryActive = false;
            BalanceRecoveryProgress01 = 0f;

            Vector2 capturedWorldVelocity = mover.BeginExternalTumble();
            if (!mover.IsExternallyTumbling)
                return;

            triggerInfo = tipOverInfo;
            CancelSelfRightingVisual();
            ChooseTumbleAxisAndDirection(tipOverInfo);
            State = RobotTumbleState.Tumbling;
            tumbleStartTime = Time.time;
            CompletedStepCount = 0;
            StepProgress01 = 0f;
            FinalRockProgress01 = 0f;
            FinalRockOffsetDegrees = 0f;
            FinalRockMaximumAngleDegrees = 0f;
            finalRockWillRecoverUpright = false;
            lastStepImpactLostSpecificEnergy = 0f;
            balanceLandingImpactStartTime = float.NegativeInfinity;
            balanceLandingImpactStrength = 0f;
            publishedBalanceVelocity = Vector2.zero;
            lastPublishedBalanceWorldOffset = Vector2.zero;
            lastBalancePublishFrame = -1;
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
                    BeginFinalRocking(requiredToContinue);
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
            lastStepImpactLostSpecificEnergy = impactLostEnergy;
            StartBalanceLandingImpact(
                impactLostEnergy,
                Mathf.Max(0.01f, stepRequiredSpecificEnergy));

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

        private void BeginFinalRocking(float requiredToContinue)
        {
            activeStep = false;
            State = RobotTumbleState.FinalRocking;
            finalRockElapsed = 0f;
            nextFinalRockImpactIndex = 0;
            FinalRockProgress01 = 0f;
            FinalRockOffsetDegrees = 0f;

            float energyCloseness = requiredToContinue > 0.0001f
                ? Mathf.Clamp01(CurrentSpecificEnergy / requiredToContinue)
                : 0f;
            float impactContribution = Mathf.Sqrt(Mathf.Clamp01(
                lastStepImpactLostSpecificEnergy
                / Mathf.Max(0.01f, requiredToContinue)));
            float rockingStrength = Mathf.Clamp01(
                energyCloseness * 0.75f + impactContribution * 0.25f);
            FinalRockMaximumAngleDegrees = Mathf.Lerp(
                finalRockMinimumAngleDegrees,
                finalRockMaximumAngleDegrees,
                rockingStrength);
            finalRockWillRecoverUpright = CompletedStepCount > 0
                                          && CompletedStepCount
                                          % StepsPerFullRotation == 0;
            UpdateFinalRockBalanceState(0f, 0f);
            FinalRockingStarted?.Invoke(new RobotTumbleFinalRockInfo(
                finalRockDuration,
                FinalRockMaximumAngleDegrees,
                energyCloseness));
        }

        private void UpdateFinalRocking()
        {
            finalRockElapsed += Mathf.Max(0f, Time.deltaTime);
            FinalRockProgress01 = Mathf.Clamp01(
                finalRockElapsed / Mathf.Max(0.1f, finalRockDuration));
            FinalRockOffsetDegrees = EvaluateFinalRockOffset(
                FinalRockProgress01)
                * FinalRockMaximumAngleDegrees;
            UpdateFinalRockBalanceState(
                FinalRockNormalizedOffset,
                FinalRockProgress01);

            while (nextFinalRockImpactIndex < 2
                   && FinalRockProgress01
                   >= GetFinalRockImpactProgress(nextFinalRockImpactIndex))
            {
                int impactIndex = nextFinalRockImpactIndex++;
                Vector2 impactDirection = DirectionWorld
                                          * (impactIndex == 0 ? -1f : 1f);
                float strength = impactIndex == 0 ? 0.46f : 0.28f;
                RockImpact?.Invoke(new RobotTumbleRockImpactInfo(
                    impactIndex,
                    impactDirection,
                    strength));
            }

            if (FinalRockProgress01 < 1f)
                return;

            FinalRockOffsetDegrees = 0f;
            UpdateFinalRockBalanceState(0f, 1f);
            Settle(RobotTumbleSettleReason.EnergyDepleted);
        }

        private static float EvaluateFinalRockOffset(float progress01)
        {
            float progress = Mathf.Clamp01(progress01);
            if (progress < 0.22f)
                return SmoothSegment(0f, -1f, progress / 0.22f);
            if (progress < 0.5f)
                return SmoothSegment(-1f, 0.5f, (progress - 0.22f) / 0.28f);
            if (progress < 0.74f)
                return SmoothSegment(0.5f, -0.2f, (progress - 0.5f) / 0.24f);

            return SmoothSegment(-0.2f, 0f, (progress - 0.74f) / 0.26f);
        }

        private static float SmoothSegment(float from, float to, float progress)
        {
            float smooth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            return Mathf.LerpUnclamped(from, to, smooth);
        }

        private static float GetFinalRockImpactProgress(int impactIndex)
        {
            return impactIndex == 0 ? 0.22f : 0.5f;
        }

        private void UpdateFinalRockBalanceState(
            float normalizedRockOffset,
            float settlingProgress01)
        {
            Vector2 baseDirection = DirectionWorld.sqrMagnitude > 0.000001f
                ? DirectionWorld.normalized
                : Vector2.right;
            float balanceAngle = normalizedRockOffset
                                 * finalRockBalanceSwingDegrees
                                 * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(balanceAngle);
            float sine = Mathf.Sin(balanceAngle);
            Vector2 displayedWorldDirection = new Vector2(
                baseDirection.x * cosine - baseDirection.y * sine,
                baseDirection.x * sine + baseDirection.y * cosine);
            float edgeMagnitude = Mathf.Max(1.01f, tumbleBalanceOuterMagnitude);
            float displayedMagnitude = Mathf.Lerp(
                tumbleBalanceOuterMagnitude,
                edgeMagnitude,
                Mathf.Abs(normalizedRockOffset) * 0.65f);
            displayedMagnitude = Mathf.Max(
                0.94f,
                displayedMagnitude + EvaluateBalanceLandingKick());
            Vector2 displayedWorldOffset = displayedWorldDirection
                                           * displayedMagnitude;
            if (finalRockWillRecoverUpright)
            {
                float returnProgress = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(settlingProgress01));
                displayedWorldOffset *= 1f - returnProgress;
            }

            PublishTumbleBalanceState(displayedWorldOffset, Vector2.zero);
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

            float safeVerticalSize = Mathf.Max(
                0.01f,
                CurrentVerticalSizeMeters);
            float safeForwardSize = Mathf.Max(
                0.01f,
                NextVerticalSizeMeters);
            float geometryAspect = Mathf.Min(
                                       safeVerticalSize,
                                       safeForwardSize)
                                   / Mathf.Max(
                                       safeVerticalSize,
                                       safeForwardSize);
            float geometryArcStrength = Mathf.Lerp(
                0.7f,
                1f,
                geometryAspect);
            displayedMagnitude += tumbleBalanceArcOutwardScale
                                  * halfTurnWave
                                  * swingStrength
                                  * geometryArcStrength;
            displayedMagnitude += EvaluateBalanceLandingKick();
            displayedMagnitude = Mathf.Max(0.94f, displayedMagnitude);

            float alternatingSign = (CompletedStepCount & 1) == 0
                ? 1f
                : -1f;
            float lateralAngleRadians = lateralSwingDegrees
                                        * Mathf.Deg2Rad
                                        * halfTurnWave
                                        * swingStrength
                                        * geometryArcStrength
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
            PublishTumbleBalanceState(displayedWorldOffset, Vector2.zero);
        }

        private void StartBalanceLandingImpact(
            float lostSpecificEnergy,
            float referenceSpecificEnergy)
        {
            float normalizedImpact = Mathf.Clamp01(
                lostSpecificEnergy
                / Mathf.Max(0.01f, referenceSpecificEnergy));
            balanceLandingImpactStrength = Mathf.Lerp(
                0.45f,
                1f,
                Mathf.Sqrt(normalizedImpact));
            balanceLandingImpactStartTime = Time.time;
        }

        private float EvaluateBalanceLandingKick()
        {
            float elapsed = Time.time - balanceLandingImpactStartTime;
            if (elapsed < 0f
                || elapsed >= tumbleBalanceLandingReboundDuration)
            {
                return 0f;
            }

            float progress = Mathf.Clamp01(
                elapsed
                / Mathf.Max(0.05f, tumbleBalanceLandingReboundDuration));
            float kickShape = progress < 0.32f
                ? Mathf.Lerp(
                    1f,
                    -0.28f,
                    Mathf.SmoothStep(0f, 1f, progress / 0.32f))
                : Mathf.Lerp(
                    -0.28f,
                    0f,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        (progress - 0.32f) / 0.68f));
            return tumbleBalanceLandingKick
                   * balanceLandingImpactStrength
                   * kickShape;
        }

        private void PublishTumbleBalanceState(
            Vector2 displayedWorldOffset,
            Vector2 playerCounterbalanceLocal)
        {
            int currentFrame = Time.frameCount;
            if (lastBalancePublishFrame >= 0
                && currentFrame != lastBalancePublishFrame)
            {
                float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
                Vector2 rawVelocity = (displayedWorldOffset
                                       - lastPublishedBalanceWorldOffset)
                                      / deltaTime;
                publishedBalanceVelocity = Vector2.ClampMagnitude(
                    Vector2.Lerp(publishedBalanceVelocity, rawVelocity, 0.65f),
                    12f);
            }

            lastBalancePublishFrame = currentFrame;
            lastPublishedBalanceWorldOffset = displayedWorldOffset;

            Vector2 displayedLocalOffset = new Vector2(
                Vector2.Dot(displayedWorldOffset, transform.right),
                Vector2.Dot(displayedWorldOffset, transform.up));
            float displayedMagnitude = displayedWorldOffset.magnitude;
            RobotBalanceLevel level = displayedMagnitude >= 1f
                ? RobotBalanceLevel.OutsideSupport
                : displayedMagnitude >= 0.72f
                    ? RobotBalanceLevel.Critical
                    : displayedMagnitude >= 0.35f
                        ? RobotBalanceLevel.Loaded
                        : RobotBalanceLevel.Stable;
            TumbleBalanceState = new RobotBalanceState(
                displayedLocalOffset,
                displayedWorldOffset,
                playerCounterbalanceLocal,
                displayedMagnitude,
                Mathf.InverseLerp(0.75f, 1f, displayedMagnitude),
                level);
        }

        private void StartUprightBalanceRecovery(float finalRockStrength)
        {
            IsBalanceRecoveryActive = true;
            BalanceRecoveryProgress01 = 0f;
            balanceRecoveryElapsed = 0f;
            balanceRecoveryStartWorldOffset =
                TumbleBalanceState.NormalizedWorldOffset;
            balanceRecoveryStartVelocity = publishedBalanceVelocity;
            balanceRecoveryOvershootScale =
                uprightBalanceRecoveryOvershootRatio
                * Mathf.Lerp(
                    1f,
                    1.65f,
                    Mathf.Clamp01(finalRockStrength)
                    * finalRockRecoveryInfluence);
        }

        private void UpdateUprightBalanceRecovery()
        {
            balanceRecoveryElapsed += Mathf.Max(0f, Time.deltaTime);
            BalanceRecoveryProgress01 = Mathf.Clamp01(
                balanceRecoveryElapsed
                / Mathf.Max(0.1f, uprightBalanceRecoveryDuration));

            RobotBalanceState liveState = balance != null
                ? balance.CurrentState
                : default;
            Vector2 targetWorldOffset = liveState.NormalizedWorldOffset;
            float progress = BalanceRecoveryProgress01;
            float envelope = (1f - progress) * (1f - progress);
            float cosine = Mathf.Cos(
                progress
                * Mathf.PI
                * 2f
                * uprightBalanceRecoveryOscillationCount);
            float residual;
            if (cosine >= 0f)
            {
                residual = envelope * cosine;
            }
            else
            {
                const float OneCycleNegativePeakEnvelope = 0.25f;
                residual = envelope
                           * cosine
                           * balanceRecoveryOvershootScale
                           / OneCycleNegativePeakEnvelope;
            }

            Vector2 carriedVelocityOffset = balanceRecoveryStartVelocity
                                            * uprightBalanceVelocityCarrySeconds
                                            * progress
                                            * envelope;
            Vector2 displayedWorldOffset = targetWorldOffset
                                           + (balanceRecoveryStartWorldOffset
                                              - targetWorldOffset)
                                           * residual
                                           + carriedVelocityOffset;
            PublishTumbleBalanceState(
                displayedWorldOffset,
                liveState.PlayerCounterbalanceLocal);

            if (BalanceRecoveryProgress01 < 1f)
                return;

            PublishTumbleBalanceState(
                targetWorldOffset,
                liveState.PlayerCounterbalanceLocal);
            IsBalanceRecoveryActive = false;
            BalanceRecoveryProgress01 = 0f;
            publishedBalanceVelocity = Vector2.zero;
        }

        private void Settle(RobotTumbleSettleReason reason)
        {
            if (State == RobotTumbleState.Fallen)
                return;

            bool interruptedDuringIncompleteStep = activeStep
                                                    && StepProgress01
                                                    < 0.9999f;
            bool recoveredUpright = !interruptedDuringIncompleteStep
                                    && CompletedStepCount > 0
                                    && CompletedStepCount
                                    % StepsPerFullRotation == 0
                                    && mover != null
                                    && mover.IsExternallyTumbling;
            bool synchronizedFinalRockRecovery = recoveredUpright
                                                  && State
                                                  == RobotTumbleState.FinalRocking
                                                  && finalRockWillRecoverUpright
                                                  && FinalRockProgress01
                                                  >= 0.9999f;
            float remainingSpecificEnergy = CurrentSpecificEnergy;
            activeStep = false;
            if (State == RobotTumbleState.FinalRocking)
            {
                if (synchronizedFinalRockRecovery)
                {
                    UpdateFinalRockBalanceState(0f, 1f);
                }
                else
                {
                    finalRockWillRecoverUpright = false;
                    UpdateFinalRockBalanceState(0f, 0f);
                }
            }
            float finalRockStrength = finalRockMaximumAngleDegrees > 0.0001f
                ? Mathf.InverseLerp(
                    finalRockMinimumAngleDegrees,
                    Mathf.Max(
                        finalRockMinimumAngleDegrees + 0.0001f,
                        finalRockMaximumAngleDegrees),
                    FinalRockMaximumAngleDegrees)
                : 0f;
            FinalRockOffsetDegrees = 0f;
            if (recoveredUpright)
            {
                State = RobotTumbleState.Upright;
                CurrentSpecificEnergy = 0f;
                StepProgress01 = 0f;
                FinalRockProgress01 = 0f;
                balance?.RestoreUprightAfterCompletedTumble();
                mover.RestoreDrivenAfterCompletedTumble();
                if (synchronizedFinalRockRecovery)
                {
                    IsBalanceRecoveryActive = false;
                    BalanceRecoveryProgress01 = 0f;
                    publishedBalanceVelocity = Vector2.zero;
                }
                else
                {
                    StartUprightBalanceRecovery(finalRockStrength);
                }
                FinalRockMaximumAngleDegrees = 0f;
            }
            else
            {
                IsBalanceRecoveryActive = false;
                BalanceRecoveryProgress01 = 0f;
                State = RobotTumbleState.Fallen;
                mover?.MarkFallenPermanently();
            }

            finalRockWillRecoverUpright = false;

            Settled?.Invoke(new RobotTumbleSettledInfo(
                reason,
                CompletedStepCount,
                transform.position,
                remainingSpecificEnergy,
                recoveredUpright));
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
            finalRockDuration = Mathf.Max(0.1f, finalRockDuration);
            finalRockMinimumAngleDegrees = Mathf.Clamp(
                finalRockMinimumAngleDegrees,
                0f,
                30f);
            finalRockMaximumAngleDegrees = Mathf.Clamp(
                finalRockMaximumAngleDegrees,
                finalRockMinimumAngleDegrees,
                30f);
            finalRockBalanceSwingDegrees = Mathf.Clamp(
                finalRockBalanceSwingDegrees,
                0f,
                30f);
            outerBalanceMagnitude = Mathf.Clamp(
                outerBalanceMagnitude,
                1.001f,
                2f);
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
            tumbleBalanceArcOutwardScale = Mathf.Clamp(
                tumbleBalanceArcOutwardScale,
                0f,
                0.35f);
            tumbleBalanceLandingKick = Mathf.Clamp(
                tumbleBalanceLandingKick,
                0f,
                0.35f);
            tumbleBalanceLandingReboundDuration = Mathf.Clamp(
                tumbleBalanceLandingReboundDuration,
                0.05f,
                0.5f);
            uprightBalanceRecoveryDuration = Mathf.Clamp(
                uprightBalanceRecoveryDuration,
                0.1f,
                1.5f);
            uprightBalanceRecoveryOvershootRatio = Mathf.Clamp(
                uprightBalanceRecoveryOvershootRatio,
                0f,
                0.35f);
            uprightBalanceRecoveryOscillationCount = Mathf.Clamp(
                uprightBalanceRecoveryOscillationCount,
                0.5f,
                2f);
            uprightBalanceVelocityCarrySeconds = Mathf.Clamp(
                uprightBalanceVelocityCarrySeconds,
                0f,
                0.25f);
            finalRockRecoveryInfluence = Mathf.Clamp01(
                finalRockRecoveryInfluence);
            maximumGroundedDetailStepMeters = Mathf.Max(
                0f,
                maximumGroundedDetailStepMeters);
            maximumStepCount = Mathf.Max(1, maximumStepCount);
            maximumTumbleDuration = Mathf.Max(0.1f, maximumTumbleDuration);
        }
    }
}
