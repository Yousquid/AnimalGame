using System.Collections.Generic;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Q-toggleable full-grid debug visualization. Production scan markers are
    /// intentionally owned by TraversalScanOverlayUI and never read this state.
    /// </summary>
    [DefaultExecutionOrder(100)]
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

        [Tooltip("Screen-pixel radius around the robot marker where signs are neither calculated nor rendered. The sign's own size is excluded as well.")]
        [SerializeField, Min(0f)] private float centerExclusionRadiusPixels = 36f;

        [Tooltip("Maximum logical map distance from the robot where signs are shown. Set to 0 for no distance limit.")]
        [SerializeField, Min(0f)] private float displayRadiusMeters = 120f;

        [Tooltip("Target maximum number of signs inside the display range. Grid spacing is increased automatically when needed to stay within this limit.")]
        [SerializeField, Range(64, 4096)] private int maximumVisibleSigns = 1600;

        [SerializeField, Range(0f, 0.45f)] private float viewportMargin = 0.02f;

        [Header("Presentation")]
        [SerializeField, Min(1f)] private float iconSize = 7.6f;
        [SerializeField, Min(0.1f)] private float refreshRate = 3f;

        private readonly List<Image> pooledImages = new List<Image>();
        private readonly List<Vector2> stableGridScreenPositions = new List<Vector2>();
        private readonly List<Vector2> visibleCandidateScreenPositions = new List<Vector2>();

        private MapTestSceneController map;
        private HeightMapTraversalEvaluator evaluator;
        private Camera mapCamera;
        private RobotMover playerRobot;
        private GameObject overlayRoot;
        private float nextRefreshTime;
        // Debug visualization starts hidden so it cannot visually obscure the
        // production scan snapshot. Q still toggles the complete legacy grid.
        private bool isOverlayVisible;
        private int cachedScreenWidth = -1;
        private int cachedScreenHeight = -1;
        private float cachedGridSpacingPixels = float.NaN;
        private float cachedDisplayRadiusMeters = float.NaN;
        private float cachedViewportMargin = float.NaN;
        private float cachedCameraSize = float.NaN;
        private int cachedMaximumVisibleSigns = -1;

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
            overlayRoot.SetActive(isActiveAndEnabled && isOverlayVisible);
            InvalidateGridLayout();
            nextRefreshTime = 0f;
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
        }

        private void LateUpdate()
        {
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

            Bounds bounds = map.WorldBounds;
            Vector3 robotViewport = mapCamera.WorldToViewportPoint(playerRobot.transform.position);
            Vector2 robotScreenPosition = new Vector2(
                robotViewport.x * Screen.width,
                robotViewport.y * Screen.height);
            Vector2 displayRadiusPixels = CalculateDisplayRadiusPixels(
                playerRobot.transform.position,
                robotScreenPosition);
            EnsureStableGridLayout(robotScreenPosition, displayRadiusPixels);
            BuildVisibleCandidateScreenPositions(robotScreenPosition, displayRadiusPixels);
            EnsureImagePool(visibleCandidateScreenPositions.Count);

            float displayRadiusSquared = displayRadiusMeters * displayRadiusMeters;
            int imageIndex = 0;

            for (int candidateIndex = 0;
                 candidateIndex < visibleCandidateScreenPositions.Count;
                 candidateIndex++)
            {
                Vector2 screenPosition = visibleCandidateScreenPositions[candidateIndex];
                // Keep this second guard next to the expensive map/path work. Candidate
                // generation already filters the exclusion area, but this guarantees that
                // future grid-generation changes cannot calculate or render signs there.
                if (IsInsideCenterExclusion(screenPosition, robotScreenPosition))
                    continue;

                Vector2 viewportPosition = new Vector2(
                    screenPosition.x / Mathf.Max(1f, Screen.width),
                    screenPosition.y / Mathf.Max(1f, Screen.height));
                if (!TryProjectViewportPointToMapPlane(
                        viewportPosition,
                        bounds.center.z,
                        out Vector3 worldPosition))
                {
                    continue;
                }

                if (!map.TrySampleWorldPosition(
                        worldPosition,
                        out Vector2 targetMapPosition,
                        out _))
                {
                    continue;
                }

                if (displayRadiusMeters > 0f
                    && (targetMapPosition - robotMapPosition).sqrMagnitude
                    > displayRadiusSquared)
                {
                    continue;
                }

                SlopeTraversalResult result = evaluator.EvaluateMapPath(
                    robotMapPosition,
                    targetMapPosition);
                if (!result.HasData)
                    continue;

                if (imageIndex >= pooledImages.Count)
                    break;

                Image image = pooledImages[imageIndex++];
                PositionImage(image, viewportPosition);

                Sprite desiredSprite = result.IsPassable ? passableSign : unpassableSign;
                image.sprite = desiredSprite;
                image.color = Color.white;
                SetImageEnabled(image, desiredSprite != null);
            }

            for (; imageIndex < pooledImages.Count; imageIndex++)
                SetImageEnabled(pooledImages[imageIndex], false);
        }

        private bool TryProjectViewportPointToMapPlane(
            Vector2 viewportPoint,
            float mapPlaneZ,
            out Vector3 worldPoint)
        {
            Ray ray = mapCamera.ViewportPointToRay(
                new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            float directionAlongPlaneNormal = ray.direction.z;
            if (Mathf.Abs(directionAlongPlaneNormal) < 0.000001f)
            {
                worldPoint = default;
                return false;
            }

            float distance = (mapPlaneZ - ray.origin.z)
                             / directionAlongPlaneNormal;
            if (distance < 0f)
            {
                worldPoint = default;
                return false;
            }

            worldPoint = ray.GetPoint(distance);
            return true;
        }

        private void CreateOverlayIfNeeded()
        {
            if (overlayRoot != null)
                return;

            pooledImages.Clear();
            overlayRoot = new GameObject(
                "Debug Traversal Reachability Overlay",
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

        private void EnsureImagePool(int requestedCount)
        {
            int requiredCount = Mathf.Min(
                Mathf.Max(64, maximumVisibleSigns),
                Mathf.Max(1, requestedCount));
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

        private Vector2 CalculateDisplayRadiusPixels(
            Vector3 robotWorldPosition,
            Vector2 robotScreenPosition)
        {
            if (displayRadiusMeters <= 0f)
                return new Vector2(Screen.width, Screen.height);

            Vector2 worldRight = ((Vector2)mapCamera.transform.right).normalized;
            Vector2 worldUp = ((Vector2)mapCamera.transform.up).normalized;
            float horizontalWorldDistance = map.MapMetersToWorldDistance(
                worldRight,
                displayRadiusMeters);
            float verticalWorldDistance = map.MapMetersToWorldDistance(
                worldUp,
                displayRadiusMeters);

            Vector3 horizontalScreenPoint = mapCamera.WorldToScreenPoint(
                robotWorldPosition + (Vector3)(worldRight * horizontalWorldDistance));
            Vector3 verticalScreenPoint = mapCamera.WorldToScreenPoint(
                robotWorldPosition + (Vector3)(worldUp * verticalWorldDistance));

            return new Vector2(
                Mathf.Max(1f, Mathf.Abs(horizontalScreenPoint.x - robotScreenPosition.x)),
                Mathf.Max(1f, Mathf.Abs(verticalScreenPoint.y - robotScreenPosition.y)));
        }

        private void EnsureStableGridLayout(
            Vector2 robotScreenPosition,
            Vector2 displayRadiusPixels)
        {
            if (!GridLayoutNeedsRebuild())
                return;

            int limit = Mathf.Max(64, maximumVisibleSigns);
            float requestedSpacing = Mathf.Max(4f, gridSpacingPixels);
            float marginX = Screen.width * viewportMargin;
            float marginY = Screen.height * viewportMargin;
            bool hasDistanceLimit = displayRadiusMeters > 0f;

            float estimatedArea = hasDistanceLimit
                ? Mathf.PI * displayRadiusPixels.x * displayRadiusPixels.y
                : Mathf.Max(1f, Screen.width - marginX * 2f)
                  * Mathf.Max(1f, Screen.height - marginY * 2f);
            float spacing = Mathf.Max(
                requestedSpacing,
                Mathf.Sqrt(estimatedArea / limit));

            // The density is solved only while rebuilding the layout. During normal
            // refreshes this spacing stays fixed, so the entire grid cannot jump.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                BuildStableScreenGrid(spacing, marginX, marginY);
                int count = CountVisibleGridPositions(
                    robotScreenPosition,
                    displayRadiusPixels,
                    limit + 1);
                if (count > limit)
                {
                    spacing *= Mathf.Sqrt(count / (float)limit) * 1.005f;
                    continue;
                }

                break;
            }

            // Pool order is centre-out. If a changed camera projection ever places more
            // points inside the range than the cap, only the outer edge is clipped.
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            stableGridScreenPositions.Sort((left, right) =>
            {
                float leftDistance = (left - screenCenter).sqrMagnitude;
                float rightDistance = (right - screenCenter).sqrMagnitude;
                return leftDistance.CompareTo(rightDistance);
            });

            cachedScreenWidth = Screen.width;
            cachedScreenHeight = Screen.height;
            cachedGridSpacingPixels = gridSpacingPixels;
            cachedDisplayRadiusMeters = displayRadiusMeters;
            cachedViewportMargin = viewportMargin;
            cachedMaximumVisibleSigns = maximumVisibleSigns;
            cachedCameraSize = GetCameraSize();
        }

        private void BuildStableScreenGrid(float spacing, float marginX, float marginY)
        {
            stableGridScreenPositions.Clear();

            float maximumX = Screen.width - marginX;
            float maximumY = Screen.height - marginY;
            float firstX = Mathf.Ceil(marginX / spacing) * spacing;
            float firstY = Mathf.Ceil(marginY / spacing) * spacing;

            for (float screenY = firstY;
                 screenY <= maximumY + 0.01f;
                 screenY += spacing)
            {
                for (float screenX = firstX;
                     screenX <= maximumX + 0.01f;
                     screenX += spacing)
                {
                    stableGridScreenPositions.Add(new Vector2(screenX, screenY));
                }
            }
        }

        private void BuildVisibleCandidateScreenPositions(
            Vector2 robotScreenPosition,
            Vector2 displayRadiusPixels)
        {
            visibleCandidateScreenPositions.Clear();
            int limit = Mathf.Max(64, maximumVisibleSigns);

            for (int index = 0; index < stableGridScreenPositions.Count; index++)
            {
                Vector2 screenPosition = stableGridScreenPositions[index];
                if (IsInsideCenterExclusion(screenPosition, robotScreenPosition)
                    || !IsInsideDisplayRange(
                        screenPosition,
                        robotScreenPosition,
                        displayRadiusPixels))
                {
                    continue;
                }

                visibleCandidateScreenPositions.Add(screenPosition);
                if (visibleCandidateScreenPositions.Count >= limit)
                    break;
            }
        }

        private int CountVisibleGridPositions(
            Vector2 robotScreenPosition,
            Vector2 displayRadiusPixels,
            int stopAfter)
        {
            int count = 0;
            for (int index = 0; index < stableGridScreenPositions.Count; index++)
            {
                Vector2 screenPosition = stableGridScreenPositions[index];
                if (IsInsideCenterExclusion(screenPosition, robotScreenPosition)
                    || !IsInsideDisplayRange(
                        screenPosition,
                        robotScreenPosition,
                        displayRadiusPixels))
                {
                    continue;
                }

                count++;
                if (count >= stopAfter)
                    break;
            }

            return count;
        }

        private bool IsInsideDisplayRange(
            Vector2 screenPosition,
            Vector2 robotScreenPosition,
            Vector2 displayRadiusPixels)
        {
            if (displayRadiusMeters <= 0f)
                return true;

            Vector2 offset = screenPosition - robotScreenPosition;
            float normalizedX = offset.x / Mathf.Max(1f, displayRadiusPixels.x);
            float normalizedY = offset.y / Mathf.Max(1f, displayRadiusPixels.y);
            return normalizedX * normalizedX + normalizedY * normalizedY <= 1f;
        }

        private bool GridLayoutNeedsRebuild()
        {
            return stableGridScreenPositions.Count == 0
                   || cachedScreenWidth != Screen.width
                   || cachedScreenHeight != Screen.height
                   || !Mathf.Approximately(cachedGridSpacingPixels, gridSpacingPixels)
                   || !Mathf.Approximately(cachedDisplayRadiusMeters, displayRadiusMeters)
                   || !Mathf.Approximately(cachedViewportMargin, viewportMargin)
                   || cachedMaximumVisibleSigns != maximumVisibleSigns
                   || !Mathf.Approximately(cachedCameraSize, GetCameraSize());
        }

        private float GetCameraSize()
        {
            if (mapCamera == null)
                return 0f;

            return mapCamera.orthographic
                ? mapCamera.orthographicSize
                : mapCamera.fieldOfView;
        }

        private void InvalidateGridLayout()
        {
            stableGridScreenPositions.Clear();
            visibleCandidateScreenPositions.Clear();
            cachedScreenWidth = -1;
            cachedScreenHeight = -1;
        }

        private bool IsInsideCenterExclusion(
            Vector2 screenPosition,
            Vector2 robotScreenPosition)
        {
            // Images are square. Adding half their diagonal keeps the entire icon outside
            // the circular exclusion area instead of checking only the icon's centre.
            const float HalfSquareDiagonal = 0.70710678f;
            float effectiveRadius = centerExclusionRadiusPixels
                                    + iconSize * HalfSquareDiagonal;
            return (screenPosition - robotScreenPosition).sqrMagnitude
                   < effectiveRadius * effectiveRadius;
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
            InvalidateGridLayout();
        }
    }
}
