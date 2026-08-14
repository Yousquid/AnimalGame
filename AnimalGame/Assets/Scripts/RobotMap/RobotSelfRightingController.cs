using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    public enum RobotSelfRightingState
    {
        Inactive,
        FallenIdle,
        BuildingSupport,
        PullingBalance,
        ReturningAfterFailure,
        RightingChassis
    }

    public enum RobotSelfRightingFailureReason
    {
        ArmDirectionLost,
        ForceCadenceLost,
        BalancePullLost
    }

    public readonly struct RobotSelfRightingForcePulseInfo
    {
        public Vector2 WorldDirection { get; }
        public bool Accepted { get; }
        public float Alignment01 { get; }

        public RobotSelfRightingForcePulseInfo(
            Vector2 worldDirection,
            bool accepted,
            float alignment01)
        {
            WorldDirection = worldDirection;
            Accepted = accepted;
            Alignment01 = alignment01;
        }
    }

    public readonly struct RobotSelfRightingFailureInfo
    {
        public Vector2 WorldDirection { get; }
        public RobotSelfRightingFailureReason Reason { get; }

        public RobotSelfRightingFailureInfo(
            Vector2 worldDirection,
            RobotSelfRightingFailureReason reason)
        {
            WorldDirection = worldDirection;
            Reason = reason;
        }
    }

    public readonly struct RobotSelfRightingLandedInfo
    {
        public Vector2 OriginalTumbleDirectionWorld { get; }

        public RobotSelfRightingLandedInfo(Vector2 originalTumbleDirectionWorld)
        {
            OriginalTumbleDirectionWorld = originalTumbleDirectionWorld;
        }
    }

    [DefaultExecutionOrder(130)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMover))]
    [RequireComponent(typeof(RobotTumbleController))]
    [RequireComponent(typeof(RobotBalanceController))]
    [RequireComponent(typeof(RobotArmController))]
    [RequireComponent(typeof(RobotMarkerView))]
    public sealed class RobotSelfRightingController : MonoBehaviour
    {
        [Header("Arm Support")]
        [Tooltip("Maximum angle between the mechanical-arm target and the locked tumble direction that still counts as a valid brace.")]
        [SerializeField, Range(1f, 60f)]
        private float supportDirectionToleranceDegrees = 18f;

        [Tooltip("Minimum remapped left-stick magnitude required to push the arms fully into the support plane.")]
        [SerializeField, Range(0f, 1f)]
        private float minimumArmPushMagnitude = 0.9f;

        [Header("Force Tapping")]
        [Tooltip("Keyboard alternative to the gamepad north face button (Xbox Y / Sony Triangle).")]
        [SerializeField] private KeyCode keyboardForceKey = KeyCode.RightControl;

        [Tooltip("Required accepted Y-button presses per second. This is evaluated through a rolling time window.")]
        [SerializeField, Range(0.5f, 10f)]
        private float requiredForceTapFrequencyPerSecond = 3f;

        [Tooltip("Length of the rolling time window used to measure force-button frequency.")]
        [SerializeField, Range(0.4f, 2f)]
        private float forceTapWindowSeconds = 1f;

        [Tooltip("Time allowed below the required cadence before a loaded brace collapses. This lets the right thumb travel between Y and the right stick.")]
        [SerializeField, Range(0.05f, 1.2f)]
        private float forceCadenceLossGraceSeconds = 0.55f;

        [Tooltip("Time a loaded brace tolerates an incorrect arm direction before it collapses.")]
        [SerializeField, Range(0f, 0.5f)]
        private float armDirectionLossGraceSeconds = 0.14f;

        [Header("Slow Balance Pull")]
        [Tooltip("Seconds of sustained full right-stick pull required to bring the fallen balance point back inside support.")]
        [SerializeField, Range(2f, 4f)] private float balancePullDuration = 3f;

        [SerializeField, Range(0f, 0.9f)] private float rightStickDeadZone = 0.2f;
        [SerializeField, Range(0f, 1f)] private float minimumBalancePullMagnitude = 0.65f;
        [SerializeField, Range(1f, 60f)] private float balancePullToleranceDegrees = 22f;

        [Tooltip("Short interruption allowed while the player moves their thumb between Y and the right stick.")]
        [SerializeField, Range(0.05f, 1f)]
        private float balancePullLossGraceSeconds = 0.38f;

        [SerializeField, Range(0.5f, 0.99f)] private float recoveredInsideMagnitude = 0.88f;
        [SerializeField, Range(0.05f, 0.6f)] private float failedReturnDuration = 0.22f;

        [Header("Righting and Opposite Inertia")]
        [SerializeField, Range(0.15f, 1f)] private float chassisRightingDuration = 0.46f;

        [Tooltip("Actual normalized balance position restored on the opposite side after the chassis lands upright.")]
        [SerializeField, Range(0.4f, 0.99f)] private float uprightLandingBalanceMagnitude = 0.82f;

        [Tooltip("Temporary balance target applied toward the opposite side after landing. Without counterbalance this is strong enough to cause another tumble.")]
        [SerializeField, Range(0.5f, 3f)] private float oppositeInertiaMagnitude = 1.5f;

        [SerializeField, Range(0.2f, 2f)] private float oppositeInertiaDuration = 1.2f;

        [Header("Virtual Support Plane")]
        [SerializeField, Range(0.2f, 1.5f)]
        private float guideDistanceOfBalanceRadius = 0.8f;

        [SerializeField, Range(0.2f, 2f)]
        private float guideWidthOfBodyDiameter = 1.05f;

        [SerializeField, Range(0.02f, 0.5f)]
        private float guideDepthOfBodyDiameter = 0.12f;

        [SerializeField, Range(0.5f, 8f)] private float guideLineWidthPixels = 2.2f;
        [SerializeField, Range(0.01f, 0.5f)] private float guideFadeDuration = 0.14f;
        [SerializeField] private Color guideNeutralColor = new Color(0.45f, 0.9f, 1f, 0.34f);
        [SerializeField] private Color guideReadyColor = new Color(1f, 0.82f, 0.18f, 0.72f);

        public RobotSelfRightingState State { get; private set; } =
            RobotSelfRightingState.Inactive;
        public bool HasBalancePresentation => State != RobotSelfRightingState.Inactive;
        public bool ShowBalancePointAsCross =>
            State == RobotSelfRightingState.FallenIdle
            || State == RobotSelfRightingState.BuildingSupport
            || State == RobotSelfRightingState.ReturningAfterFailure;
        public bool UseRecoveryBalancePointColor =>
            State == RobotSelfRightingState.PullingBalance;
        public RobotBalanceState DisplayedBalanceState { get; private set; }
        public float PullProgress01 { get; private set; }
        public float CurrentForceTapFrequency { get; private set; }
        public bool IsArmSupportCorrect { get; private set; }
        public float ArmAlignment01 { get; private set; }
        public Vector2 CurrentBalancePullInputLocal { get; private set; }

        public event Action<RobotSelfRightingForcePulseInfo> ForcePulse;
        public event Action<RobotSelfRightingFailureInfo> SupportFailed;
        public event Action<RobotSelfRightingLandedInfo> RightingLanded;

        private readonly Queue<float> validForceTapTimes = new Queue<float>();
        private RobotTumbleController tumble;
        private RobotBalanceController balance;
        private RobotArmController arms;
        private RobotMarkerView markerView;
        private RobotBalanceView balanceView;
        private Vector2 lockedTumbleDirectionWorld;
        private float fallenBalanceMagnitude;
        private float armDirectionLossElapsed;
        private float forceCadenceLossElapsed;
        private float balancePullLossElapsed;
        private float failedReturnElapsed;
        private float failedReturnStartMagnitude;
        private float rightingElapsed;
        private bool supportHasCarriedLoad;
        private float guideVisibility;
        private LineRenderer guideOutline;
        private LineRenderer guideCentreLine;
        private readonly Vector3[] guideOutlinePoints = new Vector3[4];
        private readonly Vector3[] guideCentrePoints = new Vector3[2];

        private void Awake()
        {
            tumble = GetComponent<RobotTumbleController>();
            balance = GetComponent<RobotBalanceController>();
            arms = GetComponent<RobotArmController>();
            markerView = GetComponent<RobotMarkerView>();
            balanceView = GetComponent<RobotBalanceView>();
        }

        private void Start()
        {
            EnsureGuideVisual();
        }

        private void Update()
        {
            EnsureGuideVisual();
            float deltaTime = Mathf.Min(Mathf.Max(0f, Time.deltaTime), 0.05f);

            if (tumble == null)
            {
                ResetSystem();
                UpdateGuide(deltaTime);
                return;
            }

            if (State == RobotSelfRightingState.RightingChassis)
            {
                UpdateRightingChassis(deltaTime);
                UpdateGuide(deltaTime);
                return;
            }

            if (tumble.State != RobotTumbleState.Fallen)
            {
                if (State != RobotSelfRightingState.Inactive)
                    ResetSystem();
                UpdateGuide(deltaTime);
                return;
            }

            if (State == RobotSelfRightingState.Inactive)
                EnterFallenState();

            bool forcePressed = Input.GetKeyDown(keyboardForceKey)
                                || AdaptiveLegacyGamepadInput
                                    .WasNorthFaceButtonPressedThisFrame();
            EvaluateArmSupport();
            if (forcePressed && arms != null && arms.IsArmModeActive)
            {
                bool accepted = IsArmSupportCorrect;
                if (accepted)
                {
                    validForceTapTimes.Enqueue(Time.time);
                    supportHasCarriedLoad = true;
                }

                ForcePulse?.Invoke(new RobotSelfRightingForcePulseInfo(
                    lockedTumbleDirectionWorld,
                    accepted,
                    ArmAlignment01));
            }

            PruneForceTapWindow();
            CurrentForceTapFrequency = validForceTapTimes.Count
                                       / Mathf.Max(0.1f, forceTapWindowSeconds);
            if ((State == RobotSelfRightingState.FallenIdle
                 || State == RobotSelfRightingState.BuildingSupport)
                && validForceTapTimes.Count == 0)
            {
                supportHasCarriedLoad = false;
            }

            switch (State)
            {
                case RobotSelfRightingState.FallenIdle:
                case RobotSelfRightingState.BuildingSupport:
                    UpdateBuildingSupport(deltaTime);
                    break;
                case RobotSelfRightingState.PullingBalance:
                    UpdateBalancePull(deltaTime);
                    break;
                case RobotSelfRightingState.ReturningAfterFailure:
                    UpdateFailedReturn(deltaTime);
                    break;
            }

            UpdateGuide(deltaTime);
        }

        private void EnterFallenState()
        {
            lockedTumbleDirectionWorld = tumble.DirectionWorld.sqrMagnitude
                                         > 0.000001f
                ? tumble.DirectionWorld.normalized
                : (Vector2)transform.right;
            fallenBalanceMagnitude = Mathf.Max(
                1.01f,
                tumble.TumbleBalanceState.Magnitude);
            PullProgress01 = 0f;
            supportHasCarriedLoad = false;
            validForceTapTimes.Clear();
            CurrentForceTapFrequency = 0f;
            State = RobotSelfRightingState.FallenIdle;
            PublishBalancePresentation(fallenBalanceMagnitude, Vector2.zero);
        }

        private void EvaluateArmSupport()
        {
            if (arms == null
                || !arms.IsArmModeActive
                || arms.CurrentInputMagnitude < minimumArmPushMagnitude)
            {
                IsArmSupportCorrect = false;
                ArmAlignment01 = 0f;
                return;
            }

            Vector2 targetLocal = arms.CurrentTargetLocal;
            if (targetLocal.sqrMagnitude <= 0.000001f)
            {
                IsArmSupportCorrect = false;
                ArmAlignment01 = 0f;
                return;
            }

            Vector2 tumbleLocal = WorldDirectionToLocal(
                lockedTumbleDirectionWorld);
            float alignment = Vector2.Dot(
                targetLocal.normalized,
                tumbleLocal.normalized);
            float minimumAlignment = Mathf.Cos(
                supportDirectionToleranceDegrees * Mathf.Deg2Rad);
            IsArmSupportCorrect = alignment >= minimumAlignment;
            ArmAlignment01 = Mathf.InverseLerp(
                minimumAlignment,
                1f,
                alignment);
        }

        private void UpdateBuildingSupport(float deltaTime)
        {
            CurrentBalancePullInputLocal = Vector2.zero;
            PublishBalancePresentation(fallenBalanceMagnitude, Vector2.zero);
            State = IsArmSupportCorrect
                ? RobotSelfRightingState.BuildingSupport
                : RobotSelfRightingState.FallenIdle;

            if (supportHasCarriedLoad && !IsArmSupportCorrect)
            {
                armDirectionLossElapsed += deltaTime;
                if (armDirectionLossElapsed >= armDirectionLossGraceSeconds)
                {
                    FailSupport(RobotSelfRightingFailureReason.ArmDirectionLost);
                    return;
                }
            }
            else
            {
                armDirectionLossElapsed = 0f;
            }

            if (!IsArmSupportCorrect
                || CurrentForceTapFrequency
                < requiredForceTapFrequencyPerSecond)
            {
                return;
            }

            State = RobotSelfRightingState.PullingBalance;
            PullProgress01 = 0f;
            armDirectionLossElapsed = 0f;
            forceCadenceLossElapsed = 0f;
            balancePullLossElapsed = -balancePullLossGraceSeconds;
            PublishBalancePresentation(fallenBalanceMagnitude, Vector2.zero);
        }

        private void UpdateBalancePull(float deltaTime)
        {
            if (!IsArmSupportCorrect)
            {
                armDirectionLossElapsed += deltaTime;
                if (armDirectionLossElapsed >= armDirectionLossGraceSeconds)
                {
                    FailSupport(RobotSelfRightingFailureReason.ArmDirectionLost);
                    return;
                }
            }
            else
            {
                armDirectionLossElapsed = 0f;
            }

            if (CurrentForceTapFrequency
                < requiredForceTapFrequencyPerSecond)
            {
                forceCadenceLossElapsed += deltaTime;
                if (forceCadenceLossElapsed >= forceCadenceLossGraceSeconds)
                {
                    FailSupport(RobotSelfRightingFailureReason.ForceCadenceLost);
                    return;
                }
            }
            else
            {
                forceCadenceLossElapsed = 0f;
            }

            CurrentBalancePullInputLocal = ReadBalancePullInputLocal();
            Vector2 desiredPullLocal = -WorldDirectionToLocal(
                lockedTumbleDirectionWorld);
            bool pullCorrect = CurrentBalancePullInputLocal.magnitude
                               >= minimumBalancePullMagnitude
                               && Vector2.Dot(
                                      CurrentBalancePullInputLocal.normalized,
                                      desiredPullLocal.normalized)
                               >= Mathf.Cos(
                                   balancePullToleranceDegrees
                                   * Mathf.Deg2Rad);
            if (pullCorrect)
            {
                balancePullLossElapsed = 0f;
                float pullStrength = Mathf.InverseLerp(
                    minimumBalancePullMagnitude,
                    1f,
                    CurrentBalancePullInputLocal.magnitude);
                float speedScale = Mathf.Lerp(0.72f, 1f, pullStrength);
                PullProgress01 = Mathf.Clamp01(
                    PullProgress01
                    + deltaTime
                    / Mathf.Max(0.1f, balancePullDuration)
                    * speedScale);
            }
            else
            {
                balancePullLossElapsed += deltaTime;
                if (balancePullLossElapsed >= balancePullLossGraceSeconds)
                {
                    FailSupport(RobotSelfRightingFailureReason.BalancePullLost);
                    return;
                }
            }

            float easedProgress = Mathf.SmoothStep(0f, 1f, PullProgress01);
            float magnitude = Mathf.Lerp(
                fallenBalanceMagnitude,
                recoveredInsideMagnitude,
                easedProgress);
            PublishBalancePresentation(magnitude, CurrentBalancePullInputLocal);
            if (magnitude > 1f && PullProgress01 < 1f)
                return;

            BeginRightingChassis();
        }

        private void BeginRightingChassis()
        {
            State = RobotSelfRightingState.RightingChassis;
            rightingElapsed = 0f;
            PullProgress01 = 1f;
            CurrentBalancePullInputLocal = Vector2.zero;
            PublishBalancePresentation(recoveredInsideMagnitude, Vector2.zero);
            tumble.BeginSelfRightingVisual();
        }

        private void UpdateRightingChassis(float deltaTime)
        {
            rightingElapsed += deltaTime;
            float progress = Mathf.Clamp01(
                rightingElapsed / Mathf.Max(0.05f, chassisRightingDuration));
            tumble.UpdateSelfRightingVisualProgress(progress);
            PublishBalancePresentation(recoveredInsideMagnitude, Vector2.zero);
            if (progress < 1f)
                return;

            Vector2 originalDirection = lockedTumbleDirectionWorld;
            bool completed = tumble.CompleteSelfRighting(
                lockedTumbleDirectionWorld,
                -lockedTumbleDirectionWorld,
                uprightLandingBalanceMagnitude,
                oppositeInertiaMagnitude,
                oppositeInertiaDuration);
            if (completed)
            {
                RightingLanded?.Invoke(new RobotSelfRightingLandedInfo(
                    originalDirection));
            }

            ResetSystem();
        }

        private void FailSupport(RobotSelfRightingFailureReason reason)
        {
            if (State == RobotSelfRightingState.ReturningAfterFailure)
                return;

            failedReturnStartMagnitude = Mathf.Max(
                recoveredInsideMagnitude,
                DisplayedBalanceState.Magnitude);
            failedReturnElapsed = 0f;
            State = RobotSelfRightingState.ReturningAfterFailure;
            PullProgress01 = 0f;
            CurrentBalancePullInputLocal = Vector2.zero;
            validForceTapTimes.Clear();
            CurrentForceTapFrequency = 0f;
            supportHasCarriedLoad = false;
            SupportFailed?.Invoke(new RobotSelfRightingFailureInfo(
                lockedTumbleDirectionWorld,
                reason));
        }

        private void UpdateFailedReturn(float deltaTime)
        {
            failedReturnElapsed += deltaTime;
            float progress = Mathf.Clamp01(
                failedReturnElapsed / Mathf.Max(0.05f, failedReturnDuration));
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            float magnitude = Mathf.Lerp(
                failedReturnStartMagnitude,
                fallenBalanceMagnitude,
                eased);
            PublishBalancePresentation(magnitude, Vector2.zero);
            if (progress < 1f)
                return;

            State = RobotSelfRightingState.FallenIdle;
            armDirectionLossElapsed = 0f;
            forceCadenceLossElapsed = 0f;
            balancePullLossElapsed = 0f;
        }

        private void PruneForceTapWindow()
        {
            float oldestAllowed = Time.time - forceTapWindowSeconds;
            while (validForceTapTimes.Count > 0
                   && validForceTapTimes.Peek() < oldestAllowed)
            {
                validForceTapTimes.Dequeue();
            }
        }

        private Vector2 ReadBalancePullInputLocal()
        {
            Vector2 keyboard = new Vector2(
                (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));
            keyboard = Vector2.ClampMagnitude(keyboard, 1f);
            Vector2 gamepad = Vector2.ClampMagnitude(
                AdaptiveLegacyGamepadInput.ReadBalance(),
                1f);
            if (gamepad.magnitude <= rightStickDeadZone)
            {
                gamepad = Vector2.zero;
            }
            else
            {
                gamepad = gamepad.normalized
                          * Mathf.InverseLerp(
                              rightStickDeadZone,
                              1f,
                              gamepad.magnitude);
            }

            return keyboard.sqrMagnitude >= gamepad.sqrMagnitude
                ? keyboard
                : gamepad;
        }

        private void PublishBalancePresentation(
            float magnitude,
            Vector2 playerCounterbalanceLocal)
        {
            Vector2 directionWorld = lockedTumbleDirectionWorld.sqrMagnitude
                                     > 0.000001f
                ? lockedTumbleDirectionWorld.normalized
                : (Vector2)transform.right;
            Vector2 directionLocal = WorldDirectionToLocal(directionWorld);
            float safeMagnitude = Mathf.Max(0f, magnitude);
            RobotBalanceLevel level = safeMagnitude >= 1f
                ? RobotBalanceLevel.OutsideSupport
                : safeMagnitude >= 0.72f
                    ? RobotBalanceLevel.Critical
                    : safeMagnitude >= 0.35f
                        ? RobotBalanceLevel.Loaded
                        : RobotBalanceLevel.Stable;
            DisplayedBalanceState = new RobotBalanceState(
                directionLocal * safeMagnitude,
                directionWorld * safeMagnitude,
                playerCounterbalanceLocal,
                safeMagnitude,
                Mathf.InverseLerp(0.72f, 1f, safeMagnitude),
                level);
        }

        private Vector2 WorldDirectionToLocal(Vector2 worldDirection)
        {
            Vector2 right = transform.right;
            Vector2 forward = transform.up;
            Vector2 local = new Vector2(
                Vector2.Dot(worldDirection, right),
                Vector2.Dot(worldDirection, forward));
            return local.sqrMagnitude > 0.000001f
                ? local.normalized
                : Vector2.up;
        }

        private void EnsureGuideVisual()
        {
            if (guideOutline != null
                || markerView == null
                || markerView.MarkerVisualRoot == null)
            {
                return;
            }

            guideOutline = RobotMapDemo.CreateLine(
                markerView.MarkerVisualRoot,
                "Self Righting Support Plane Outline",
                guideOutlinePoints,
                0.03f,
                Color.clear,
                994,
                true);
            guideCentreLine = RobotMapDemo.CreateLine(
                markerView.MarkerVisualRoot,
                "Self Righting Support Plane Contact",
                guideCentrePoints,
                0.04f,
                Color.clear,
                995);
            guideOutline.transform.localPosition = Vector3.zero;
            guideOutline.transform.localRotation = Quaternion.identity;
            guideOutline.transform.localScale = Vector3.one;
            guideCentreLine.transform.localPosition = Vector3.zero;
            guideCentreLine.transform.localRotation = Quaternion.identity;
            guideCentreLine.transform.localScale = Vector3.one;
            guideOutline.enabled = false;
            guideCentreLine.enabled = false;
        }

        private void UpdateGuide(float deltaTime)
        {
            if (guideOutline == null || guideCentreLine == null)
                return;

            bool shouldShow = tumble != null
                              && tumble.State != RobotTumbleState.Upright
                              && arms != null
                              && arms.IsArmModeActive;
            float targetVisibility = shouldShow
                ? arms.VisibleDeployment01
                : 0f;
            float fadeStep = deltaTime / Mathf.Max(0.01f, guideFadeDuration);
            guideVisibility = Mathf.MoveTowards(
                guideVisibility,
                targetVisibility,
                fadeStep);
            bool visible = guideVisibility > 0.001f;
            guideOutline.enabled = visible;
            guideCentreLine.enabled = visible;
            if (!visible)
                return;

            Vector2 worldDirection = tumble.DirectionWorld.sqrMagnitude
                                     > 0.000001f
                ? tumble.DirectionWorld.normalized
                : lockedTumbleDirectionWorld;
            Vector2 localDirection = WorldDirectionToLocal(worldDirection);
            Vector2 tangent = new Vector2(-localDirection.y, localDirection.x);
            if (balanceView == null)
                balanceView = GetComponent<RobotBalanceView>();
            float ringRadius = balanceView != null
                ? markerView.ScreenPixelsToMarkerLocalUnits(
                    balanceView.ControlRingRadiusPixels)
                : markerView.BodyDiameter * 1.65f;
            if (ringRadius <= 0.0001f)
                ringRadius = markerView.BodyDiameter * 1.65f;
            Vector2 centre = localDirection
                             * ringRadius
                             * guideDistanceOfBalanceRadius;
            float halfWidth = markerView.BodyDiameter
                              * guideWidthOfBodyDiameter
                              * 0.5f;
            float halfDepth = markerView.BodyDiameter
                              * guideDepthOfBodyDiameter
                              * 0.5f;
            guideOutlinePoints[0] = centre - tangent * halfWidth
                                    - localDirection * halfDepth;
            guideOutlinePoints[1] = centre + tangent * halfWidth
                                    - localDirection * halfDepth;
            guideOutlinePoints[2] = centre + tangent * halfWidth
                                    + localDirection * halfDepth;
            guideOutlinePoints[3] = centre - tangent * halfWidth
                                    + localDirection * halfDepth;
            guideCentrePoints[0] = centre - tangent * halfWidth;
            guideCentrePoints[1] = centre + tangent * halfWidth;
            guideOutline.SetPositions(guideOutlinePoints);
            guideCentreLine.SetPositions(guideCentrePoints);

            float lineWidth = Mathf.Max(
                0.002f,
                markerView.ScreenPixelsToMarkerLocalUnits(
                    guideLineWidthPixels));
            guideOutline.startWidth = lineWidth * 0.7f;
            guideOutline.endWidth = lineWidth * 0.7f;
            guideCentreLine.startWidth = lineWidth;
            guideCentreLine.endWidth = lineWidth;
            Color targetColor = IsArmSupportCorrect
                                || State == RobotSelfRightingState.PullingBalance
                ? guideReadyColor
                : guideNeutralColor;
            targetColor.a *= guideVisibility;
            guideOutline.startColor = targetColor;
            guideOutline.endColor = targetColor;
            targetColor.a = Mathf.Clamp01(targetColor.a * 1.25f);
            guideCentreLine.startColor = targetColor;
            guideCentreLine.endColor = targetColor;
        }

        private void ResetSystem()
        {
            State = RobotSelfRightingState.Inactive;
            PullProgress01 = 0f;
            CurrentForceTapFrequency = 0f;
            CurrentBalancePullInputLocal = Vector2.zero;
            IsArmSupportCorrect = false;
            ArmAlignment01 = 0f;
            supportHasCarriedLoad = false;
            validForceTapTimes.Clear();
            armDirectionLossElapsed = 0f;
            forceCadenceLossElapsed = 0f;
            balancePullLossElapsed = 0f;
            if (tumble != null)
                tumble.CancelSelfRightingVisual();
        }

        private void OnDisable()
        {
            ResetSystem();
            guideVisibility = 0f;
            if (guideOutline != null)
                guideOutline.enabled = false;
            if (guideCentreLine != null)
                guideCentreLine.enabled = false;
        }

        private void OnDestroy()
        {
            if (guideOutline != null && guideOutline.sharedMaterial != null)
                Destroy(guideOutline.sharedMaterial);
            if (guideCentreLine != null && guideCentreLine.sharedMaterial != null)
                Destroy(guideCentreLine.sharedMaterial);
        }

        private void OnValidate()
        {
            supportDirectionToleranceDegrees = Mathf.Clamp(
                supportDirectionToleranceDegrees,
                1f,
                60f);
            minimumArmPushMagnitude = Mathf.Clamp01(minimumArmPushMagnitude);
            requiredForceTapFrequencyPerSecond = Mathf.Clamp(
                requiredForceTapFrequencyPerSecond,
                0.5f,
                10f);
            forceTapWindowSeconds = Mathf.Clamp(forceTapWindowSeconds, 0.4f, 2f);
            forceCadenceLossGraceSeconds = Mathf.Clamp(
                forceCadenceLossGraceSeconds,
                0.05f,
                1.2f);
            armDirectionLossGraceSeconds = Mathf.Clamp(
                armDirectionLossGraceSeconds,
                0f,
                0.5f);
            balancePullDuration = Mathf.Clamp(balancePullDuration, 2f, 4f);
            rightStickDeadZone = Mathf.Clamp(rightStickDeadZone, 0f, 0.9f);
            minimumBalancePullMagnitude = Mathf.Clamp01(
                minimumBalancePullMagnitude);
            balancePullToleranceDegrees = Mathf.Clamp(
                balancePullToleranceDegrees,
                1f,
                60f);
            balancePullLossGraceSeconds = Mathf.Clamp(
                balancePullLossGraceSeconds,
                0.05f,
                1f);
            recoveredInsideMagnitude = Mathf.Clamp(
                recoveredInsideMagnitude,
                0.5f,
                0.99f);
            failedReturnDuration = Mathf.Clamp(failedReturnDuration, 0.05f, 0.6f);
            chassisRightingDuration = Mathf.Clamp(chassisRightingDuration, 0.15f, 1f);
            uprightLandingBalanceMagnitude = Mathf.Clamp(
                uprightLandingBalanceMagnitude,
                0.4f,
                0.99f);
            oppositeInertiaMagnitude = Mathf.Clamp(oppositeInertiaMagnitude, 0.5f, 3f);
            oppositeInertiaDuration = Mathf.Clamp(oppositeInertiaDuration, 0.2f, 2f);
            guideDistanceOfBalanceRadius = Mathf.Clamp(
                guideDistanceOfBalanceRadius,
                0.2f,
                1.5f);
            guideWidthOfBodyDiameter = Mathf.Clamp(
                guideWidthOfBodyDiameter,
                0.2f,
                2f);
            guideDepthOfBodyDiameter = Mathf.Clamp(
                guideDepthOfBodyDiameter,
                0.02f,
                0.5f);
            guideLineWidthPixels = Mathf.Clamp(guideLineWidthPixels, 0.5f, 8f);
            guideFadeDuration = Mathf.Clamp(guideFadeDuration, 0.01f, 0.5f);
        }
    }
}
