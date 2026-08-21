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
        [SerializeField, Min(0.01f)] private float nearestFrameScale = 0.22f;

        [Tooltip("Camera-frame scale at the farthest photo distance.")]
        [SerializeField, Min(0.01f)] private float farthestFrameScale = 0.16f;

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

        private PhotoModeController controller;
        private Camera mapCamera;
        private Canvas rootCanvas;
        private Canvas playerRangeCanvas;
        private RectTransform playerRangeCanvasRoot;
        private RectTransform visualRoot;
        private RectTransform frameRoot;
        private PhotoRangeGuideGraphic rangeGuide;
        private Image frameImage;
        private Image bigAimImage;
        private Image smallAimImage;

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

            SetVisible(controller != null && controller.IsActive);
        }

        private void LateUpdate()
        {
            if (visualRoot == null)
                EnsureVisuals();

            bool visible = controller != null
                           && mapCamera != null
                           && controller.IsActive;
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
                frameRoot.anchoredPosition = localPoint;
            }

            float frameScale = Mathf.Lerp(
                nearestFrameScale,
                farthestFrameScale,
                controller.Zoom01);
            float reveal = controller.Reveal01;
            float frameReveal = SmoothReveal(
                reveal,
                frameRevealStart,
                frameRevealEnd);
            float revealScale = CalculateRevealScale(frameReveal);
            frameRoot.localScale = new Vector3(
                frameScale * revealScale,
                frameScale * revealScale,
                1f);
            frameRoot.localRotation = Quaternion.Euler(
                0f,
                0f,
                CalculateFrameTiltDegrees());
            SetArtworkAlpha(frameImage, frameReveal);
            SetArtworkAlpha(
                bigAimImage,
                SmoothReveal(reveal, 0.5f, 0.9f));
            SetArtworkAlpha(
                smallAimImage,
                SmoothReveal(reveal, 0.62f, 1f));
            rangeGuide?.UpdatePlayerScreenAnchor();
            rangeGuide?.SetReveal(SmoothReveal(
                reveal,
                guideRevealStart,
                guideRevealEnd));
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

            frameImage = CreateArtworkImage(
                "camera_frame",
                frameRoot,
                cameraFrameSprite,
                cameraFrameColor);
            bigAimImage = CreateArtworkImage(
                "camera_aim_big",
                frameRoot,
                cameraAimBigSprite,
                cameraFrameColor);
            smallAimImage = CreateArtworkImage(
                "camera_aim_small",
                frameRoot,
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
            playerRangeCanvas.sortingOrder = rootCanvas.sortingOrder - 1;

            CanvasScaler targetScaler = canvasObject.GetComponent<CanvasScaler>();
            targetScaler.uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            targetScaler.scaleFactor = 1f;
            targetScaler.referencePixelsPerUnit = 100f;
        }

        private void SetVisible(bool visible)
        {
            if (visualRoot != null
                && visualRoot.gameObject.activeSelf != visible)
            {
                visualRoot.gameObject.SetActive(visible);
                if (visible)
                    rangeGuide?.SetVerticesDirty();
            }

            if (playerRangeCanvasRoot != null
                && playerRangeCanvasRoot.gameObject.activeSelf != visible)
            {
                playerRangeCanvasRoot.gameObject.SetActive(visible);
                if (visible)
                    rangeGuide?.SetVerticesDirty();
            }
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
            dashLength = Mathf.Max(1f, dashLength);
            dashGap = Mathf.Max(0f, dashGap);
            dashWidth = Mathf.Max(0.5f, dashWidth);
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
