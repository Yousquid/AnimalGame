using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Drives the authored Scan_Idle, Scan_Hold, and Scan_Release sprite clips,
    /// plus an outward release ring centred on the fixed player UI.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class ScanChargeUI : MonoBehaviour
    {
        private enum ScanVisualState
        {
            Idle,
            Charging,
            Charged,
            Releasing
        }

        private enum ScanRingPhase
        {
            Hidden,
            Expanding,
            ExpansionComplete
        }

        private enum ScanCameraZoomPhase
        {
            Base,
            Charging,
            Charged,
            Releasing,
            Cancelling
        }

        private const string IdleClipName = "Scan_Idle";
        private const string HoldClipName = "Scan_Hold";
        private const string ReleaseClipName = "Scan_Release";

        private static readonly int IdleStateHash =
            Animator.StringToHash("Base Layer.Scan_Idle");
        private static readonly int HoldStateHash =
            Animator.StringToHash("Base Layer.Scan_Hold");
        private static readonly int ReleaseStateHash =
            Animator.StringToHash("Base Layer.Scan_Release");

        [Header("References")]
        [Tooltip("Animator containing the authored Scan_Idle, Scan_Hold, and Scan_Release sprite clips.")]
        [SerializeField] private Animator animator;

        [Header("Charge Input")]
        [Tooltip("Keyboard key held to charge a scan.")]
        [SerializeField] private KeyCode keyboardScanKey = KeyCode.E;

        [Tooltip("Legacy Input Manager fallback for Xbox LB and Sony L1. JoystickButton4 is the left shoulder button on both supported layouts.")]
        [SerializeField] private KeyCode gamepadScanButton =
            KeyCode.JoystickButton4;

        [Header("Animation Timing")]
        [Tooltip("Seconds required to play from the first through the last frame of Scan_Hold.")]
        [SerializeField, Min(0.05f)] private float maximumChargeDuration = 1.5f;

        [Tooltip("Seconds required to play the complete authored Scan_Release clip. Lower values make the activation-key animation finish faster.")]
        [SerializeField, Min(0.05f)] private float releaseDuration = 0.3f;

        [Tooltip("Independent duration of the outward scan ring. This starts with Scan_Release but may finish before or after the activation-key animation.")]
        [SerializeField, Min(0.05f)] private float releaseRingExpansionDuration = 0.65f;

        [Header("Scan Camera Zoom")]
        [Tooltip("Enables the camera-size animation during Scan_Hold and Scan_Release.")]
        [SerializeField] private bool enableScanCameraZoom = true;

        [Tooltip("Orthographic Size reached at full charge. This should be smaller than the camera base size (currently 9) to zoom in.")]
        [SerializeField, Min(0.01f)] private float holdTargetOrthographicSize = 7.5f;

        [Tooltip("Largest Orthographic Size reached during the first part of Scan_Release. This should be larger than the camera base size to zoom out.")]
        [SerializeField, Min(0.01f)] private float releasePeakOrthographicSize = 11f;

        [Tooltip("Independent seconds used to move from the charged close view to the large release view. Increase this for a softer Hold-to-Release transition.")]
        [SerializeField, Min(0.05f)] private float releaseZoomOutDuration = 0.45f;

        [Tooltip("Independent seconds used to move from the large release view back to the normal camera size.")]
        [SerializeField, Min(0.05f)] private float releaseZoomReturnDuration = 0.55f;

        [Tooltip("Seconds used to return to the original size when charging is cancelled before completion.")]
        [SerializeField, Min(0.01f)] private float cancelledChargeZoomReturnDuration = 0.2f;

        [Header("Fully Charged Camera Shake")]
        [Tooltip("Adds a continuous camera vibration while a fully charged scan is still being held.")]
        [SerializeField] private bool enableFullyChargedCameraShake = true;

        [Tooltip("Seconds for the fully charged vibration to build from zero to full strength.")]
        [SerializeField, Min(0.01f)] private float fullyChargedShakeBuildUpDuration = 0.4f;

        [Tooltip("Maximum camera position vibration caused by holding a fully charged scan, in world units.")]
        [SerializeField, Min(0f)] private float fullyChargedShakePositionAmplitude = 0.035f;

        [Tooltip("Maximum camera rotation vibration caused by holding a fully charged scan, in degrees.")]
        [SerializeField, Min(0f)] private float fullyChargedShakeRotationAmplitude = 0.45f;

        [Tooltip("Frequency of the fully charged scan vibration.")]
        [SerializeField, Min(0.1f)] private float fullyChargedShakeFrequency = 4.5f;

        [Header("Scan Ring")]
        [Tooltip("Shows the outward-release scan ring.")]
        [SerializeField] private bool showScanRing = true;

        [Tooltip("Final release-ring radius at the main UI boundary, in reference-canvas pixels.")]
        [SerializeField, Min(8f)] private float uiRingRadiusPixels = 430f;

        [Tooltip("Initial release-ring radius at the fixed player UI centre, in reference-canvas pixels.")]
        [SerializeField, Min(1f)] private float robotRingRadiusPixels = 43f;

        [Tooltip("Thickness of the scan ring in reference-canvas pixels.")]
        [SerializeField, Min(0.25f)] private float scanRingThicknessPixels = 2f;

        [Tooltip("Number of segments used for the procedural circle. Higher values make a smoother circle.")]
        [SerializeField, Range(24, 256)] private int scanRingSegments = 128;

        [Tooltip("Colour and opacity used while the released scan ring expands.")]
        [SerializeField] private Color releaseRingColor =
            new Color(0.92f, 0.98f, 1f, 0.92f);

        private ScanVisualState state;
        private ScanRingPhase ringPhase;
        private float chargeElapsed;
        private float releaseElapsed;
        private float releaseRingElapsed;
        private float releaseCameraZoomElapsed;

        private ScanCameraZoomPhase cameraZoomPhase;
        private RobotCameraShake cameraShake;
        private bool cameraZoomInitialized;
        private float baseCameraOrthographicSize = 9f;
        private float currentScanOrthographicSize = 9f;
        private float chargeZoomStartSize = 9f;
        private float releaseZoomStartSize = 9f;
        private float cancelledZoomStartSize = 9f;
        private float cancelledZoomElapsed;
        private float fullyChargedShakeElapsed;

        private Camera mapCamera;
        private RectTransform ringCoordinateSpace;
        private ScanPulseRingGraphic ringGraphic;

        public float Charge01 => Mathf.Clamp01(
            chargeElapsed / Mathf.Max(0.05f, maximumChargeDuration));
        public bool IsFullyCharged => state == ScanVisualState.Charged;
        public bool IsCharging => state == ScanVisualState.Charging
                                  || state == ScanVisualState.Charged;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            ringCoordinateSpace = transform as RectTransform;
            CreateScanRing();
            ResolveTrackingReferences();
            ValidateAuthoredClips();
        }

        private void OnEnable()
        {
            EnterIdle(false);
            ResetScanCameraZoomImmediate();
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;

            if (!showScanRing && ringPhase != ScanRingPhase.Hidden)
                HideScanRing();

            UpdateReleaseRing(deltaTime);

            bool scanHeld = IsScanInputHeld();
            switch (state)
            {
                case ScanVisualState.Idle:
                    if (scanHeld)
                        BeginCharge();
                    break;

                case ScanVisualState.Charging:
                    if (!scanHeld)
                    {
                        // Releasing before maximum charge cancels the scan.
                        CancelCharge();
                        break;
                    }

                    chargeElapsed = Mathf.Min(
                        maximumChargeDuration,
                        chargeElapsed + deltaTime);
                    float charge01 = Charge01;
                    SampleAuthoredState(HoldStateHash, charge01);
                    if (charge01 >= 1f)
                    {
                        state = ScanVisualState.Charged;
                        cameraZoomPhase = ScanCameraZoomPhase.Charged;
                    }
                    break;

                case ScanVisualState.Charged:
                    // Keep the final Hold sprite visible until the completed charge is released.
                    if (!scanHeld)
                        BeginRelease();
                    break;

                case ScanVisualState.Releasing:
                    releaseElapsed = Mathf.Min(
                        releaseDuration,
                        releaseElapsed + deltaTime);
                    float release01 = Mathf.Clamp01(
                        releaseElapsed / releaseDuration);
                    SampleAuthoredState(ReleaseStateHash, release01);
                    if (release01 >= 1f)
                    {
                        // The ring and camera zoom have independent durations
                        // and may continue after the authored release clip ends.
                        EnterIdle(true);
                    }
                    break;
            }

            UpdateScanCameraZoom(deltaTime);
            ApplyScanCameraZoom();
            UpdateFullyChargedCameraShake(scanHeld, deltaTime);
        }

        private void LateUpdate()
        {
            PinRingToPlayerUiCenter();
        }

        private void BeginCharge()
        {
            ResolveTrackingReferences();
            state = ScanVisualState.Charging;
            chargeElapsed = 0f;
            releaseElapsed = 0f;
            fullyChargedShakeElapsed = 0f;
            chargeZoomStartSize = currentScanOrthographicSize;
            cameraZoomPhase = ScanCameraZoomPhase.Charging;
            HideScanRing();
            SampleAuthoredState(HoldStateHash, 0f);
        }

        private void CancelCharge()
        {
            cancelledZoomStartSize = currentScanOrthographicSize;
            cancelledZoomElapsed = 0f;
            cameraZoomPhase = ScanCameraZoomPhase.Cancelling;
            EnterIdle(false);
        }

        private void BeginRelease()
        {
            ClearFullyChargedCameraShake();
            state = ScanVisualState.Releasing;
            releaseElapsed = 0f;
            releaseRingElapsed = 0f;
            releaseCameraZoomElapsed = 0f;
            releaseZoomStartSize = currentScanOrthographicSize;
            cameraZoomPhase = ScanCameraZoomPhase.Releasing;
            ringPhase = ScanRingPhase.Expanding;
            SampleAuthoredState(ReleaseStateHash, 0f);
            ShowRingAt(robotRingRadiusPixels, releaseRingColor);
        }

        private void EnterIdle(bool preserveReleaseRing)
        {
            state = ScanVisualState.Idle;
            chargeElapsed = 0f;
            releaseElapsed = 0f;
            SampleAuthoredState(IdleStateHash, 0f);

            if (!preserveReleaseRing
                || (ringPhase != ScanRingPhase.Expanding
                    && ringPhase != ScanRingPhase.ExpansionComplete))
            {
                HideScanRing();
            }
        }

        private void UpdateScanCameraZoom(float deltaTime)
        {
            ResolveTrackingReferences();
            if (!cameraZoomInitialized)
                return;

            if (!enableScanCameraZoom)
            {
                cameraZoomPhase = ScanCameraZoomPhase.Base;
                currentScanOrthographicSize = baseCameraOrthographicSize;
                return;
            }

            switch (cameraZoomPhase)
            {
                case ScanCameraZoomPhase.Charging:
                    currentScanOrthographicSize = Mathf.Lerp(
                        chargeZoomStartSize,
                        holdTargetOrthographicSize,
                        Mathf.SmoothStep(0f, 1f, Charge01));
                    break;

                case ScanCameraZoomPhase.Charged:
                    currentScanOrthographicSize = holdTargetOrthographicSize;
                    break;

                case ScanCameraZoomPhase.Releasing:
                    releaseCameraZoomElapsed += deltaTime;
                    float outwardDuration = Mathf.Max(
                        0.05f,
                        releaseZoomOutDuration);
                    float returnDuration = Mathf.Max(
                        0.05f,
                        releaseZoomReturnDuration);
                    if (releaseCameraZoomElapsed <= outwardDuration)
                    {
                        float outward01 = Mathf.Clamp01(
                            releaseCameraZoomElapsed / outwardDuration);
                        currentScanOrthographicSize = Mathf.Lerp(
                            releaseZoomStartSize,
                            releasePeakOrthographicSize,
                            Mathf.SmoothStep(0f, 1f, outward01));
                    }
                    else
                    {
                        float returnElapsed =
                            releaseCameraZoomElapsed - outwardDuration;
                        float return01 = Mathf.Clamp01(
                            returnElapsed / returnDuration);
                        currentScanOrthographicSize = Mathf.Lerp(
                            releasePeakOrthographicSize,
                            baseCameraOrthographicSize,
                            Mathf.SmoothStep(0f, 1f, return01));
                        if (return01 >= 1f)
                            cameraZoomPhase = ScanCameraZoomPhase.Base;
                    }
                    break;

                case ScanCameraZoomPhase.Cancelling:
                    cancelledZoomElapsed = Mathf.Min(
                        cancelledChargeZoomReturnDuration,
                        cancelledZoomElapsed + deltaTime);
                    float cancel01 = Mathf.Clamp01(
                        cancelledZoomElapsed
                        / Mathf.Max(0.01f, cancelledChargeZoomReturnDuration));
                    currentScanOrthographicSize = Mathf.Lerp(
                        cancelledZoomStartSize,
                        baseCameraOrthographicSize,
                        Mathf.SmoothStep(0f, 1f, cancel01));
                    if (cancel01 >= 1f)
                        cameraZoomPhase = ScanCameraZoomPhase.Base;
                    break;

                default:
                    currentScanOrthographicSize = baseCameraOrthographicSize;
                    break;
            }
        }

        private void ApplyScanCameraZoom()
        {
            ResolveTrackingReferences();
            if (!cameraZoomInitialized || mapCamera == null)
                return;

            float desiredSize = enableScanCameraZoom
                ? currentScanOrthographicSize
                : baseCameraOrthographicSize;
            desiredSize = Mathf.Max(0.01f, desiredSize);

            if (cameraShake != null && cameraShake.isActiveAndEnabled)
            {
                cameraShake.SetScanZoomMultiplier(
                    desiredSize / Mathf.Max(0.01f, baseCameraOrthographicSize));
            }
            else
            {
                mapCamera.orthographicSize = desiredSize;
            }
        }

        private void ResetScanCameraZoomImmediate()
        {
            ResolveTrackingReferences();
            cameraZoomPhase = ScanCameraZoomPhase.Base;
            releaseCameraZoomElapsed = 0f;
            cancelledZoomElapsed = 0f;
            if (cameraZoomInitialized)
                currentScanOrthographicSize = baseCameraOrthographicSize;
            ApplyScanCameraZoom();
        }

        private void UpdateFullyChargedCameraShake(
            bool scanHeld,
            float deltaTime)
        {
            ResolveTrackingReferences();
            bool shouldShake = enableFullyChargedCameraShake
                               && scanHeld
                               && state == ScanVisualState.Charged;
            if (!shouldShake)
            {
                ClearFullyChargedCameraShake();
                return;
            }

            fullyChargedShakeElapsed += Mathf.Max(0f, deltaTime);
            float buildUp01 = Mathf.Clamp01(
                fullyChargedShakeElapsed
                / Mathf.Max(0.01f, fullyChargedShakeBuildUpDuration));
            float strength = Mathf.SmoothStep(0f, 1f, buildUp01);
            if (cameraShake != null)
            {
                cameraShake.SetScanChargeShake(
                    strength,
                    fullyChargedShakePositionAmplitude,
                    fullyChargedShakeRotationAmplitude,
                    fullyChargedShakeFrequency);
            }
        }

        private void ClearFullyChargedCameraShake()
        {
            fullyChargedShakeElapsed = 0f;
            if (cameraShake != null)
                cameraShake.SetScanChargeShake(0f, 0f, 0f, 1f);
        }

        private void UpdateReleaseRing(float deltaTime)
        {
            if (ringPhase == ScanRingPhase.ExpansionComplete)
            {
                HideScanRing();
                return;
            }

            if (ringPhase != ScanRingPhase.Expanding)
                return;

            if (!showScanRing)
            {
                HideScanRing();
                return;
            }

            releaseRingElapsed = Mathf.Min(
                releaseRingExpansionDuration,
                releaseRingElapsed + deltaTime);
            float progress = Mathf.Clamp01(
                releaseRingElapsed / releaseRingExpansionDuration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float radius = Mathf.Lerp(
                robotRingRadiusPixels,
                uiRingRadiusPixels,
                eased);
            ShowRingAt(radius, releaseRingColor);

            // Leave the exact final radius visible for one rendered frame.
            if (progress >= 1f)
                ringPhase = ScanRingPhase.ExpansionComplete;
        }

        private void ShowRingAt(float radius, Color displayedColor)
        {
            if (!showScanRing || ringGraphic == null)
                return;

            if (!ringGraphic.gameObject.activeSelf)
                ringGraphic.gameObject.SetActive(true);

            ringGraphic.SetRing(
                radius,
                scanRingThicknessPixels,
                scanRingSegments,
                displayedColor);
        }

        private void HideScanRing()
        {
            ringPhase = ScanRingPhase.Hidden;
            releaseRingElapsed = 0f;
            if (ringGraphic != null && ringGraphic.gameObject.activeSelf)
                ringGraphic.gameObject.SetActive(false);
        }

        private void CreateScanRing()
        {
            if (ringGraphic != null || ringCoordinateSpace == null)
                return;

            var ringObject = new GameObject(
                "Scan Release Ring",
                typeof(RectTransform),
                typeof(CanvasRenderer));
            ringObject.layer = gameObject.layer;
            ringObject.transform.SetParent(ringCoordinateSpace, false);

            ringGraphic = ringObject.AddComponent<ScanPulseRingGraphic>();
            ringGraphic.raycastTarget = false;

            RectTransform rect = ringGraphic.rectTransform;
            rect.anchorMin = Vector2.one * 0.5f;
            rect.anchorMax = Vector2.one * 0.5f;
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            ringObject.SetActive(false);
        }

        private void PinRingToPlayerUiCenter()
        {
            if (ringGraphic == null || !ringGraphic.gameObject.activeSelf)
                return;

            // Scan Activation Controls stretches across the canvas with a
            // centred pivot, so zero is the stable centre of the player UI.
            ringGraphic.enabled = true;
            ringGraphic.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void ResolveTrackingReferences()
        {
            if (mapCamera == null || !mapCamera.isActiveAndEnabled)
            {
                Camera resolvedCamera = Camera.main;
                if (resolvedCamera != mapCamera)
                {
                    if (cameraShake != null)
                    {
                        cameraShake.SetScanZoomMultiplier(1f);
                        cameraShake.SetScanChargeShake(0f, 0f, 0f, 1f);
                    }
                    mapCamera = resolvedCamera;
                    cameraShake = null;
                    cameraZoomInitialized = false;
                }
            }

            if (!cameraZoomInitialized && mapCamera != null)
            {
                cameraShake = mapCamera.GetComponent<RobotCameraShake>();
                baseCameraOrthographicSize = cameraShake != null
                    ? cameraShake.BaseOrthographicSize
                    : Mathf.Max(0.01f, mapCamera.orthographicSize);
                currentScanOrthographicSize = baseCameraOrthographicSize;
                chargeZoomStartSize = baseCameraOrthographicSize;
                releaseZoomStartSize = baseCameraOrthographicSize;
                cancelledZoomStartSize = baseCameraOrthographicSize;
                cameraZoomInitialized = true;
            }

        }

        private void SampleAuthoredState(int stateHash, float normalizedTime)
        {
            if (animator == null)
                return;

            // Script-owned time maps the adjustable durations onto the authored
            // Sprite frames without adding procedural animation to those frames.
            animator.speed = 0f;
            animator.Play(stateHash, 0, Mathf.Clamp01(normalizedTime));
            animator.Update(0f);
        }

        private void ValidateAuthoredClips()
        {
            RuntimeAnimatorController controller =
                animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null)
                return;

            Debug.Assert(HasClip(controller, IdleClipName),
                "Scan UI controller is missing Scan_Idle.", this);
            Debug.Assert(HasClip(controller, HoldClipName),
                "Scan UI controller is missing Scan_Hold.", this);
            Debug.Assert(HasClip(controller, ReleaseClipName),
                "Scan UI controller is missing Scan_Release.", this);
        }

        private static bool HasClip(
            RuntimeAnimatorController controller,
            string clipName)
        {
            foreach (AnimationClip clip in controller.animationClips)
            {
                if (clip != null && clip.name == clipName)
                    return true;
            }

            return false;
        }

        private bool IsScanInputHeld()
        {
            if (Input.GetKey(keyboardScanKey)
                || Input.GetKey(gamepadScanButton))
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad != null
                    && gamepad.added
                    // The Input System maps Xbox LB and Sony L1 to leftShoulder.
                    && gamepad.leftShoulder.isPressed)
                {
                    return true;
                }
            }
