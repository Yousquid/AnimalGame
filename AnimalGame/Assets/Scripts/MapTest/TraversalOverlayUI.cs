using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.MapTest
{
    [DisallowMultipleComponent]
    public sealed class TraversalOverlayUI : MonoBehaviour
    {
        [Header("Signs")]
        [SerializeField] private Sprite passableSign;
        [SerializeField] private Sprite unpassableSign;

        [Header("Viewport Grid")]
        [Tooltip("Approximate screen-pixel distance between neighboring signs. Lower values produce a denser grid.")]
        [SerializeField, Range(2f, 160f)] private float iconSpacingPixels = 42f;

        [Tooltip("Safety limit used when the game runs at a very high resolution.")]
        [SerializeField, Range(64, 4096)] private int maximumVisibleSigns = 1600;

        [SerializeField, Range(0f, 0.45f)] private float viewportMargin = 0.05f;
        [SerializeField, Min(1f)] private float iconSize = 12f;
        [SerializeField, Min(0.1f)] private float refreshRate = 10f;

        [Header("Display Range")]
        [Tooltip("Maximum logical map distance from the player where signs are shown. Set to 0 for no distance limit.")]
        [SerializeField, Min(0f)] private float displayRadiusMeters = 12f;

        [Tooltip("Radius around the player marker where signs are hidden, normalized to the viewport height.")]
        [SerializeField, Range(0f, 0.5f)] private float centerExclusionRadius = 0.05f;

        private readonly List<Image> pooledImages = new List<Image>();

        private MapTestSceneController map;
        private HeightMapTraversalEvaluator evaluator;
        private Camera mapCamera;
        private Transform exclusionTarget;
        private GameObject overlayRoot;
        private RectTransform overlayRect;
        private float nextRefreshTime;
        private int activeColumns = 1;
        private int activeRows = 1;

        public void Initialize(
            MapTestSceneController mapController,
            HeightMapTraversalEvaluator traversalEvaluator,
            Camera cameraToSample,
            Transform playerTarget)
        {
            map = mapController;
            evaluator = traversalEvaluator;
            mapCamera = cameraToSample;
            exclusionTarget = playerTarget;

            if (map == null || evaluator == null || mapCamera == null)
            {
                Debug.LogError(
                    "TraversalOverlayUI requires a map, traversal evaluator, and camera.",
                    this);
                HideAllImages();
                return;
            }

            CreateOverlayIfNeeded();
            UpdateGridDimensions();
            EnsureImagePool();
            overlayRoot.SetActive(isActiveAndEnabled);
            nextRefreshTime = 0f;
            RefreshOverlay();
        }

        private void OnEnable()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(true);

            nextRefreshTime = 0f;
        }

        private void OnDisable()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }

        private void Update()
        {
            if (map == null || evaluator == null || mapCamera == null)
                return;

            if (Time.unscaledTime < nextRefreshTime)
                return;

            float interval = 1f / Mathf.Max(0.1f, refreshRate);
            nextRefreshTime = Time.unscaledTime + interval;
            RefreshOverlay();
        }

        private void RefreshOverlay()
        {
            if (map == null
                || evaluator == null
                || mapCamera == null
                || !map.HasGeneratedMap)
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
            Vector2 exclusionCenter = GetExclusionCenter();
            Vector2 playerMapPosition = Vector2.zero;
            bool hasPlayerMapPosition = exclusionTarget != null
                                        && map.TrySampleWorldPosition(
                                            exclusionTarget.position,
                                            out playerMapPosition,
                                            out _);
            int imageIndex = 0;

            for (int row = 0; row < activeRows; row++)
            {
                float rowProgress = (row + 0.5f) / activeRows;
                float viewportY = viewportMargin + availableViewport * rowProgress;

                for (int column = 0; column < activeColumns; column++)
                {
                    Image image = pooledImages[imageIndex++];
                    float columnProgress = (column + 0.5f) / activeColumns;
                    float viewportX = viewportMargin + availableViewport * columnProgress;
                    Vector2 viewportPosition = new Vector2(viewportX, viewportY);

                    PositionImage(image, viewportPosition);

                    Vector2 exclusionOffset = viewportPosition - exclusionCenter;
                    exclusionOffset.x *= mapCamera.aspect;
                    if (exclusionOffset.sqrMagnitude
                        < centerExclusionRadius * centerExclusionRadius)
                    {
                        SetImageEnabled(image, false);
                        continue;
                    }

                    Vector3 worldPosition = mapCamera.ViewportToWorldPoint(
                        new Vector3(viewportX, viewportY, mapPlaneDistance));
                    if (!map.TrySampleWorldPosition(
                            worldPosition,
                            out Vector2 sampleMapPosition,
                            out _))
                    {
                        SetImageEnabled(image, false);
                        continue;
                    }

                    if (hasPlayerMapPosition
                        && displayRadiusMeters > 0f
                        && Vector2.Distance(sampleMapPosition, playerMapPosition)
                        > displayRadiusMeters)
                    {
                        SetImageEnabled(image, false);
                        continue;
                    }

                    SlopeTraversalResult result = evaluator.EvaluateLocalSurface(worldPosition);
                    if (!result.HasData)
                    {
                        SetImageEnabled(image, false);
                        continue;
                    }

                    Sprite desiredSprite = result.IsPassable ? passableSign : unpassableSign;
                    if (image.sprite != desiredSprite)
                        image.sprite = desiredSprite;
                    SetImageEnabled(image, desiredSprite != null);
                }
            }

            for (; imageIndex < pooledImages.Count; imageIndex++)
                SetImageEnabled(pooledImages[imageIndex], false);
        }

        private void CreateOverlayIfNeeded()
        {
            if (overlayRoot != null)
                return;

            // Unity objects can be destroyed independently while their managed
            // references remain in the pool. A rebuilt canvas needs a clean pool.
            pooledImages.Clear();

            overlayRoot = new GameObject(
                "Traversal Overlay Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                overlayRoot.layer = uiLayer;

            Canvas canvas = overlayRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = overlayRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            overlayRect = overlayRoot.GetComponent<RectTransform>();
        }

        private void EnsureImagePool()
        {
            int requiredCount = Mathf.Max(1, activeColumns) * Mathf.Max(1, activeRows);
            int uiLayer = LayerMask.NameToLayer("UI");

            while (pooledImages.Count < requiredCount)
            {
                var imageObject = new GameObject(
                    $"Traversal Sign {pooledImages.Count + 1:00}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                if (uiLayer >= 0)
                    imageObject.layer = uiLayer;

                RectTransform rect = imageObject.GetComponent<RectTransform>();
                rect.SetParent(overlayRect, false);
                rect.sizeDelta = Vector2.one * iconSize;

                Image image = imageObject.GetComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.enabled = false;
                pooledImages.Add(image);
            }

            for (int i = requiredCount; i < pooledImages.Count; i++)
                SetImageEnabled(pooledImages[i], false);
        }

        private void UpdateGridDimensions()
        {
            float availableFraction = Mathf.Max(0.01f, 1f - viewportMargin * 2f);
            float spacing = Mathf.Max(2f, iconSpacingPixels);
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

        private Vector2 GetExclusionCenter()
        {
            if (exclusionTarget == null || mapCamera == null)
                return Vector2.one * 0.5f;

            Vector3 viewportPosition = mapCamera.WorldToViewportPoint(exclusionTarget.position);
            return new Vector2(viewportPosition.x, viewportPosition.y);
        }

        private void PositionImage(Image image, Vector2 viewportPosition)
        {
            RectTransform rect = image.rectTransform;
            if (rect.anchorMin != viewportPosition || rect.anchorMax != viewportPosition)
            {
                rect.anchorMin = viewportPosition;
                rect.anchorMax = viewportPosition;
                rect.anchoredPosition = Vector2.zero;
            }

            Vector2 desiredSize = Vector2.one * iconSize;
            if (rect.sizeDelta != desiredSize)
                rect.sizeDelta = desiredSize;
        }

        private static void SetImageEnabled(Image image, bool isEnabled)
        {
            if (image.enabled != isEnabled)
                image.enabled = isEnabled;
        }

        private void HideAllImages()
        {
            for (int i = 0; i < pooledImages.Count; i++)
                SetImageEnabled(pooledImages[i], false);
        }

        private void OnDestroy()
        {
            pooledImages.Clear();
            if (overlayRoot == null)
                return;

            if (Application.isPlaying)
                Destroy(overlayRoot);
            else
                DestroyImmediate(overlayRoot);
        }

        private void OnValidate()
        {
            iconSpacingPixels = Mathf.Clamp(iconSpacingPixels, 2f, 160f);
            maximumVisibleSigns = Mathf.Clamp(maximumVisibleSigns, 64, 4096);
            viewportMargin = Mathf.Clamp(viewportMargin, 0f, 0.45f);
            iconSize = Mathf.Max(1f, iconSize);
            refreshRate = Mathf.Max(0.1f, refreshRate);
            displayRadiusMeters = Mathf.Max(0f, displayRadiusMeters);
            centerExclusionRadius = Mathf.Clamp(centerExclusionRadius, 0f, 0.5f);
        }
    }
}
