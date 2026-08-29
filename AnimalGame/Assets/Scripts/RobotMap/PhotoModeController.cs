using System;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RobotMover))]
    public sealed class PhotoModeController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Keyboard fallback used to enter or leave photo mode while testing without a gamepad.")]
        [SerializeField] private KeyCode keyboardToggleKey = KeyCode.P;

        [Tooltip("Keyboard fallback for the RB focus-and-shutter interaction.")]
        [SerializeField] private KeyCode keyboardFocusAndShutterKey =
            KeyCode.Space;

        [Tooltip("Radial dead zone applied to the right stick while positioning the photo frame.")]
        [SerializeField, Range(0f, 0.9f)] private float rightStickDeadZone = 0.2f;

        [Tooltip("Response curve applied after the right-stick dead zone. Values above one make fine framing gentler.")]
        [SerializeField, Range(1f, 3f)] private float rightStickExponent = 1.35f;

        [Header("Photo Range")]
        [Tooltip("Horizontal distance from the robot centre to each dashed guide origin, in world units.")]
        [SerializeField, Min(0f)] private float guideOriginSideOffset;

        [Tooltip("Forward distance from the robot centre to the two dashed guide origins, in world units.")]
        [SerializeField, Min(0f)] private float guideOriginForwardDistance = 0.6f;

        [Tooltip("Outward angle of each dashed range guide from the robot's forward direction.")]
        [SerializeField, Range(1f, 75f)]
        private float guideHalfAngleDegrees = 28.88889f;

        [Tooltip("Nearest allowed photo-frame distance in front of the robot.")]
        [SerializeField, Min(0f)] private float minimumAimDistance = 2f;

        [Tooltip("Farthest allowed photo-frame distance in front of the robot.")]
        [SerializeField, Min(0.1f)]
        private float maximumAimDistance = 6.066667f;

        [Tooltip("World-space margin kept between the photo-frame centre and either dashed guide.")]
        [SerializeField, Min(0f)] private float lateralFramePadding = 0.2f;

        [Header("Frame Motion")]
        [Tooltip("Maximum horizontal photo-frame motion speed in world units per second.")]
        [SerializeField, Min(0f)] private float lateralAimSpeed = 5f;

        [Tooltip("Maximum near/far photo-frame motion speed in world units per second.")]
        [SerializeField, Min(0f)] private float depthAimSpeed = 4f;

        [Header("Focus")]
        [Tooltip("How long RB must remain held to complete focus.")]
        [SerializeField, Min(0.05f)] private float focusDuration = 0.75f;

        [Tooltip("Seconds used to return camera and UI presentation after focus is cancelled.")]
        [SerializeField, Min(0.05f)] private float focusCancelBlendDuration =
            0.25f;

        [Tooltip("Final camera magnification after focus completes.")]
        [SerializeField, Min(1f)] private float focusZoomMagnification = 1.3f;

        [Tooltip("Fraction of the aim offset used by the camera while normally framing a photo.")]
        [SerializeField, Range(0f, 1f)]
        private float normalCameraAimFollowFraction = 0.5f;

        [Tooltip("Fraction of the aim offset used by the camera after focus completes.")]
        [SerializeField, Range(0f, 1f)]
        private float focusedCameraAimFollowFraction = 1f;

        [Tooltip("Right-stick magnitude that cancels a completed focus so the frame can be repositioned. This remains above normal stick drift.")]
        [SerializeField, Range(0.1f, 1f)]
        private float focusRetargetCancelDeadZone = 0.3f;

        [Header("Shutter")]
        [Tooltip("Seconds for the white shutter flash to reach full opacity.")]
        [SerializeField, Min(0.01f)] private float shutterFlashInDuration =
            0.04f;

        [Tooltip("Seconds the shutter flash remains fully white.")]
        [SerializeField, Min(0f)] private float shutterFlashHoldDuration =
            0.02f;

        [Tooltip("Seconds for the white shutter flash to fade away.")]
        [SerializeField, Min(0.01f)] private float shutterFlashOutDuration =
            0.14f;

        [Header("Transition Presentation")]
        [Tooltip("Seconds during which the photo range is revealed and all player commands are locked.")]
        [SerializeField, Min(0.05f)] private float entryDuration = 0.7f;

        [Tooltip("Seconds during which the photo range is hidden and all player commands are locked.")]
        [SerializeField, Min(0.05f)] private float exitDuration = 0.6f;

        [Tooltip("Camera position damping while following the photo aim.")]
        [SerializeField, Min(0f)] private float photoCameraPositionDamping =
            0.15f;

        [Tooltip("Camera position damping while returning to the robot after leaving photo mode.")]
        [SerializeField, Min(0f)] private float returnCameraPositionDamping =
            0.15f;

        [Tooltip("How long the return-to-player damping override remains active.")]
        [SerializeField, Min(0.05f)] private float returnCameraBlendDuration =
            0.45f;

        [Header("Transition Camera Shake")]
        [SerializeField, Min(0f)] private float entryShakePositionAmplitude =
            0.02f;
        [SerializeField, Min(0f)] private float entryShakeRotationAmplitude =
            0.15f;
        [SerializeField, Min(0.1f)] private float entryShakeFrequency = 5f;

        public bool IsActive => state != PhotoModeState.Inactive;
        public bool IsEntering => state == PhotoModeState.Entering;
        public bool IsExiting => state == PhotoModeState.Exiting;
        public bool IsFocusing => captureState == PhotoCaptureState.Focusing;
        public bool IsFocusComplete =>
            captureState == PhotoCaptureState.Focused
            || captureState == PhotoCaptureState.Capturing;
        public bool IsCapturing =>
            captureState == PhotoCaptureState.Capturing;
        public bool IsReviewing =>
            captureState == PhotoCaptureState.Reviewing;
        public bool IsInputLocked =>
            IsEntering
            || IsExiting
            || captureState != PhotoCaptureState.Framing;
        public float FocusProgress01
        {
            get
            {
                if (captureState == PhotoCaptureState.Focusing)
                {
                    return Mathf.Clamp01(
                        focusElapsed / Mathf.Max(0.05f, focusDuration));
                }

                return IsFocusComplete ? 1f : 0f;
            }
        }
        public float FocusPresentation01 => focusPresentation01;
        public float ShutterFlash01
        {
            get
            {
                if (captureState != PhotoCaptureState.Capturing)
                    return 0f;

                float flashIn = Mathf.Max(0.01f, shutterFlashInDuration);
                float holdEnd = flashIn
                                + Mathf.Max(0f, shutterFlashHoldDuration);
                if (shutterElapsed < flashIn)
                {
                    return Mathf.SmoothStep(
                        0f,
                        1f,
                        shutterElapsed / flashIn);
                }

                if (shutterElapsed <= holdEnd)
                    return 1f;

                float flashOut = Mathf.Max(0.01f, shutterFlashOutDuration);
                return 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        holdEnd,
                        holdEnd + flashOut,
                        shutterElapsed));
            }
        }
        public float Reveal01
        {
            get
            {
                if (state == PhotoModeState.Active)
                    return 1f;
                if (state == PhotoModeState.Entering)
                {
                    return Mathf.Clamp01(
                        entryElapsed / Mathf.Max(0.05f, entryDuration));
                }
                if (state == PhotoModeState.Exiting)
                {
                    return 1f - Mathf.Clamp01(
                        exitElapsed / Mathf.Max(0.05f, exitDuration));
                }

                return 0f;
            }
        }
        public Vector2 AimLocalPosition { get; private set; }
        public float Zoom01 => Mathf.InverseLerp(
            minimumAimDistance,
            maximumAimDistance,
            AimLocalPosition.y);

        public float NormalizedLateralAim
        {
            get
            {
                float halfWidth = CalculateHalfWidthAtDistance(
                    AimLocalPosition.y);
                return halfWidth > 0.0001f
                    ? Mathf.Clamp(AimLocalPosition.x / halfWidth, -1f, 1f)
                    : 0f;
            }
        }

        public Vector3 AimWorldPosition => LocalPointToWorld(AimLocalPosition);
        public Transform AimFollowTarget
        {
            get
            {
                EnsureAimFollowTarget();
                return aimFollowTarget;
            }
        }

        public event Action<bool> ModeChanged;
        public event Action PhotoCaptured;

        private enum PhotoModeState
        {
            Inactive,
            Entering,
            Active,
            Exiting
        }

        private enum PhotoCaptureState
        {
            Framing,
            Focusing,
            Focused,
            Capturing,
            Reviewing
        }

        private RobotMover mover;
        private RobotBalanceController balance;
        private RobotCameraFollow cameraFollow;
        private RobotCameraShake cameraShake;
        private Transform aimFollowTarget;
        private PhotoModeState state;
        private float entryElapsed;
        private float exitElapsed;
        private bool photoStickArmed;
        private PhotoCaptureState captureState;
        private float focusElapsed;
        private float focusPresentation01;
        private float focusPresentationVelocity;
        private bool shutterArmed;
        private float shutterElapsed;
        private bool captureMomentFired;
        private bool photoReviewRequested;

        private void Awake()
        {
            mover = GetComponent<RobotMover>();
            balance = GetComponent<RobotBalanceController>();
            ResetAimToNearest();
            EnsureAimFollowTarget();
            UpdateAimFollowTarget();
        }

        private void OnEnable()
        {
            if (balance == null)
                balance = GetComponent<RobotBalanceController>();
            if (balance != null)
                balance.TippedOver += OnRobotTippedOver;
        }

        private void Update()
        {
            bool togglePressed = Input.GetKeyDown(keyboardToggleKey)
                                 || AdaptiveLegacyGamepadInput
                                     .WasWestFaceButtonPressedThisFrame();
            if (togglePressed)
            {
                if (state == PhotoModeState.Active)
                    SetPhotoModeActive(false);
                else if (state == PhotoModeState.Inactive
                         && CanEnterPhotoMode())
                    SetPhotoModeActive(true);
            }

            if (!IsActive)
                return;

            if (!CanRemainInPhotoMode())
            {
                ExitPhotoModeImmediately();
                return;
            }

            if (state == PhotoModeState.Entering)
            {
                UpdateFocusPresentation();
                UpdateEntryPresentation();
                return;
            }

            if (state == PhotoModeState.Exiting)
            {
                UpdateFocusPresentation();
                UpdateExitPresentation();
                return;
            }

            UpdateCaptureState();
            UpdateFocusPresentation();
            if (captureState == PhotoCaptureState.Framing)
                UpdateAimFromRightStick();
            UpdateAimFollowTarget();
            ApplyFocusCameraPresentation();
        }

        public void InitializeCamera(
            RobotCameraFollow photoCameraFollow,
            RobotCameraShake photoCameraShake)
        {
            cameraFollow = photoCameraFollow;
            cameraShake = photoCameraShake;
            if (state == PhotoModeState.Exiting)
            {
                cameraFollow?.FollowBalanceTargetSmooth(
                    balance,
                    returnCameraPositionDamping,
                    returnCameraBlendDuration);
            }
            else if (IsActive)
            {
                cameraFollow?.FollowTarget(
                    AimFollowTarget,
                    photoCameraPositionDamping);
            }

            ApplyFocusCameraPresentation();
        }

        public void SetPhotoModeActive(bool active)
        {
            if (active)
            {
                if (state != PhotoModeState.Inactive
                    || !CanEnterPhotoMode())
                {
                    return;
                }

                ResetAimToNearest();
                UpdateAimFollowTarget();
                ResolveCameraReferences();
                state = PhotoModeState.Entering;
                entryElapsed = 0f;
                exitElapsed = 0f;
                photoStickArmed = false;
                ResetCaptureState(true);
                mover?.SetPhotoModeInputLocked(true);
                cameraFollow?.FollowTarget(
                    AimFollowTarget,
                    photoCameraPositionDamping);
                cameraShake?.SetPhotoModeRevealShake(
                    0f,
                    entryShakePositionAmplitude,
                    entryShakeRotationAmplitude,
                    entryShakeFrequency);
                ModeChanged?.Invoke(true);
            }
            else
            {
                if (state == PhotoModeState.Inactive
                    || state == PhotoModeState.Exiting)
                {
                    return;
                }

                BeginExitPresentation();
            }
        }

        public void GetGuideWorldPoints(
            out Vector3 leftStart,
            out Vector3 leftEnd,
            out Vector3 rightStart,
            out Vector3 rightEnd)
        {
            float radians = guideHalfAngleDegrees * Mathf.Deg2Rad;
            float forwardRun = Mathf.Max(
                0f,
                maximumAimDistance - guideOriginForwardDistance);
            float lateralRun = Mathf.Tan(radians) * forwardRun;

            leftStart = LocalPointToWorld(new Vector2(
                -guideOriginSideOffset,
                guideOriginForwardDistance));
            leftEnd = LocalPointToWorld(new Vector2(
                -guideOriginSideOffset - lateralRun,
                maximumAimDistance));
            rightStart = LocalPointToWorld(new Vector2(
                guideOriginSideOffset,
                guideOriginForwardDistance));
            rightEnd = LocalPointToWorld(new Vector2(
                guideOriginSideOffset + lateralRun,
                maximumAimDistance));
        }

        public bool RequestPhotoReview()
        {
            if (captureState != PhotoCaptureState.Capturing
                || !captureMomentFired)
            {
                return false;
            }

            photoReviewRequested = true;
            return true;
        }

        public void EndPhotoReview()
        {
            if (captureState != PhotoCaptureState.Reviewing)
                return;

            captureState = PhotoCaptureState.Framing;
            focusElapsed = 0f;
            shutterArmed = false;
            shutterElapsed = 0f;
            captureMomentFired = false;
            photoReviewRequested = false;
            if (state == PhotoModeState.Active)
                mover?.SetPhotoModeInputLocked(false);
        }

        private void UpdateCaptureState()
        {
            bool shoulderHeld = Input.GetKey(keyboardFocusAndShutterKey)
                                || AdaptiveLegacyGamepadInput
                                    .IsRightShoulderHeld();
            bool shoulderPressed = Input.GetKeyDown(
                                       keyboardFocusAndShutterKey)
                                   || AdaptiveLegacyGamepadInput
                                       .WasRightShoulderPressedThisFrame();
            float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);

            switch (captureState)
            {
                case PhotoCaptureState.Framing:
                    if (shoulderPressed)
                        BeginFocus();
                    break;

                case PhotoCaptureState.Focusing:
                    if (!shoulderHeld)
                    {
                        CancelFocus();
                        break;
                    }

                    focusElapsed = Mathf.Min(
                        focusDuration,
                        focusElapsed + deltaTime);
                    if (FocusProgress01 >= 1f)
                        CompleteFocus();
                    break;

                case PhotoCaptureState.Focused:
                    if (AdaptiveLegacyGamepadInput
                            .ReadRightStick().magnitude
                        > focusRetargetCancelDeadZone)
                    {
                        CancelFocus();
                        break;
                    }

                    if (!shoulderHeld)
                        shutterArmed = true;
                    if (shutterArmed && shoulderPressed)
                        BeginCapture();
                    break;

                case PhotoCaptureState.Capturing:
                    UpdateShutter(deltaTime);
                    break;

                case PhotoCaptureState.Reviewing:
                    break;
            }
        }

        private void BeginFocus()
        {
            captureState = PhotoCaptureState.Focusing;
            focusElapsed = 0f;
            shutterArmed = false;
            shutterElapsed = 0f;
            captureMomentFired = false;
            photoReviewRequested = false;
            mover?.SetPhotoModeInputLocked(true);
        }

        private void CompleteFocus()
        {
            focusElapsed = Mathf.Max(0.05f, focusDuration);
            captureState = PhotoCaptureState.Focused;
            shutterArmed = false;
            mover?.SetPhotoModeInputLocked(true);
        }

        private void CancelFocus()
        {
            captureState = PhotoCaptureState.Framing;
            focusElapsed = 0f;
            shutterArmed = false;
            shutterElapsed = 0f;
            captureMomentFired = false;
            photoReviewRequested = false;
            if (state == PhotoModeState.Active)
                mover?.SetPhotoModeInputLocked(false);
        }

        private void BeginCapture()
        {
            captureState = PhotoCaptureState.Capturing;
            shutterElapsed = 0f;
            captureMomentFired = false;
            shutterArmed = false;
            photoReviewRequested = false;
            mover?.SetPhotoModeInputLocked(true);
        }

        private void UpdateShutter(float deltaTime)
        {
            float flashIn = Mathf.Max(0.01f, shutterFlashInDuration);
            float hold = Mathf.Max(0f, shutterFlashHoldDuration);
            float flashOut = Mathf.Max(0.01f, shutterFlashOutDuration);
            float totalDuration = flashIn + hold + flashOut;
            shutterElapsed = Mathf.Min(
                totalDuration,
                shutterElapsed + deltaTime);

            if (!captureMomentFired && shutterElapsed >= flashIn)
            {
                captureMomentFired = true;
                PhotoCaptured?.Invoke();
            }

            if (shutterElapsed < totalDuration)
                return;

            bool enterReview = photoReviewRequested;
            captureState = enterReview
                ? PhotoCaptureState.Reviewing
                : PhotoCaptureState.Framing;
            focusElapsed = 0f;
            shutterArmed = false;
            captureMomentFired = false;
            photoReviewRequested = false;
            mover?.SetPhotoModeInputLocked(enterReview);
        }

        private void UpdateFocusPresentation()
        {
            bool focusPresentationActive =
                captureState == PhotoCaptureState.Focusing
                || captureState == PhotoCaptureState.Focused
                || captureState == PhotoCaptureState.Capturing;
            if (focusPresentationActive)
            {
                focusPresentation01 = FocusProgress01;
                focusPresentationVelocity = 0f;
                return;
            }

            focusPresentation01 = Mathf.SmoothDamp(
                focusPresentation01,
                0f,
                ref focusPresentationVelocity,
                Mathf.Max(0.05f, focusCancelBlendDuration),
                Mathf.Infinity,
                Mathf.Max(0f, Time.unscaledDeltaTime));
            if (focusPresentation01 <= 0.0001f)
            {
                focusPresentation01 = 0f;
                focusPresentationVelocity = 0f;
            }
        }

        private void ApplyFocusCameraPresentation()
        {
            float easedFocus = Mathf.SmoothStep(
                0f,
                1f,
                focusPresentation01);
            cameraShake?.SetPhotoFocusMagnification(
                Mathf.Lerp(1f, focusZoomMagnification, easedFocus));
        }

        private void ResetCaptureState(bool resetPresentation)
        {
            captureState = PhotoCaptureState.Framing;
            focusElapsed = 0f;
            shutterArmed = false;
            shutterElapsed = 0f;
            captureMomentFired = false;
            photoReviewRequested = false;
            if (!resetPresentation)
                return;

            focusPresentation01 = 0f;
            focusPresentationVelocity = 0f;
            cameraShake?.SetPhotoFocusMagnification(1f);
        }

        private void UpdateAimFromRightStick()
        {
            Vector2 rawStick = AdaptiveLegacyGamepadInput.ReadRightStick();
            if (!photoStickArmed)
            {
                if (rawStick.magnitude <= rightStickDeadZone)
                    photoStickArmed = true;
                else
                    return;
            }

            Vector2 input = ApplyRadialDeadZone(rawStick, rightStickDeadZone);
            if (input.sqrMagnitude > 0.000001f)
            {
                input = input.normalized
                        * Mathf.Pow(
                            Mathf.Clamp01(input.magnitude),
                            rightStickExponent);
            }

            Vector2 target = AimLocalPosition;
            target.x += input.x * lateralAimSpeed * Time.deltaTime;
            target.y += input.y * depthAimSpeed * Time.deltaTime;
            target.y = Mathf.Clamp(
                target.y,
                minimumAimDistance,
                maximumAimDistance);

            float halfWidth = CalculateHalfWidthAtDistance(target.y);
            target.x = Mathf.Clamp(target.x, -halfWidth, halfWidth);
            AimLocalPosition = target;
        }

        private void UpdateEntryPresentation()
        {
            entryElapsed = Mathf.Min(
                entryDuration,
                entryElapsed + Mathf.Max(0f, Time.unscaledDeltaTime));
            UpdateAimFollowTarget();
            ApplyFocusCameraPresentation();

            float progress = Reveal01;
            float shakeEnvelope = Mathf.Sin(progress * Mathf.PI);
            cameraShake?.SetPhotoModeRevealShake(
                shakeEnvelope,
                entryShakePositionAmplitude,
                entryShakeRotationAmplitude,
                entryShakeFrequency);

            if (progress < 1f)
                return;

            state = PhotoModeState.Active;
            mover?.SetPhotoModeInputLocked(false);
            cameraShake?.SetPhotoModeRevealShake(0f, 0f, 0f, 1f);
            photoStickArmed = AdaptiveLegacyGamepadInput
                .ReadRightStick().magnitude <= rightStickDeadZone;
        }

        private void BeginExitPresentation()
        {
            ResolveCameraReferences();
            state = PhotoModeState.Exiting;
            exitElapsed = 0f;
            photoStickArmed = false;
            ResetCaptureState(false);
            mover?.SetPhotoModeInputLocked(true);
            cameraFollow?.FollowBalanceTargetSmooth(
                balance,
                returnCameraPositionDamping,
                returnCameraBlendDuration);
            cameraShake?.SetPhotoModeRevealShake(
                0f,
                entryShakePositionAmplitude,
                entryShakeRotationAmplitude,
                entryShakeFrequency);
        }

        private void UpdateExitPresentation()
        {
            exitElapsed = Mathf.Min(
                exitDuration,
                exitElapsed + Mathf.Max(0f, Time.unscaledDeltaTime));
            UpdateAimFollowTarget();
            ApplyFocusCameraPresentation();

            float progress = Mathf.Clamp01(
                exitElapsed / Mathf.Max(0.05f, exitDuration));
            float shakeEnvelope = Mathf.Sin(progress * Mathf.PI) * 0.65f;
            cameraShake?.SetPhotoModeRevealShake(
                shakeEnvelope,
                entryShakePositionAmplitude,
                entryShakeRotationAmplitude,
                entryShakeFrequency);

            if (progress < 1f)
                return;

            CompleteExitPresentation();
        }

        private void CompleteExitPresentation()
        {
            state = PhotoModeState.Inactive;
            entryElapsed = 0f;
            exitElapsed = 0f;
            photoStickArmed = false;
            ResetCaptureState(true);
            mover?.SetPhotoModeInputLocked(false);
            cameraShake?.SetPhotoModeRevealShake(0f, 0f, 0f, 1f);
            ModeChanged?.Invoke(false);
        }

        private void ExitPhotoModeImmediately()
        {
            bool wasActive = IsActive;
            state = PhotoModeState.Inactive;
            entryElapsed = 0f;
            exitElapsed = 0f;
            photoStickArmed = false;
            ResetCaptureState(true);
            mover?.SetPhotoModeInputLocked(false);
            cameraShake?.SetPhotoModeRevealShake(0f, 0f, 0f, 1f);
            if (wasActive)
            {
                cameraFollow?.FollowBalanceTargetSmooth(
                    balance,
                    returnCameraPositionDamping,
                    returnCameraBlendDuration);
                ModeChanged?.Invoke(false);
            }
        }

        private float CalculateHalfWidthAtDistance(float forwardDistance)
        {
            float forwardRun = Mathf.Max(
                0f,
                forwardDistance - guideOriginForwardDistance);
            float halfWidth = guideOriginSideOffset
                              + Mathf.Tan(
                                  guideHalfAngleDegrees * Mathf.Deg2Rad)
                              * forwardRun;
            return Mathf.Max(0f, halfWidth - lateralFramePadding);
        }

        private void ResetAimToNearest()
        {
            AimLocalPosition = new Vector2(0f, minimumAimDistance);
        }

        private void EnsureAimFollowTarget()
        {
            if (aimFollowTarget != null)
                return;

            Transform existing = transform.Find("Photo Aim Camera Target");
            if (existing != null)
            {
                aimFollowTarget = existing;
                return;
            }

            var targetObject = new GameObject("Photo Aim Camera Target");
            aimFollowTarget = targetObject.transform;
            aimFollowTarget.SetParent(transform, false);
        }

        private void UpdateAimFollowTarget()
        {
            EnsureAimFollowTarget();
            if (aimFollowTarget == null)
                return;

            float followFraction = Mathf.Lerp(
                normalCameraAimFollowFraction,
                focusedCameraAimFollowFraction,
                Mathf.SmoothStep(0f, 1f, focusPresentation01));
            aimFollowTarget.localPosition = new Vector3(
                AimLocalPosition.x * followFraction,
                AimLocalPosition.y * followFraction,
                0f);
            aimFollowTarget.localRotation = Quaternion.identity;
        }

        private void ResolveCameraReferences()
        {
            if (cameraFollow != null && cameraShake != null)
                return;

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
                return;

            if (cameraFollow == null)
                cameraFollow = mainCamera.GetComponent<RobotCameraFollow>();
            if (cameraShake == null)
                cameraShake = mainCamera.GetComponent<RobotCameraShake>();
        }

        private bool CanEnterPhotoMode()
        {
            return mover != null
                   && mover.MovementMode == RobotMovementMode.Driven
                   && !mover.IsArmInputCaptured
                   && (balance == null || !balance.IsTippedOver);
        }

        private bool CanRemainInPhotoMode()
        {
            return mover != null
                   && mover.MovementMode == RobotMovementMode.Driven
                   && (balance == null || !balance.IsTippedOver);
        }

        private Vector3 LocalPointToWorld(Vector2 localPoint)
        {
            return transform.position
                   + transform.right * localPoint.x
                   + transform.up * localPoint.y;
        }

        private void OnRobotTippedOver(RobotTipOverInfo _)
        {
            ExitPhotoModeImmediately();
        }

        private static Vector2 ApplyRadialDeadZone(
            Vector2 value,
            float deadZone)
        {
            float magnitude = value.magnitude;
            if (magnitude <= deadZone)
                return Vector2.zero;

            float normalizedMagnitude = Mathf.InverseLerp(
                deadZone,
                1f,
                magnitude);
            return value.normalized * Mathf.Clamp01(normalizedMagnitude);
        }

        private void OnDisable()
        {
            if (balance != null)
                balance.TippedOver -= OnRobotTippedOver;
            ExitPhotoModeImmediately();
        }

        private void OnValidate()
        {
            rightStickDeadZone = Mathf.Clamp(rightStickDeadZone, 0f, 0.9f);
            rightStickExponent = Mathf.Clamp(rightStickExponent, 1f, 3f);
            guideOriginSideOffset = Mathf.Max(0f, guideOriginSideOffset);
            guideOriginForwardDistance = Mathf.Max(
                0f,
                guideOriginForwardDistance);
            guideHalfAngleDegrees = Mathf.Clamp(
                guideHalfAngleDegrees,
                1f,
                75f);
            minimumAimDistance = Mathf.Max(
                guideOriginForwardDistance + 0.01f,
                minimumAimDistance);
            maximumAimDistance = Mathf.Max(
                minimumAimDistance + 0.1f,
                maximumAimDistance);
            lateralFramePadding = Mathf.Max(0f, lateralFramePadding);
            lateralAimSpeed = Mathf.Max(0f, lateralAimSpeed);
            depthAimSpeed = Mathf.Max(0f, depthAimSpeed);
            focusDuration = Mathf.Max(0.05f, focusDuration);
            focusCancelBlendDuration = Mathf.Max(
                0.05f,
                focusCancelBlendDuration);
            focusZoomMagnification = Mathf.Max(
                1f,
                focusZoomMagnification);
            normalCameraAimFollowFraction = Mathf.Clamp01(
                normalCameraAimFollowFraction);
            focusedCameraAimFollowFraction = Mathf.Clamp01(
                focusedCameraAimFollowFraction);
            focusRetargetCancelDeadZone = Mathf.Clamp(
                focusRetargetCancelDeadZone,
                0.1f,
                1f);
            shutterFlashInDuration = Mathf.Max(
                0.01f,
                shutterFlashInDuration);
            shutterFlashHoldDuration = Mathf.Max(
                0f,
                shutterFlashHoldDuration);
            shutterFlashOutDuration = Mathf.Max(
                0.01f,
                shutterFlashOutDuration);
            entryDuration = Mathf.Max(0.05f, entryDuration);
            exitDuration = Mathf.Max(0.05f, exitDuration);
            photoCameraPositionDamping = Mathf.Max(
                0f,
                photoCameraPositionDamping);
            returnCameraPositionDamping = Mathf.Max(
                0f,
                returnCameraPositionDamping);
            returnCameraBlendDuration = Mathf.Max(
                0.05f,
                returnCameraBlendDuration);
            entryShakePositionAmplitude = Mathf.Max(
                0f,
                entryShakePositionAmplitude);
            entryShakeRotationAmplitude = Mathf.Max(
                0f,
                entryShakeRotationAmplitude);
            entryShakeFrequency = Mathf.Max(0.1f, entryShakeFrequency);
        }
    }
}
