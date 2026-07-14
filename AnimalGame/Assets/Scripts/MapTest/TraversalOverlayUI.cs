using System.Collections.Generic;
using AnimalGame.RobotMap;
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

        [Header("Robot Reachability Scan")]
        [Tooltip("Number of directions tested around the robot.")]
        [SerializeField, Range(8, 128)] private int angularSamples = 64;

        [Tooltip("Number of signs tested along every direction.")]
        [SerializeField, Range(2, 32)] private int radialSamples = 16;

        [Tooltip("The first sign is placed this many logical map meters away from the robot.")]
        [SerializeField, Min(0f)] private float innerRadiusMeters = 20f;

        [Tooltip("Maximum logical map distance covered by the reachability scan.")]
        [SerializeField, Min(0.1f)] private float displayRadiusMeters = 120f;

        [Tooltip("Safety limit for the number of pooled UI signs.")]
        [SerializeField, Range(64, 4096)] private int maximumVisibleSigns = 1600;

        [SerializeField, Range(0f, 0.45f)] private float viewportMargin = 0.02f;

        [Header("Presentation")]
        [SerializeField, Min(1f)] private float iconSize = 7.6f;
        [SerializeField, Min(0.1f)] private float refreshRate = 5f;

        [Tooltip("Opacity used beyond the first blocking point to show that the rest of that direct route is unreachable.")]
        [SerializeField, Range(0.05f, 1f)] private float blockedContinuationOpacity = 0.45f;

        private readonly List<Image> pooledImages = new List<Image>();

        private MapTestSceneController map;
        private HeightMapTraversalEvaluator evaluator;
        private Camera mapCamera;
        private RobotMover playerRobot;
        private GameObject overlayRoot;
        private float nextRefreshTime;

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

        private void OnDestroy()
        {
            if (overlayRoot != null)
                Destroy(overlayRoot);
        }

        private void Update()
        {
            if (map == null || evaluator == null || mapCamera == null || playerRobot == null)
                return;

            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + 1f / Mathf.Max(0.1f, refreshRate);
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
            EnsureImagePool();

            Vector2 mapForward = map.WorldDirectionToMapDirection(playerRobot.Forward);
            if (mapForward.sqrMagnitude < 0.000001f)
                mapForward = Vector2.up;

            float minimumRadius = Mathf.Min(innerRadiusMeters, displayRadiusMeters);
            int imageIndex = 0;
            int directions = Mathf.Max(8, angularSamples);
            int rings = Mathf.Max(2, radialSamples);

            for (int directionIndex = 0; directionIndex < directions; directionIndex++)
            {
                float angle = directionIndex * (360f / directions);
                Vector2 mapDirection = Rotate(mapForward, angle);
                Vector2 previousMapPosition = robotMapPosition;
                bool routeBlocked = false;

                for (int ringIndex = 0; ringIndex < rings; ringIndex++)
                {
                    float ringProgress = rings > 1
                        ? ringIndex / (float)(rings - 1)
                        : 1f;
                    float radius = Mathf.Lerp(minimumRadius, displayRadiusMeters, ringProgress);
                    Vector2 targetMapPosition = robotMapPosition + mapDirection * radius;

                    if (!map.TrySampleMapPosition(targetMapPosition, out _))
                        break;

                    bool wasAlreadyBlocked = routeBlocked;
                    if (!routeBlocked)
                    {
                        SlopeTraversalResult result = evaluator.EvaluateMapPath(
                            previousMapPosition,
                            targetMapPosition);

                        if (!result.HasData)
                        {
                            previousMapPosition = targetMapPosition;
                            continue;
                        }

                        routeBlocked = !result.IsPassable;
                    }

                    Vector3 worldPosition = map.MapPositionToWorld(targetMapPosition);
                    Vector3 viewportPosition = mapCamera.WorldToViewportPoint(worldPosition);
                    previousMapPosition = targetMapPosition;

                    if (viewportPosition.z <= 0f
                        || viewportPosition.x < viewportMargin
                        || viewportPosition.x > 1f - viewportMargin
                        || viewportPosition.y < viewportMargin
                        || viewportPosition.y > 1f - viewportMargin)
                    {
                        continue;
                    }

                    if (imageIndex >= pooledImages.Count)
                        goto Finish;

                    Image image = pooledImages[imageIndex++];
                    PositionImage(image, new Vector2(viewportPosition.x, viewportPosition.y));

                    Sprite desiredSprite = routeBlocked ? unpassableSign : passableSign;
                    image.sprite = desiredSprite;
                    float opacity = routeBlocked && wasAlreadyBlocked
                        ? blockedContinuationOpacity
                        : 1f;
                    image.color = new Color(1f, 1f, 1f, opacity);
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
            canvas.sortingOrder = 20;

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
                Mathf.Max(8, angularSamples) * Mathf.Max(2, radialSamples));
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

        private void PositionImage(Image image, Vector2 viewportPosition)
        {
            RectTransform rect = image.rectTransform;
            rect.anchorMin = viewportPosition;
            rect.anchorMax = viewportPosition;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.one * iconSize;
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sine = Mathf.Sin(radians);
            float cosine = Mathf.Cos(radians);
            return new Vector2(
                vector.x * cosine - vector.y * sine,
                vector.x * sine + vector.y * cosine);
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
            angularSamples = Mathf.Max(8, angularSamples);
            radialSamples = Mathf.Max(2, radialSamples);
            innerRadiusMeters = Mathf.Max(0f, innerRadiusMeters);
            displayRadiusMeters = Mathf.Max(0.1f, displayRadiusMeters);
            maximumVisibleSigns = Mathf.Max(64, maximumVisibleSigns);
            iconSize = Mathf.Max(1f, iconSize);
            refreshRate = Mathf.Max(0.1f, refreshRate);
        }
    }
}
