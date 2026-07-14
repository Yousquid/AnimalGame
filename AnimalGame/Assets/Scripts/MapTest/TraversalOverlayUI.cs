using System.Collections.Generic;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.MapTest
{
    [DisallowMultipleComponent]
    public sealed class TraversalOverlayUI : MonoBehaviour
    {
        private const KeyCode ToggleKey = KeyCode.Q;

        [Header("Signs")]
        [SerializeField] private Sprite passableSign;
        [SerializeField] private Sprite unpassableSign;

        [Header("Screen-space Grid")]
        [Tooltip("Screen-pixel spacing between neighboring rows and columns.")]
        [SerializeField, Min(4f)] private float gridSpacingPixels = 56f;

        [Tooltip("Screen-pixel radius around the robot marker where signs are hidden.")]
        [SerializeField, Min(0f)] private float centerExclusionRadiusPixels = 36f;

        [Tooltip("Maximum logical map distance from the robot where signs are shown. Set to 0 for no distance limit.")]
        [SerializeField, Min(0f)] private float displayRadiusMeters = 120f;

        [Tooltip("Safety limit for the number of pooled UI signs.")]
        [SerializeField, Range(64, 4096)] private int maximumVisibleSigns = 1600;

        [SerializeField, Range(0f, 0.45f)] private float viewportMargin = 0.02f;

        [Header("Presentation")]
        [SerializeField, Min(1f)] private float iconSize = 7.6f;
        [SerializeField, Min(0.1f)] private float refreshRate = 3f;

        private readonly List<Image> pooledImages = new List<Image>();

        private MapTestSceneController map;
        private HeightMapTraversalEvaluator evaluator;
        private Camera mapCamera;
        private RobotMover playerRobot;
        private GameObject overlayRoot;
        private float nextRefreshTime;
        private int activeColumns = 1;
        private int activeRows = 1;
        private bool isOverlayVisible = true;

        public bool IsOverlayVisible => isOverlayVisible;

        public void Initialize(
            MapTestSceneController mapController,
            HeightMapTraversalEvaluator traversalEvaluator,
            Camera cameraToSample,
            RobotMover robot)
        {
            map = mapController;
            evaluator = traversalEvaluator;
            mapCamera = cameraToSample;
            playerRobot = robot;

            if (map == null || evaluator == null || mapCamera == null || playerRobot == null)
            {
                Debug.LogError(
                    "TraversalOverlayUI requires a map, traversal evaluator, camera, and robot.",
                    this);
                HideAllImages();
                return;
            }

            CreateOverlayIfNeeded();
            UpdateGridDimensions();
            EnsureImagePool();
            overlayRoot.SetActive(isActiveAndEnabled && isOverlayVisible);
            nextRefreshTime = 0f;
            if (isOverlayVisible)
                RefreshOverlay();
        }

        private void OnEnable()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(isOverlayVisible);

            nextRefreshTime = 0f;
        }

        private void OnDisable()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (overlayRoot != null)
                Destroy(overlayRoot);
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey))
                SetOverlayVisible(!isOverlayVisible);

            if (!isOverlayVisible)
                return;

            if (map == null || evaluator == null || mapCamera == null || playerRobot == null)
                return;

            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + 1f / Mathf.Max(0.1f, refreshRate);
            RefreshOverlay();
        }

        public void SetOverlayVisible(bool shouldBeVisible)
        {
            isOverlayVisible = shouldBeVisible;

            if (overlayRoot != null)
                overlayRoot.SetActive(isActiveAndEnabled && isOverlayVisible);

            if (!isOverlayVisible)
            {
                HideAllImages();
                return;
            }

            nextRefreshTime = 0f;
            if (map != null && evaluator != null && mapCamera != null && playerRobot != null)
                RefreshOverlay();
        }

        private void RefreshOverlay()
        {
            if (!map.HasGeneratedMap
                || !map.TrySampleWorldPosition(
                    playerRobot.transform.position,
                    out Vector2 robotMapPosition,
                    out _))
            {
                HideAllImages();
                return;
            }

            CreateOverlayIfNeeded();
            UpdateGridDimensions();
            EnsureImagePool();

            Bounds bounds = map.WorldBounds;
            float mapPlaneDistance = Mathf.Abs(
                bounds.center.z - mapCamera.transform.position.z);
            float availableViewport = Mathf.Max(0f, 1f - viewportMargin * 2f);
            Vector3 robotViewport = mapCamera.WorldToViewportPoint(playerRobot.transform.position);
            Vector2 robotScreenPosition = new Vector2(
                robotViewport.x * Screen.width,
                robotViewport.y * Screen.height);
            float exclusionRadiusSquared =
                centerExclusionRadiusPixels * centerExclusionRadiusPixels;
            int imageIndex = 0;

            for (int row = 0; row < activeRows; row++)
            {
                float rowProgress = (row + 0.5f) / activeRows;
                float viewportY = viewportMargin + availableViewport * rowProgress;

                for (int column = 0; column < activeColumns; column++)
                {
                    float columnProgress = (column + 0.5f) / activeColumns;
                    float viewportX = viewportMargin + availableViewport * columnProgress;
                    Vector2 screenPosition = new Vector2(
                        viewportX * Screen.width,
                        viewportY * Screen.height);
                    if ((screenPosition - robotScreenPosition).sqrMagnitude
                        < exclusionRadiusSquared)
                    {
                        continue;
                    }

                    Vector3 worldPosition = mapCamera.ViewportToWorldPoint(
                        new Vector3(viewportX, viewportY, mapPlaneDistance));
                    if (!map.TrySampleWorldPosition(
                            worldPosition,
                            out Vector2 targetMapPosition,
                            out _))
                    {
                        continue;
                    }

                    if (displayRadiusMeters > 0f
                        && Vector2.Distance(robotMapPosition, targetMapPosition)
                        > displayRadiusMeters)
                    {
                        continue;
                    }

                    SlopeTraversalResult result = evaluator.EvaluateMapPath(
                        robotMapPosition,
                        targetMapPosition);
                    if (!result.HasData)
                        continue;

                    if (imageIndex >= pooledImages.Count)
                        goto Finish;

                    Image image = pooledImages[imageIndex++];
                    PositionImage(image, new Vector2(viewportX, viewportY));

                    Sprite desiredSprite = result.IsPassable ? passableSign : unpassableSign;
                    image.sprite = desiredSprite;
                    image.color = Color.white;
                    SetImageEnabled(image, desiredSprite != null);
                }
            }

        Finish:
            for (; imageIndex < pooledImages.Count; imageIndex++)
                SetImageEnabled(pooledImages[imageIndex], false);
        }

        private void CreateOverlayIfNeeded()
        {
            if (overlayRoot != null)
                return;

            pooledImages.Clear();
            overlayRoot = new GameObject(
                "Traversal Reachability Overlay",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            overlayRoot.layer = LayerMask.NameToLayer("UI");

            Canvas canvas = overlayRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 20;
            overlayRoot.transform.rotation = Quaternion.identity;

            CanvasScaler scaler = overlayRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            GraphicRaycaster raycaster = overlayRoot.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;
        }

        private void EnsureImagePool()
        {
            int requiredCount = Mathf.Min(
                Mathf.Max(64, maximumVisibleSigns),
                Mathf.Max(1, activeColumns * activeRows));
            int uiLayer = LayerMask.NameToLayer("UI");

            while (pooledImages.Count < requiredCount)
            {
                var imageObject = new GameObject(
                    $"Traversal Sign {pooledImages.Count + 1:0000}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                imageObject.layer = uiLayer;
                imageObject.transform.SetParent(overlayRoot.transform, false);

                Image image = imageObject.GetComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.enabled = false;
                pooledImages.Add(image);
            }

            for (int i = requiredCount; i < pooledImages.Count; i++)
                SetImageEnabled(pooledImages[i], false);
        }

        private void UpdateGridDimensions()
        {
            float availableFraction = Mathf.Max(0.01f, 1f - viewportMargin * 2f);
            float spacing = Mathf.Max(4f, gridSpacingPixels);
            activeColumns = Mathf.Max(
                1,
                Mathf.FloorToInt(Screen.width * availableFraction / spacing));
            activeRows = Mathf.Max(
                1,
                Mathf.FloorToInt(Screen.height * availableFraction / spacing));

            int requestedCount = activeColumns * activeRows;
            int limit = Mathf.Max(64, maximumVisibleSigns);
            if (requestedCount <= limit)
                return;

            float scale = Mathf.Sqrt(limit / (float)requestedCount);
            activeColumns = Mathf.Max(1, Mathf.FloorToInt(activeColumns * scale));
            activeRows = Mathf.Max(1, Mathf.FloorToInt(activeRows * scale));
        }

        private void PositionImage(Image image, Vector2 viewportPosition)
        {
            RectTransform rect = image.rectTransform;
            rect.anchorMin = viewportPosition;
            rect.anchorMax = viewportPosition;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.one * iconSize;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        private static void SetImageEnabled(Image image, bool isEnabled)
        {
            if (image != null)
                image.enabled = isEnabled;
        }

        private void HideAllImages()
        {
            for (int i = 0; i < pooledImages.Count; i++)
                SetImageEnabled(pooledImages[i], false);
        }

        private void OnValidate()
        {
            gridSpacingPixels = Mathf.Max(4f, gridSpacingPixels);
            centerExclusionRadiusPixels = Mathf.Max(0f, centerExclusionRadiusPixels);
            displayRadiusMeters = Mathf.Max(0f, displayRadiusMeters);
            maximumVisibleSigns = Mathf.Max(64, maximumVisibleSigns);
            iconSize = Mathf.Max(1f, iconSize);
            refreshRate = Mathf.Max(0.1f, refreshRate);
        }
    }
}
