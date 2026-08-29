using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.RobotMap
{
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class PhotoModeUI : MonoBehaviour
    {
        [Header("Authored Artwork")]
        [SerializeField] private Sprite cameraFrameSprite;
        [SerializeField] private Sprite cameraAimSmallSprite;
        [SerializeField] private Sprite cameraAimBigSprite;

        [Header("Frame Presentation")]
        [Tooltip("Shared source size of the three aligned camera sprites.")]
        [SerializeField] private Vector2 frameReferenceSize =
            new Vector2(1920f, 1080f);

        [Tooltip("Interaction color applied to all three authored camera-frame sprites.")]
        [SerializeField] private Color cameraFrameColor =
            new Color(1f, 0.82f, 0.18f, 1f);

        [Tooltip("Camera-frame scale at the nearest photo distance.")]
        [SerializeField, Min(0.01f)] private float nearestFrameScale = 0.44f;

        [Tooltip("Camera-frame scale at the farthest photo distance.")]
        [SerializeField, Min(0.01f)] private float farthestFrameScale = 0.32f;

        [Tooltip("Maximum camera-frame width relative to the photo range's widest far edge. A uniform limiter preserves the near-large/far-small size curve.")]
        [SerializeField, Range(0.1f, 1f)]
        private float maximumFrameWidthOfRange = 0.72f;

        [Tooltip("Additional camera-frame line thickness in final screen pixels. This remains stable while the frame scales and moves.")]
        [SerializeField, Min(0f)]
        private float frameLineWidthIncreasePixels = 1.5f;

        [Header("Entry Reveal")]
        [Tooltip("Normalized entry time at which the dashed range starts drawing outward.")]
        [SerializeField, Range(0f, 1f)] private float guideRevealStart = 0.2f;
        [SerializeField, Range(0f, 1f)] private float guideRevealEnd = 0.78f;
        [Tooltip("Normalized entry time at which the yellow frame starts appearing.")]
        [SerializeField, Range(0f, 1f)] private float frameRevealStart = 0.34f;
        [SerializeField, Range(0f, 1f)] private float frameRevealEnd = 0.82f;
        [SerializeField, Range(0.1f, 1f)] private float entryFrameScale = 0.75f;

        [Header("Dashed Range Guides")]
        [SerializeField] private Color rangeGuideColor =
            new Color(0.92f, 0.98f, 1f, 0.92f);
        [SerializeField, Min(1f)] private float dashLength = 18f;
        [SerializeField, Min(0f)] private float dashGap = 11f;
        [SerializeField, Min(0.5f)] private float dashWidth = 3f;

        [Header("Photo Range Focus")]
        [Tooltip("Shader used to darken everything outside the active photo range.")]
        [SerializeField] private Shader rangeDimShader;

        [Tooltip("Dark overlay applied outside the photo range at full reveal.")]
        [SerializeField] private Color rangeOutsideDimColor =
            new Color(0f, 0f, 0f, 0.82f);

        [Tooltip("Softness of the transition at the two range lines and far edge, in screen pixels.")]
        [SerializeField, Min(0f)] private float rangeDimEdgeSoftnessPixels = 2f;

        [Tooltip("Undimmed radius retained around the player so the robot and camera form stay readable.")]
        [SerializeField, Min(0f)]
        private float rangeDimPlayerProtectionRadiusPixels = 48f;

        [Header("Focus Presentation")]
        [Tooltip("Shader used to darken the screen outside the camera frame while focusing.")]
        [SerializeField] private Shader focusDimShader;

        [Tooltip("Brightness retained outside the camera frame when focus completes.")]
        [SerializeField, Range(0f, 1f)]
        private float focusOutsideBrightness = 0.3f;

        [Tooltip("Softness of the focus cutout edge, in screen pixels.")]
        [SerializeField, Min(0f)]
        private float focusDimEdgeSoftnessPixels = 3f;

        [Tooltip("Largest camera-frame scale reached during the focus pulse.")]
        [SerializeField, Min(1f)] private float focusFramePeakScale = 1.2f;

        [Tooltip("Scale at which the two aim marks reappear during focus.")]
        [SerializeField, Min(1f)] private float focusAimStartScale = 3.5f;

        [Tooltip("Normalized focus time at which the original aim marks have disappeared.")]
        [SerializeField, Range(0.01f, 0.4f)]
        private float focusAimFadeOutEnd = 0.08f;

        [Tooltip("Normalized focus time at which the enlarged aim marks finish reappearing.")]
        [SerializeField, Range(0.05f, 0.6f)]
        private float focusAimFadeInEnd = 0.24f;

        [Tooltip("Seconds used to smoothly restore frame and aim artwork after focus cancellation.")]
        [SerializeField, Min(0.01f)]
        private float focusCancelVisualBlendDuration = 0.18f;

        [Header("Shutter Flash")]
        [SerializeField] private Color shutterFlashColor = Color.white;

        private PhotoModeController controller;
        private Camera mapCamera;
        private Canvas rootCanvas;
        private Canvas playerRangeCanvas;
        private RectTransform playerRangeCanvasRoot;
        private Canvas shutterFlashCanvas;
        private RectTransform shutterFlashCanvasRoot;
        private RectTransform visualRoot;
        private RectTransform frameRoot;
        private RectTransform aimRoot;
        private PhotoRangeDimGraphic rangeDim;
        private PhotoFocusDimGraphic focusDim;
        private PhotoRangeGuideGraphic rangeGuide;
        private readonly Vector3[] captureFrameWorldCorners = new Vector3[4];
        private readonly Image[] frameStrokeImages = new Image[12];
        private Image frameImage;
        private Image bigAimImage;
        private Image smallAimImage;
        private Image shutterFlashImage;
        private float displayedFocusFrameScale = 1f;
        private float displayedFocusFrameScaleVelocity;
        private float displayedFocusAimScale = 1f;
        private float displayedFocusAimScaleVelocity;
        private float displayedFocusAimAlpha = 1f;
        private float displayedFocusAimAlphaVelocity;

        private void Awake()
        {
            EnsureVisuals();
        }

        public void Initialize(
            PhotoModeController photoModeController,
            Camera camera)
        {
            controller = photoModeController;
            mapCamera = camera;
            EnsureVisuals();
            if (rangeGuide != null)
            {
                rangeGuide.Configure(
                    controller,
                    mapCamera,
                    dashLength,
                    dashGap,
                    dashWidth);
                rangeGuide.color = rangeGuideColor;
            }

            if (rangeDim != null)
            {
                rangeDim.Configure(
                    controller,
                    mapCamera,
                    rangeDimShader,
                    rangeDimEdgeSoftnessPixels,
                    rangeDimPlayerProtectionRadiusPixels);
                rangeDim.color = rangeOutsideDimColor;
            }

            if (focusDim != null)
            {
                focusDim.Configure(
                    rootCanvas,
                    frameRoot,
                    focusDimShader,
                    focusDimEdgeSoftnessPixels);
                focusDim.color = new Color(
                    0f,
                    0f,
                    0f,
                    1f - focusOutsideBrightness);
            }

            SetVisible(
                controller != null
                && controller.IsActive
                && !controller.IsReviewing);
        }

        public bool TryGetCaptureFrameScreenCorners(
            Vector2[] screenCorners,
            float insetNormalizedPerSide = 0f)
        {
            if (screenCorners == null
                || screenCorners.Length < 4
                || frameRoot == null
                || controller == null
                || !controller.IsActive)
            {
                return false;
            }

            frameRoot.GetWorldCorners(captureFrameWorldCorners);
            Camera eventCamera = rootCanvas != null
                                 && rootCanvas.renderMode
                                 == RenderMode.ScreenSpaceOverlay
                ? null
                : mapCamera;
            Vector2 center = Vector2.zero;
            for (int index = 0; index < 4; index++)
            {
                screenCorners[index] =
                    RectTransformUtility.WorldToScreenPoint(
                        eventCamera,
                        captureFrameWorldCorners[index]);
                center += screenCorners[index];
            }

            center *= 0.25f;
            float cornerInset = Mathf.Clamp01(
                insetNormalizedPerSide * 2f);
            for (int index = 0; index < 4; index++)
            {
                screenCorners[index] = Vector2.Lerp(
                    screenCorners[index],
                    center,
                    cornerInset);
            }

            return true;
        }

        private void LateUpdate()
        {
            if (visualRoot == null)
                EnsureVisuals();

            UpdateShutterFlash();

            bool visible = controller != null
                           && mapCamera != null
                           && controller.IsActive
                           && !controller.IsReviewing;
            SetVisible(visible);
            if (!visible || frameRoot == null)
                return;

            Vector3 screenPoint = mapCamera.WorldToScreenPoint(
                controller.AimWorldPosition);
            if (screenPoint.z <= 0f)
            {
                SetVisible(false);
                return;
            }

            Camera eventCamera = rootCanvas != null
                                 && rootCanvas.renderMode
                                 == RenderMode.ScreenSpaceOverlay
                ? null
                : mapCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    visualRoot,
                    screenPoint,
                    eventCamera,
                    out Vector2 localPoint))
            {
                frameRoot.anchoredPosition = SnapToCanvasPixels(localPoint);
            }

            float preferredFrameScale = Mathf.Lerp(
                nearestFrameScale,
                farthestFrameScale,
                controller.Zoom01);
            float reveal = controller.Reveal01;
            float frameReveal = SmoothReveal(
                reveal,
                frameRevealStart,
                frameRevealEnd);
            float revealScale = CalculateRevealScale(frameReveal);
            float maximumRangeScale = CalculateMaximumRangeFrameScale();
            float rangeScaleMultiplier = 1f;
            if (maximumRangeScale < float.PositiveInfinity)
            {
                float largestPreferredScale = Mathf.Max(
                    nearestFrameScale,
                    farthestFrameScale);
                rangeScaleMultiplier = Mathf.Min(
                    1f,
                    maximumRangeScale
                    / Mathf.Max(0.0001f, largestPreferredScale));
            }

            float displayedFrameScale = preferredFrameScale
                                        * rangeScaleMultiplier
                                        * revealScale;
            if (maximumRangeScale < float.PositiveInfinity)
            {
                displayedFrameScale = Mathf.Min(
                    displayedFrameScale,
                    maximumRangeScale);
            }

            UpdateFocusArtworkPresentation();
            displayedFrameScale *= displayedFocusFrameScale;

            frameRoot.localScale = new Vector3(
                displayedFrameScale,
                displayedFrameScale,
                1f);
            frameRoot.localRotation = Quaternion.Euler(
                0f,
                0f,
                CalculateFrameTiltDegrees());
            if (aimRoot != null)
            {
                aimRoot.localScale = new Vector3(
                    displayedFocusAimScale,
                    displayedFocusAimScale,
                    1f);
            }

            UpdateFrameStroke(displayedFrameScale, frameReveal);
            SetArtworkAlpha(frameImage, frameReveal);
            SetArtworkAlpha(
                bigAimImage,
                SmoothReveal(reveal, 0.5f, 0.9f)
                * displayedFocusAimAlpha);
            SetArtworkAlpha(
                smallAimImage,
                SmoothReveal(reveal, 0.62f, 1f)
                * displayedFocusAimAlpha);
            float guideReveal = SmoothReveal(
                reveal,
                guideRevealStart,
                guideRevealEnd);
            float focusPresentation = controller.FocusPresentation01;
            rangeDim?.UpdatePresentation(
                guideReveal * (1f - focusPresentation));
            focusDim?.UpdatePresentation(focusPresentation);
            rangeGuide?.UpdatePlayerScreenAnchor();
            rangeGuide?.SetReveal(guideReveal);
        }

        private void UpdateFocusArtworkPresentation()
        {
            if (controller == null)
                return;

            if (controller.IsFocusing)
            {
                float progress = controller.FocusProgress01;
                displayedFocusFrameScale = 1f
                    + (focusFramePeakScale - 1f)
                    * Mathf.Sin(progress * Mathf.PI);
                displayedFocusFrameScaleVelocity = 0f;

                if (progress <= focusAimFadeOutEnd)
                {
                    float disappear = Mathf.SmoothStep(
                        0f,
                        1f,
                        progress / Mathf.Max(0.01f, focusAimFadeOutEnd));
                    displayedFocusAimAlpha = 1f - disappear;
                    displayedFocusAimScale = Mathf.Lerp(
                        1f,
                        focusAimStartScale,
                        disappear);
                }
                else
                {
                    float appear = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            focusAimFadeOutEnd,
                            focusAimFadeInEnd,
                            progress));
                    float shrink = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            focusAimFadeOutEnd,
                            1f,
                            progress));
                    displayedFocusAimAlpha = appear;
                    displayedFocusAimScale = Mathf.Lerp(
                        focusAimStartScale,
                        1f,
                        shrink);
                }

                displayedFocusAimAlphaVelocity = 0f;
                displayedFocusAimScaleVelocity = 0f;
                return;
            }

            if (controller.IsFocusComplete)
            {
                displayedFocusFrameScale = 1f;
                displayedFocusAimScale = 1f;
                displayedFocusAimAlpha = 1f;
                displayedFocusFrameScaleVelocity = 0f;
                displayedFocusAimScaleVelocity = 0f;
                displayedFocusAimAlphaVelocity = 0f;
                return;
            }

            float blendDuration = Mathf.Max(
                0.01f,
                focusCancelVisualBlendDuration);
            float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
            displayedFocusFrameScale = Mathf.SmoothDamp(
                displayedFocusFrameScale,
                1f,
                ref displayedFocusFrameScaleVelocity,
                blendDuration,
                Mathf.Infinity,
                deltaTime);
            displayedFocusAimScale = Mathf.SmoothDamp(
                displayedFocusAimScale,
                1f,
                ref displayedFocusAimScaleVelocity,
                blendDuration,
                Mathf.Infinity,
                deltaTime);
            displayedFocusAimAlpha = Mathf.SmoothDamp(
                displayedFocusAimAlpha,
                1f,
                ref displayedFocusAimAlphaVelocity,
                blendDuration,
                Mathf.Infinity,
                deltaTime);
        }

        private void UpdateShutterFlash()
        {
            if (shutterFlashImage == null)
                return;

            float flash = controller != null
                ? controller.ShutterFlash01
                : 0f;
            Color displayedColor = shutterFlashColor;
            displayedColor.a *= Mathf.Clamp01(flash);
            shutterFlashImage.color = displayedColor;
        }

        private float CalculateRevealScale(float frameReveal)
        {
            if (frameReveal < 0.78f)
            {
                return Mathf.Lerp(
                    entryFrameScale,
                    1.04f,
                    Mathf.SmoothStep(0f, 1f, frameReveal / 0.78f));
            }

            float settle = Mathf.InverseLerp(0.78f, 1f, frameReveal);
            return Mathf.Lerp(
                1.04f,
                1f,
                Mathf.SmoothStep(0f, 1f, settle));
        }

        private float CalculateMaximumRangeFrameScale()
        {
            if (controller == null
                || mapCamera == null
                || frameReferenceSize.x <= 0f)
            {
                return float.PositiveInfinity;
            }

            controller.GetGuideWorldPoints(
                out Vector3 leftStart,
                out Vector3 leftEnd,
                out Vector3 rightStart,
                out Vector3 rightEnd);
            Vector3 leftScreen = mapCamera.WorldToScreenPoint(leftEnd);
            Vector3 rightScreen = mapCamera.WorldToScreenPoint(rightEnd);
            if (leftScreen.z <= 0f || rightScreen.z <= 0f)
                return float.PositiveInfinity;

            float availableWidthPixels = Vector2.Distance(
                new Vector2(leftScreen.x, leftScreen.y),
                new Vector2(rightScreen.x, rightScreen.y));
            float canvasScaleFactor = rootCanvas != null
                ? Mathf.Max(0.0001f, rootCanvas.scaleFactor)
                : 1f;
            float availableWidthInCanvas = availableWidthPixels
                                           / canvasScaleFactor;
            return availableWidthInCanvas
                   * maximumFrameWidthOfRange
                   / frameReferenceSize.x;
        }

        private void UpdateFrameStroke(
            float displayedFrameScale,
            float frameReveal)
        {
            float canvasScaleFactor = rootCanvas != null
                ? Mathf.Max(0.0001f, rootCanvas.scaleFactor)
                : 1f;
            float localOffset = frameLineWidthIncreasePixels
                                / Mathf.Max(
                                    0.0001f,
                                    displayedFrameScale
                                    * canvasScaleFactor);
            float strokeReveal = frameReveal * frameReveal;
            for (int i = 0; i < frameStrokeImages.Length; i++)
            {
                Image strokeImage = frameStrokeImages[i];
                if (strokeImage == null)
                    continue;

                strokeImage.rectTransform.anchoredPosition =
                    GetFrameStrokeDirection(i) * localOffset;
                SetArtworkAlpha(strokeImage, strokeReveal);
            }
        }

        private Vector2 SnapToCanvasPixels(Vector2 canvasPosition)
        {
            float canvasScaleFactor = rootCanvas != null
                ? Mathf.Max(0.0001f, rootCanvas.scaleFactor)
                : 1f;
            return new Vector2(
                Mathf.Round(canvasPosition.x * canvasScaleFactor)
                / canvasScaleFactor,
                Mathf.Round(canvasPosition.y * canvasScaleFactor)
                / canvasScaleFactor);
        }

        private static Vector2 GetFrameStrokeDirection(int index)
        {
            const float Diagonal = 0.70710678f;
            switch (index)
            {
                case 0: return Vector2.left;
                case 1: return Vector2.right;
                case 2: return Vector2.down;
                case 3: return Vector2.up;
                case 4: return new Vector2(-Diagonal, -Diagonal);
                case 5: return new Vector2(-Diagonal, Diagonal);
                case 6: return new Vector2(Diagonal, -Diagonal);
                case 7: return new Vector2(Diagonal, Diagonal);
                case 8: return Vector2.left * 0.5f;
                case 9: return Vector2.right * 0.5f;
                case 10: return Vector2.down * 0.5f;
                default: return Vector2.up * 0.5f;
            }
        }

        private static float SmoothReveal(
            float progress,
            float start,
            float end)
        {
            float normalized = Mathf.InverseLerp(start, end, progress);
            return Mathf.SmoothStep(0f, 1f, normalized);
        }

        private void SetArtworkAlpha(Image image, float alpha)
        {
            if (image == null)
                return;

            Color displayedColor = cameraFrameColor;
            displayedColor.a *= Mathf.Clamp01(alpha);
            image.color = displayedColor;
        }

        private float CalculateFrameTiltDegrees()
        {
            float lateralAim = controller != null
                ? controller.NormalizedLateralAim
                : 0f;
            if (Mathf.Abs(lateralAim) <= 0.0001f || mapCamera == null)
                return 0f;

            controller.GetGuideWorldPoints(
                out Vector3 leftStart,
                out Vector3 leftEnd,
                out Vector3 rightStart,
                out Vector3 rightEnd);
            Vector3 guideStartWorld = lateralAim < 0f
                ? leftStart
                : rightStart;
            Vector3 guideEndWorld = lateralAim < 0f
                ? leftEnd
                : rightEnd;
            Vector3 guideStartScreen = mapCamera.WorldToScreenPoint(
                guideStartWorld);
            Vector3 guideEndScreen = mapCamera.WorldToScreenPoint(
                guideEndWorld);
            Vector2 guideDirection = new Vector2(
                guideEndScreen.x - guideStartScreen.x,
                guideEndScreen.y - guideStartScreen.y);
            if (guideDirection.sqrMagnitude <= 0.0001f)
                return 0f;

            float edgeTilt = Vector2.SignedAngle(
                Vector2.up,
                guideDirection);
            return edgeTilt * Mathf.Abs(lateralAim);
        }

        private void EnsureVisuals()
        {
            if (visualRoot != null)
                return;

            rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas == null)
            {
                Debug.LogError(
                    "PhotoModeUI must be placed below a Canvas.",
                    this);
                enabled = false;
                return;
            }

            GameObject visualRootObject = CreateUiObject(
                "Photo Mode Root",
                transform,
                false);
            visualRoot = visualRootObject.GetComponent<RectTransform>();
            StretchToParent(visualRoot);

            CreatePlayerRangeCanvas();
            CreateShutterFlashCanvas();
            GameObject dimObject = CreateUiObject(
                "Photo Range Outside Dim",
                playerRangeCanvasRoot,
                true);
            RectTransform dimTransform =
                dimObject.GetComponent<RectTransform>();
            StretchToParent(dimTransform);
            rangeDim = dimObject.AddComponent<PhotoRangeDimGraphic>();
            rangeDim.raycastTarget = false;
            rangeDim.color = rangeOutsideDimColor;

            GameObject focusDimObject = CreateUiObject(
                "Photo Focus Outside Dim",
                playerRangeCanvasRoot,
                true);
            RectTransform focusDimTransform =
                focusDimObject.GetComponent<RectTransform>();
            StretchToParent(focusDimTransform);
            focusDim = focusDimObject.AddComponent<PhotoFocusDimGraphic>();
            focusDim.raycastTarget = false;
            focusDim.color = new Color(
                0f,
                0f,
                0f,
                1f - focusOutsideBrightness);

            GameObject rangeObject = CreateUiObject(
                "Photo Range Dashed Guides",
                playerRangeCanvasRoot,
                true);
            RectTransform rangeTransform =
                rangeObject.GetComponent<RectTransform>();
            rangeTransform.anchorMin = Vector2.zero;
            rangeTransform.anchorMax = Vector2.zero;
            rangeTransform.pivot = Vector2.one * 0.5f;
            rangeTransform.anchoredPosition = Vector2.zero;
            rangeTransform.sizeDelta = Vector2.one;
            rangeGuide = rangeObject.AddComponent<PhotoRangeGuideGraphic>();
            rangeGuide.raycastTarget = false;
            rangeGuide.color = rangeGuideColor;

            GameObject frameRootObject = CreateUiObject(
                "Camera Frame Root",
                visualRoot,
                false);
            frameRoot = frameRootObject.GetComponent<RectTransform>();
            frameRoot.anchorMin = Vector2.one * 0.5f;
            frameRoot.anchorMax = Vector2.one * 0.5f;
            frameRoot.pivot = Vector2.one * 0.5f;
            frameRoot.anchoredPosition = Vector2.zero;
            frameRoot.sizeDelta = frameReferenceSize;
            frameRoot.localScale = Vector3.one;

            for (int i = 0; i < frameStrokeImages.Length; i++)
            {
                frameStrokeImages[i] = CreateArtworkImage(
                    "camera_frame_stroke_" + i,
                    frameRoot,
                    cameraFrameSprite,
                    cameraFrameColor);
            }

            frameImage = CreateArtworkImage(
                "camera_frame",
                frameRoot,
                cameraFrameSprite,
                cameraFrameColor);

            GameObject aimRootObject = CreateUiObject(
                "Camera Aim Root",
                frameRoot,
                false);
            aimRoot = aimRootObject.GetComponent<RectTransform>();
            StretchToParent(aimRoot);
            bigAimImage = CreateArtworkImage(
                "camera_aim_big",
                aimRoot,
                cameraAimBigSprite,
                cameraFrameColor);
            smallAimImage = CreateArtworkImage(
                "camera_aim_small",
                aimRoot,
                cameraAimSmallSprite,
                cameraFrameColor);

            if (cameraFrameSprite == null
                || cameraAimSmallSprite == null
                || cameraAimBigSprite == null)
            {
                Debug.LogError(
                    "PhotoModeUI is missing one or more authored camera sprites.",
                    this);
            }

            visualRoot.gameObject.SetActive(false);
            if (playerRangeCanvasRoot != null)
                playerRangeCanvasRoot.gameObject.SetActive(false);
        }

        private void CreatePlayerRangeCanvas()
        {
            if (playerRangeCanvasRoot != null)
                return;

            var canvasObject = new GameObject(
                "Photo Player Range UI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            playerRangeCanvasRoot =
                canvasObject.GetComponent<RectTransform>();
            playerRangeCanvas = canvasObject.GetComponent<Canvas>();
            playerRangeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            playerRangeCanvas.overrideSorting = true;
            playerRangeCanvas.sortingLayerID = rootCanvas.sortingLayerID;
            // Draw the focus overlay above MainUI so its terrain texture and
            // gameplay markers are darkened outside the photo range. The
            // dashed guides are later siblings and remain bright on top.
            playerRangeCanvas.sortingOrder = rootCanvas.sortingOrder + 1;

            CanvasScaler targetScaler = canvasObject.GetComponent<CanvasScaler>();
            targetScaler.uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            targetScaler.scaleFactor = 1f;
            targetScaler.referencePixelsPerUnit = 100f;
        }

        private void CreateShutterFlashCanvas()
        {
            if (shutterFlashCanvasRoot != null)
                return;

            var canvasObject = new GameObject(
                "Photo Shutter Flash UI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            shutterFlashCanvasRoot =
                canvasObject.GetComponent<RectTransform>();
            shutterFlashCanvas = canvasObject.GetComponent<Canvas>();
            shutterFlashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            shutterFlashCanvas.overrideSorting = true;
            shutterFlashCanvas.sortingLayerID = rootCanvas.sortingLayerID;
            shutterFlashCanvas.sortingOrder = rootCanvas.sortingOrder + 2;

            CanvasScaler targetScaler =
                canvasObject.GetComponent<CanvasScaler>();
            targetScaler.uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            targetScaler.scaleFactor = 1f;
            targetScaler.referencePixelsPerUnit = 100f;

            GameObject flashObject = CreateUiObject(
                "Photo Shutter White Flash",
                shutterFlashCanvasRoot,
                true);
            RectTransform flashTransform =
                flashObject.GetComponent<RectTransform>();
            StretchToParent(flashTransform);
            shutterFlashImage = flashObject.AddComponent<Image>();
            shutterFlashImage.raycastTarget = false;
            Color transparentFlash = shutterFlashColor;
            transparentFlash.a = 0f;
            shutterFlashImage.color = transparentFlash;
        }

        private void SetVisible(bool visible)
        {
            if (visualRoot != null
                && visualRoot.gameObject.activeSelf != visible)
            {
                visualRoot.gameObject.SetActive(visible);
                if (visible)
                {
                    rangeDim?.SetVerticesDirty();
                    focusDim?.SetVerticesDirty();
                    rangeGuide?.SetVerticesDirty();
                }
                else
                {
                    ResetFocusArtworkPresentation();
                }
            }

            if (playerRangeCanvasRoot != null
                && playerRangeCanvasRoot.gameObject.activeSelf != visible)
            {
                playerRangeCanvasRoot.gameObject.SetActive(visible);
                if (visible)
                    rangeGuide?.SetVerticesDirty();
            }
        }

        private void ResetFocusArtworkPresentation()
        {
            displayedFocusFrameScale = 1f;
            displayedFocusFrameScaleVelocity = 0f;
            displayedFocusAimScale = 1f;
            displayedFocusAimScaleVelocity = 0f;
            displayedFocusAimAlpha = 1f;
            displayedFocusAimAlphaVelocity = 0f;
        }

        private static GameObject CreateUiObject(
            string objectName,
            Transform parent,
            bool addCanvasRenderer)
        {
            GameObject result = addCanvasRenderer
                ? new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer))
                : new GameObject(objectName, typeof(RectTransform));
            result.layer = parent.gameObject.layer;
            result.transform.SetParent(parent, false);
            return result;
        }

        private static Image CreateArtworkImage(
            string objectName,
            Transform parent,
            Sprite sprite,
            Color color)
        {
            var imageObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            imageObject.layer = parent.gameObject.layer;
            RectTransform imageTransform =
                imageObject.GetComponent<RectTransform>();
            imageTransform.SetParent(parent, false);
            StretchToParent(imageTransform);

            Image image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = Vector2.one * 0.5f;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.localScale = Vector3.one;
        }

        private void OnValidate()
        {
            frameReferenceSize.x = Mathf.Max(1f, frameReferenceSize.x);
            frameReferenceSize.y = Mathf.Max(1f, frameReferenceSize.y);
            nearestFrameScale = Mathf.Max(0.01f, nearestFrameScale);
            farthestFrameScale = Mathf.Max(0.01f, farthestFrameScale);
            maximumFrameWidthOfRange = Mathf.Clamp(
                maximumFrameWidthOfRange,
                0.1f,
                1f);
            frameLineWidthIncreasePixels = Mathf.Max(
                0f,
                frameLineWidthIncreasePixels);
            dashLength = Mathf.Max(1f, dashLength);
            dashGap = Mathf.Max(0f, dashGap);
            dashWidth = Mathf.Max(0.5f, dashWidth);
            rangeDimEdgeSoftnessPixels = Mathf.Max(
                0f,
                rangeDimEdgeSoftnessPixels);
            rangeDimPlayerProtectionRadiusPixels = Mathf.Max(
                0f,
                rangeDimPlayerProtectionRadiusPixels);
            focusOutsideBrightness = Mathf.Clamp01(
                focusOutsideBrightness);
            focusDimEdgeSoftnessPixels = Mathf.Max(
                0f,
                focusDimEdgeSoftnessPixels);
            focusFramePeakScale = Mathf.Max(1f, focusFramePeakScale);
            focusAimStartScale = Mathf.Max(1f, focusAimStartScale);
            focusAimFadeOutEnd = Mathf.Clamp(
                focusAimFadeOutEnd,
                0.01f,
                0.4f);
            focusAimFadeInEnd = Mathf.Clamp(
                focusAimFadeInEnd,
                focusAimFadeOutEnd + 0.01f,
                0.6f);
            focusCancelVisualBlendDuration = Mathf.Max(
                0.01f,
                focusCancelVisualBlendDuration);
            guideRevealStart = Mathf.Clamp(guideRevealStart, 0f, 0.99f);
            guideRevealEnd = Mathf.Clamp(
                guideRevealEnd,
                guideRevealStart + 0.01f,
                1f);
            frameRevealStart = Mathf.Clamp(frameRevealStart, 0f, 0.99f);
            frameRevealEnd = Mathf.Clamp(
                frameRevealEnd,
                frameRevealStart + 0.01f,
                1f);
            entryFrameScale = Mathf.Clamp(entryFrameScale, 0.1f, 1f);
        }

        private void OnDestroy()
        {
            if (playerRangeCanvasRoot != null)
                Destroy(playerRangeCanvasRoot.gameObject);
            if (shutterFlashCanvasRoot != null)
                Destroy(shutterFlashCanvasRoot.gameObject);
        }

    }

    public sealed class PhotoFocusDimGraphic : MaskableGraphic
    {
        private static readonly int CornerAId =
            Shader.PropertyToID("_CornerA");
        private static readonly int CornerBId =
            Shader.PropertyToID("_CornerB");
        private static readonly int CornerCId =
            Shader.PropertyToID("_CornerC");
        private static readonly int CornerDId =
            Shader.PropertyToID("_CornerD");
        private static readonly int RevealId =
            Shader.PropertyToID("_Reveal");
        private static readonly int EdgeSoftnessId =
            Shader.PropertyToID("_EdgeSoftnessPixels");

        private readonly Vector3[] frameWorldCorners = new Vector3[4];
        private Canvas sourceCanvas;
        private RectTransform frameRoot;
        private Material runtimeMaterial;
        private float edgeSoftnessPixels = 3f;

        public void Configure(
            Canvas canvas,
            RectTransform focusFrame,
            Shader dimShader,
            float configuredEdgeSoftnessPixels)
        {
            sourceCanvas = canvas;
            frameRoot = focusFrame;
            edgeSoftnessPixels = Mathf.Max(
                0f,
                configuredEdgeSoftnessPixels);

            Shader activeShader = dimShader != null
                ? dimShader
                : Shader.Find("UI/Photo Focus Dim");
            if (activeShader == null)
            {
                Debug.LogError(
                    "PhotoFocusDimGraphic could not find the "
                    + "UI/Photo Focus Dim shader.",
                    this);
                return;
            }

            if (runtimeMaterial == null
                || runtimeMaterial.shader != activeShader)
            {
                DestroyRuntimeMaterial();
                runtimeMaterial = new Material(activeShader)
                {
                    name = "Runtime Photo Focus Dim Material",
                    hideFlags = HideFlags.DontSave
                };
                material = runtimeMaterial;
            }

            runtimeMaterial.SetFloat(EdgeSoftnessId, edgeSoftnessPixels);
            runtimeMaterial.SetFloat(RevealId, 0f);
            SetAllDirty();
        }

        public void UpdatePresentation(float reveal)
        {
            if (runtimeMaterial == null)
                return;

            float clampedReveal = Mathf.Clamp01(reveal);
            runtimeMaterial.SetFloat(RevealId, clampedReveal);
            if (frameRoot == null || clampedReveal <= 0f)
                return;

            frameRoot.GetWorldCorners(frameWorldCorners);
            Camera eventCamera = null;
            if (sourceCanvas != null
                && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                eventCamera = sourceCanvas.worldCamera;
            }

            float inverseWidth = 1f / Mathf.Max(1f, Screen.width);
            float inverseHeight = 1f / Mathf.Max(1f, Screen.height);
            Vector2 bottomLeft = NormalizeScreenPoint(
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    frameWorldCorners[0]),
                inverseWidth,
                inverseHeight);
            Vector2 topLeft = NormalizeScreenPoint(
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    frameWorldCorners[1]),
                inverseWidth,
                inverseHeight);
            Vector2 topRight = NormalizeScreenPoint(
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    frameWorldCorners[2]),
                inverseWidth,
                inverseHeight);
            Vector2 bottomRight = NormalizeScreenPoint(
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    frameWorldCorners[3]),
                inverseWidth,
                inverseHeight);

            runtimeMaterial.SetVector(
                CornerAId,
                new Vector4(bottomLeft.x, bottomLeft.y, 0f, 0f));
            runtimeMaterial.SetVector(
                CornerBId,
                new Vector4(topLeft.x, topLeft.y, 0f, 0f));
            runtimeMaterial.SetVector(
                CornerCId,
                new Vector4(topRight.x, topRight.y, 0f, 0f));
            runtimeMaterial.SetVector(
                CornerDId,
                new Vector4(bottomRight.x, bottomRight.y, 0f, 0f));
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            var quad = new UIVertex[4];

            vertex.position = new Vector2(rect.xMin, rect.yMin);
            vertex.uv0 = new Vector2(0f, 0f);
            quad[0] = vertex;
            vertex.position = new Vector2(rect.xMin, rect.yMax);
            vertex.uv0 = new Vector2(0f, 1f);
            quad[1] = vertex;
            vertex.position = new Vector2(rect.xMax, rect.yMax);
            vertex.uv0 = new Vector2(1f, 1f);
            quad[2] = vertex;
            vertex.position = new Vector2(rect.xMax, rect.yMin);
            vertex.uv0 = new Vector2(1f, 0f);
            quad[3] = vertex;
            vertexHelper.AddUIVertexQuad(quad);
        }

        protected override void OnDestroy()
        {
            DestroyRuntimeMaterial();
            base.OnDestroy();
        }

        private static Vector2 NormalizeScreenPoint(
            Vector2 screenPoint,
            float inverseWidth,
            float inverseHeight)
        {
            return new Vector2(
                screenPoint.x * inverseWidth,
                screenPoint.y * inverseHeight);
        }

        private void DestroyRuntimeMaterial()
        {
            if (runtimeMaterial == null)
                return;

            material = null;
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    public sealed class PhotoRangeDimGraphic : MaskableGraphic
    {
        private static readonly int TriangleAId =
            Shader.PropertyToID("_TriangleA");
        private static readonly int TriangleBId =
            Shader.PropertyToID("_TriangleB");
        private static readonly int TriangleCId =
            Shader.PropertyToID("_TriangleC");
        private static readonly int PlayerCenterId =
            Shader.PropertyToID("_PlayerCenter");
        private static readonly int RevealId =
            Shader.PropertyToID("_Reveal");
        private static readonly int EdgeSoftnessId =
            Shader.PropertyToID("_EdgeSoftnessPixels");
        private static readonly int PlayerRadiusId =
            Shader.PropertyToID("_PlayerRadiusPixels");

        private PhotoModeController controller;
        private Camera mapCamera;
        private Material runtimeMaterial;
        private float edgeSoftnessPixels = 2f;
        private float playerProtectionRadiusPixels = 48f;

        public void Configure(
            PhotoModeController photoModeController,
            Camera camera,
            Shader dimShader,
            float configuredEdgeSoftnessPixels,
            float configuredPlayerProtectionRadiusPixels)
        {
            controller = photoModeController;
            mapCamera = camera;
            edgeSoftnessPixels = Mathf.Max(
                0f,
                configuredEdgeSoftnessPixels);
            playerProtectionRadiusPixels = Mathf.Max(
                0f,
                configuredPlayerProtectionRadiusPixels);

            Shader activeShader = dimShader != null
                ? dimShader
                : Shader.Find("UI/Photo Range Dim");
            if (activeShader == null)
            {
                Debug.LogError(
                    "PhotoRangeDimGraphic could not find the "
                    + "UI/Photo Range Dim shader.",
                    this);
                return;
            }

            if (runtimeMaterial == null
                || runtimeMaterial.shader != activeShader)
            {
                DestroyRuntimeMaterial();
                runtimeMaterial = new Material(activeShader)
                {
                    name = "Runtime Photo Range Dim Material",
                    hideFlags = HideFlags.DontSave
                };
                material = runtimeMaterial;
            }

            runtimeMaterial.SetFloat(EdgeSoftnessId, edgeSoftnessPixels);
            runtimeMaterial.SetFloat(
                PlayerRadiusId,
                playerProtectionRadiusPixels);
            SetAllDirty();
        }

        public void UpdatePresentation(float reveal)
        {
            if (runtimeMaterial == null)
                return;

            float clampedReveal = Mathf.Clamp01(reveal);
            runtimeMaterial.SetFloat(RevealId, clampedReveal);
            if (controller == null
                || mapCamera == null
                || clampedReveal <= 0f)
            {
                return;
            }

            controller.GetGuideWorldPoints(
                out Vector3 leftStart,
                out Vector3 leftEnd,
                out Vector3 rightStart,
                out Vector3 rightEnd);
            Vector3 apexWorld = (leftStart + rightStart) * 0.5f;
            leftEnd = Vector3.Lerp(leftStart, leftEnd, clampedReveal);
            rightEnd = Vector3.Lerp(rightStart, rightEnd, clampedReveal);

            Vector3 apexViewport = mapCamera.WorldToViewportPoint(apexWorld);
            Vector3 leftViewport = mapCamera.WorldToViewportPoint(leftEnd);
            Vector3 rightViewport = mapCamera.WorldToViewportPoint(rightEnd);
            Vector3 playerViewport = mapCamera.WorldToViewportPoint(
                controller.transform.position);
            if (apexViewport.z <= 0f
                || leftViewport.z <= 0f
                || rightViewport.z <= 0f
                || playerViewport.z <= 0f)
            {
                runtimeMaterial.SetFloat(RevealId, 0f);
                return;
            }

            runtimeMaterial.SetVector(
                TriangleAId,
                new Vector4(apexViewport.x, apexViewport.y, 0f, 0f));
            runtimeMaterial.SetVector(
                TriangleBId,
                new Vector4(leftViewport.x, leftViewport.y, 0f, 0f));
            runtimeMaterial.SetVector(
                TriangleCId,
                new Vector4(rightViewport.x, rightViewport.y, 0f, 0f));
            runtimeMaterial.SetVector(
                PlayerCenterId,
                new Vector4(playerViewport.x, playerViewport.y, 0f, 0f));
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            var quad = new UIVertex[4];

            vertex.position = new Vector2(rect.xMin, rect.yMin);
            vertex.uv0 = new Vector2(0f, 0f);
            quad[0] = vertex;
            vertex.position = new Vector2(rect.xMin, rect.yMax);
            vertex.uv0 = new Vector2(0f, 1f);
            quad[1] = vertex;
            vertex.position = new Vector2(rect.xMax, rect.yMax);
            vertex.uv0 = new Vector2(1f, 1f);
            quad[2] = vertex;
            vertex.position = new Vector2(rect.xMax, rect.yMin);
            vertex.uv0 = new Vector2(1f, 0f);
            quad[3] = vertex;
            vertexHelper.AddUIVertexQuad(quad);
        }

        protected override void OnDestroy()
        {
            DestroyRuntimeMaterial();
            base.OnDestroy();
        }

        private void DestroyRuntimeMaterial()
        {
            if (runtimeMaterial == null)
                return;

            material = null;
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    public sealed class PhotoRangeGuideGraphic : MaskableGraphic
    {
        private PhotoModeController controller;
        private Camera mapCamera;
        private float dashLength = 18f;
        private float dashGap = 11f;
        private float dashWidth = 3f;
        private float reveal01 = 1f;

        public void Configure(
            PhotoModeController photoModeController,
            Camera camera,
            float configuredDashLength,
            float configuredDashGap,
            float configuredDashWidth)
        {
            controller = photoModeController;
            mapCamera = camera;
            dashLength = Mathf.Max(1f, configuredDashLength);
            dashGap = Mathf.Max(0f, configuredDashGap);
            dashWidth = Mathf.Max(0.5f, configuredDashWidth);
            SetVerticesDirty();
        }

        public void UpdatePlayerScreenAnchor()
        {
            if (controller == null || mapCamera == null)
                return;

            Vector3 playerScreen = mapCamera.WorldToScreenPoint(
                controller.transform.position);
            if (playerScreen.z <= 0f)
                return;

            Vector2 screenAnchor = new Vector2(
                playerScreen.x,
                playerScreen.y);
            rectTransform.anchoredPosition = screenAnchor;
            rectTransform.sizeDelta = new Vector2(
                Mathf.Max(1f, Screen.width * 2f),
                Mathf.Max(1f, Screen.height * 2f));
            // The anchor follows the same projected player point as the gravity
            // UI. Rebuild every frame so player rotation updates the local rays
            // without ever inheriting movement from the camera-frame UI.
            SetVerticesDirty();
        }

        public void SetReveal(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            if (Mathf.Approximately(reveal01, clamped))
                return;

            reveal01 = clamped;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (controller == null
                || mapCamera == null
                || !controller.IsActive
                || reveal01 <= 0f)
            {
                return;
            }

            controller.GetGuideWorldPoints(
                out Vector3 leftStartWorld,
                out Vector3 leftEndWorld,
                out Vector3 rightStartWorld,
                out Vector3 rightEndWorld);
            if (TryWorldToLocal(leftStartWorld, out Vector2 leftStart)
                && TryWorldToLocal(leftEndWorld, out Vector2 leftEnd))
            {
                AddDashedLine(
                    vertexHelper,
                    leftStart,
                    Vector2.Lerp(leftStart, leftEnd, reveal01));
            }

            if (TryWorldToLocal(rightStartWorld, out Vector2 rightStart)
                && TryWorldToLocal(rightEndWorld, out Vector2 rightEnd))
            {
                AddDashedLine(
                    vertexHelper,
                    rightStart,
                    Vector2.Lerp(rightStart, rightEnd, reveal01));
            }
        }

        private bool TryWorldToLocal(Vector3 worldPoint, out Vector2 localPoint)
        {
            Vector3 screenPoint = mapCamera.WorldToScreenPoint(worldPoint);
            if (screenPoint.z <= 0f)
            {
                localPoint = Vector2.zero;
                return false;
            }

            localPoint = new Vector2(
                screenPoint.x - rectTransform.anchoredPosition.x,
                screenPoint.y - rectTransform.anchoredPosition.y);
            return true;
        }

        private void AddDashedLine(
            VertexHelper vertexHelper,
            Vector2 start,
            Vector2 end)
        {
            Vector2 difference = end - start;
            float totalLength = difference.magnitude;
            if (totalLength <= 0.001f)
                return;

            Vector2 direction = difference / totalLength;
            Vector2 perpendicular = new Vector2(
                -direction.y,
                direction.x) * (dashWidth * 0.5f);
            float step = Mathf.Max(0.001f, dashLength + dashGap);
            for (float offset = 0f; offset < totalLength; offset += step)
            {
                float segmentEnd = Mathf.Min(
                    totalLength,
                    offset + dashLength);
                Vector2 dashStart = start + direction * offset;
                Vector2 dashEnd = start + direction * segmentEnd;
                AddQuad(
                    vertexHelper,
                    dashStart - perpendicular,
                    dashStart + perpendicular,
                    dashEnd + perpendicular,
                    dashEnd - perpendicular);
            }
        }

        private void AddQuad(
            VertexHelper vertexHelper,
            Vector2 first,
            Vector2 second,
            Vector2 third,
            Vector2 fourth)
        {
            UIVertex vertex = UIVertex.simpleVert;
            Color displayedColor = color;
            displayedColor.a *= reveal01;
            vertex.color = displayedColor;
            var quad = new UIVertex[4];
            vertex.position = first;
            quad[0] = vertex;
            vertex.position = second;
            quad[1] = vertex;
            vertex.position = third;
            quad[2] = vertex;
            vertex.position = fourth;
            quad[3] = vertex;
            vertexHelper.AddUIVertexQuad(quad);
        }
    }
}
