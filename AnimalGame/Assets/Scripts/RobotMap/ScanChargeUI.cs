using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.RobotMap
{
    /// <summary>
    /// Drives the authored Scan_Idle, Scan_Hold, and Scan_Release sprite clips,
    /// plus a screen-space scan ring centred on the robot.
    /// </summary>
    // Camera follow and camera shake finish in LateUpdate at orders 200/250.
    // Projecting the robot after them prevents the scan ring lagging by one frame.
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
            Contracting,
            Charged,
            Expanding,
            ExpansionComplete
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

        [Tooltip("Legacy Input Manager fallback for RB/R1. JoystickButton5 is the right shoulder button on the supported Xbox and Sony layouts.")]
        [SerializeField] private KeyCode gamepadScanButton =
            KeyCode.JoystickButton5;

        [Header("Animation Timing")]
        [Tooltip("Seconds required to play from the first through the last frame of Scan_Hold. The contraction ring uses this same progress.")]
        [SerializeField, Min(0.05f)] private float maximumChargeDuration = 1.5f;

        [Tooltip("Seconds required to play the complete authored Scan_Release clip. Lower values make the activation-key animation finish faster.")]
        [SerializeField, Min(0.05f)] private float releaseDuration = 0.3f;

        [Tooltip("Independent duration of the outward scan ring. This starts with Scan_Release but may finish before or after the activation-key animation.")]
        [SerializeField, Min(0.05f)] private float releaseRingExpansionDuration = 0.65f;

        [Header("Scan Ring")]
        [Tooltip("Shows the contraction and outward-release scan ring.")]
        [SerializeField] private bool showScanRing = true;

        [Tooltip("Ring radius at the main UI boundary, in reference-canvas pixels.")]
        [SerializeField, Min(8f)] private float uiRingRadiusPixels = 430f;

        [Tooltip("Ring radius when it has contracted onto the robot body, in reference-canvas pixels.")]
        [SerializeField, Min(1f)] private float robotRingRadiusPixels = 43f;

        [Tooltip("Thickness of the scan ring in reference-canvas pixels.")]
        [SerializeField, Min(0.25f)] private float scanRingThicknessPixels = 2f;

        [Tooltip("Number of segments used for the procedural circle. Higher values make a smoother circle.")]
        [SerializeField, Range(24, 256)] private int scanRingSegments = 128;

        [Tooltip("Colour and opacity used while the ring contracts during Scan_Hold.")]
        [SerializeField] private Color holdRingColor =
            new Color(0.92f, 0.98f, 1f, 0.78f);

        [Tooltip("Colour and opacity used while the released scan ring expands.")]
        [SerializeField] private Color releaseRingColor =
            new Color(0.92f, 0.98f, 1f, 0.92f);

        private ScanVisualState state;
        private ScanRingPhase ringPhase;
        private float chargeElapsed;
        private float releaseElapsed;
        private float releaseRingElapsed;

        private Camera mapCamera;
        private Transform robotTarget;
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
                        EnterIdle(false);
                        break;
                    }

                    chargeElapsed = Mathf.Min(
                        maximumChargeDuration,
                        chargeElapsed + deltaTime);
                    float charge01 = Charge01;
                    SampleAuthoredState(HoldStateHash, charge01);
                    UpdateContractionRing(charge01);
                    if (charge01 >= 1f)
                    {
                        state = ScanVisualState.Charged;
                        ringPhase = ScanRingPhase.Charged;
                        ShowRingAt(robotRingRadiusPixels, holdRingColor);
                    }
                    break;

                case ScanVisualState.Charged:
                    // Keep both the final Hold sprite and contracted circle
                    // on the robot until the completed charge is released.
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
                        // The ring has its own duration and is allowed to keep
                        // expanding after the authored release clip has ended.
                        EnterIdle(true);
                    }
                    break;
            }
        }

        private void LateUpdate()
        {
            UpdateRingScreenPosition();
        }

        private void BeginCharge()
        {
            state = ScanVisualState.Charging;
            chargeElapsed = 0f;
            releaseElapsed = 0f;
            ringPhase = ScanRingPhase.Contracting;
            SampleAuthoredState(HoldStateHash, 0f);
            ShowRingAt(uiRingRadiusPixels, holdRingColor);
        }

        private void BeginRelease()
        {
            state = ScanVisualState.Releasing;
            releaseElapsed = 0f;
            releaseRingElapsed = 0f;
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

        private void UpdateContractionRing(float charge01)
        {
            if (!showScanRing)
            {
                HideScanRing();
                return;
            }

            ringPhase = ScanRingPhase.Contracting;
            float eased = Mathf.SmoothStep(0f, 1f, charge01);
            float radius = Mathf.Lerp(
                uiRingRadiusPixels,
                robotRingRadiusPixels,
                eased);
            ShowRingAt(radius, holdRingColor);
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
                "Scan Contraction and Release Ring",
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

        private void UpdateRingScreenPosition()
        {
            if (ringGraphic == null || !ringGraphic.gameObject.activeSelf)
                return;

            ResolveTrackingReferences();
            if (mapCamera == null
                || robotTarget == null
                || ringCoordinateSpace == null)
            {
                ringGraphic.rectTransform.anchoredPosition = Vector2.zero;
                return;
            }

            Vector3 screenPosition = mapCamera.WorldToScreenPoint(
                robotTarget.position);
            if (screenPosition.z <= 0f)
            {
                ringGraphic.enabled = false;
                return;
            }

            ringGraphic.enabled = true;
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            Camera uiCamera = parentCanvas != null
                              && parentCanvas.renderMode
                              != RenderMode.ScreenSpaceOverlay
                ? parentCanvas.worldCamera
                : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    ringCoordinateSpace,
                    screenPosition,
                    uiCamera,
                    out Vector2 localPosition))
            {
                ringGraphic.rectTransform.anchoredPosition = localPosition;
            }
        }

        private void ResolveTrackingReferences()
        {
            if (mapCamera == null || !mapCamera.isActiveAndEnabled)
                mapCamera = Camera.main;

            if (robotTarget != null)
                return;

#pragma warning disable CS0618
            RobotMover robot = FindObjectOfType<RobotMover>();
#pragma warning restore CS0618
            if (robot != null)
                robotTarget = robot.transform;
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
                    && gamepad.rightShoulder.isPressed)
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
        }

        private void OnValidate()
        {
            maximumChargeDuration = Mathf.Max(0.05f, maximumChargeDuration);
            releaseDuration = Mathf.Max(0.05f, releaseDuration);
            releaseRingExpansionDuration = Mathf.Max(
                0.05f,
                releaseRingExpansionDuration);
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