#endif
            return false;
        }

        private void OnDisable()
        {
            if (animator != null)
                animator.speed = 0f;
            HideScanRing();
            ClearFullyChargedCameraShake();
            ResetScanCameraZoomImmediate();
        }

        private void OnValidate()
        {
            maximumChargeDuration = Mathf.Max(0.05f, maximumChargeDuration);
            releaseDuration = Mathf.Max(0.05f, releaseDuration);
            releaseRingExpansionDuration = Mathf.Max(
                0.05f,
                releaseRingExpansionDuration);
            holdTargetOrthographicSize = Mathf.Max(
                0.01f,
                holdTargetOrthographicSize);
            releasePeakOrthographicSize = Mathf.Max(
                0.01f,
                releasePeakOrthographicSize);
            releaseZoomOutDuration = Mathf.Max(
                0.05f,
                releaseZoomOutDuration);
            releaseZoomReturnDuration = Mathf.Max(
                0.05f,
                releaseZoomReturnDuration);
            cancelledChargeZoomReturnDuration = Mathf.Max(
                0.01f,
                cancelledChargeZoomReturnDuration);
            fullyChargedShakeBuildUpDuration = Mathf.Max(
                0.01f,
                fullyChargedShakeBuildUpDuration);
            fullyChargedShakePositionAmplitude = Mathf.Max(
                0f,
                fullyChargedShakePositionAmplitude);
            fullyChargedShakeRotationAmplitude = Mathf.Max(
                0f,
                fullyChargedShakeRotationAmplitude);
            fullyChargedShakeFrequency = Mathf.Max(
                0.1f,
                fullyChargedShakeFrequency);
            uiRingRadiusPixels = Mathf.Max(8f, uiRingRadiusPixels);
            robotRingRadiusPixels = Mathf.Clamp(
                robotRingRadiusPixels,
                1f,
                uiRingRadiusPixels);
            scanRingThicknessPixels = Mathf.Max(
                0.25f,
                scanRingThicknessPixels);
            scanRingSegments = Mathf.Clamp(scanRingSegments, 24, 256);
        }
    }

    [AddComponentMenu("")]
    public sealed class ScanPulseRingGraphic : MaskableGraphic
    {
        private float ringRadius = 100f;
        private float ringThickness = 2f;
        private int ringSegments = 128;
        private Color32 ringColor = Color.white;

        public void SetRing(
            float radius,
            float thickness,
            int segments,
            Color displayedColor)
        {
            ringRadius = Mathf.Max(0f, radius);
            ringThickness = Mathf.Max(0.25f, thickness);
            ringSegments = Mathf.Clamp(segments, 24, 256);
            ringColor = displayedColor;

            float diameter = (ringRadius + ringThickness + 2f) * 2f;
            rectTransform.sizeDelta = Vector2.one * diameter;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            Vector2 center = rectTransform.rect.center;
            float innerRadius = Mathf.Max(
                0f,
                ringRadius - ringThickness * 0.5f);
            float outerRadius = ringRadius + ringThickness * 0.5f;
            int startIndex = vertexHelper.currentVertCount;

            for (int i = 0; i <= ringSegments; i++)
            {
                float angle = i / (float)ringSegments * Mathf.PI * 2f;
                Vector2 radial = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));
                AddVertex(
                    vertexHelper,
                    center + radial * innerRadius,
                    ringColor);
                AddVertex(
                    vertexHelper,
                    center + radial * outerRadius,
                    ringColor);
            }

            for (int i = 0; i < ringSegments; i++)
            {
                int index = startIndex + i * 2;
                vertexHelper.AddTriangle(index, index + 1, index + 3);
                vertexHelper.AddTriangle(index, index + 3, index + 2);
            }
        }

        private static void AddVertex(
            VertexHelper vertexHelper,
            Vector2 position,
            Color32 vertexColor)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = vertexColor;
            vertex.uv0 = Vector2.zero;
            vertexHelper.AddVert(vertex);
        }
    }
}
