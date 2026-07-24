using System.Collections.Generic;
using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.UI;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Production traversal display. A fully charged scan replaces the previous
    /// snapshot, reveals absolute map markers with the release wave, keeps them for
    /// a configured duration, and periodically re-evaluates robot passability.
    /// This component is deliberately independent from the Q debug overlay.
    /// </summary>
    [DefaultExecutionOrder(325)]
    [DisallowMultipleComponent]
    public sealed class TraversalScanOverlayUI : MonoBehaviour
    {
        [Header("Sign Assets")]
        [SerializeField] private Sprite passableSign;
        [SerializeField] private Sprite unpassableSign;

        [Header("Scan Sampling")]
        [Tooltip("Row/column spacing of scan candidates in reference-canvas pixels. This is a regular grid, not a radial pattern.")]
        [SerializeField, Min(4f)] private float sampleGridSpacingPixels = 32f;

        [Tooltip("Candidates this close to the visible contour crossing are included on both sides of the line, measured in logical map meters.")]
        [SerializeField, Min(0f)] private float contourBoundaryHalfWidthMeters = 3f;

        [Tooltip("Map distance used to estimate the local height gradient and contour normal.")]
        [SerializeField, Min(0.1f)] private float terrainGradientProbeMeters = 1.5f;

        [Tooltip("No production signs are calculated or rendered inside this screen-space radius around the fixed player UI centre.")]
        [SerializeField, Min(0f)] private float centerExclusionRadiusPixels = 40f;

        [Tooltip("Maximum number of signs produced by one scan snapshot.")]
        [SerializeField, Range(32, 4096)] private int maximumScannedSigns = 700;

        [Header("Closed-region Unpassable Expansion")]
        [Tooltip("Inside the player's current closed contour region, each locally unpassable sample also exposes every sampled traversal state within this N-meter radius.")]
        [SerializeField, Min(0f)] private float unpassableNeighborhoodRadiusMeters = 8f;

        [Header("Persistence and Refresh")]
        [Tooltip("Seconds the completed scan snapshot remains visible. A new scan always replaces it immediately.")]
        [SerializeField, Min(0.1f)] private float markerLifetimeSeconds = 8f;

        [Tooltip("Seconds between passability rechecks for the absolute marker positions.")]
        [SerializeField, Min(0.05f)] private float stateRefreshIntervalSeconds = 0.75f;

        [Tooltip("Maximum marker states re-evaluated in one frame when a refresh is due.")]
        [SerializeField, Range(1, 512)] private int refreshCalculationsPerFrame = 96;

        [Tooltip("Maximum new terrain samples evaluated in one frame while the release wave expands.")]
        [SerializeField, Range(1, 512)] private int scanCalculationsPerFrame = 128;

        [Header("Presentation")]
        [SerializeField, Min(1f)] private float iconSizePixels = 8f;
        [SerializeField, Range(-100, 100)] private int canvasSortingOrder = 21;

        private sealed class PendingScreenSample
        {
            public Vector2 ScreenPosition;
            public float Radius01;
        }

        private sealed class SampledCandidate
        {
            public Vector2 MapPosition;
            public Vector2 EvaluationDirection;
            public bool IsInsideClosedRegion;
            public bool IsPassable;
            public bool IsSelected;
        }

        private sealed class PersistentMarker
        {
            public Vector2 MapPosition;
            public Vector2 EvaluationDirection;
            public bool IsPassable;
        }

        private readonly List<PendingScreenSample> pendingSamples =
            new List<PendingScreenSample>();
        private readonly List<SampledCandidate> sampledCandidates =
            new List<SampledCandidate>();
        private readonly List<Vector2> unpassableSeeds = new List<Vector2>();
        private readonly List<PersistentMarker> markers =
            new List<PersistentMarker>();
        private readonly List<Image> pooledImages = new List<Image>();

        private MapTestSceneController map;
        private HeightMapTraversalEvaluator evaluator;
        private Camera mapCamera;
        private RobotMover robot;
        private ScanChargeUI scanChargeUi;
        private ContourRegionIndex contourRegions;
        private ContourRegionHandle scannedClosedRegion;
        private GameObject overlayRoot;
        private int nextPendingSample;
        private int nextRefreshMarker;
        private float scanStartedAt;
        private float scanWaveDuration;
        private float scannedUiRadiusPixels;
        private float snapshotExpiresAt;
        private float nextStateRefreshAt;
        private bool scanIsRevealing;
        private bool refreshInProgress;

        public int VisibleMarkerCount => markers.Count;
        public bool HasActiveSnapshot => scanIsRevealing || markers.Count > 0;

        public void Initialize(
            MapTestSceneController mapController,
            HeightMapTraversalEvaluator traversalEvaluator,
            Camera cameraToSample,
            RobotMover playerRobot,
            ScanChargeUI scanUi)
        {
            UnsubscribeFromScan();
            map = mapController;
            evaluator = traversalEvaluator;
            mapCamera = cameraToSample;
            robot = playerRobot;
            scanChargeUi = scanUi;

            if (map == null || evaluator == null || mapCamera == null
                || robot == null || scanChargeUi == null
                || map.HeightField == null)
            {
                Debug.LogError(
                    "TraversalScanOverlayUI requires map, evaluator, camera, robot, scan UI, and a baked height field.",
                    this);
                enabled = false;
                return;
            }

            contourRegions = new ContourRegionIndex(
                map.HeightField,
                map.ContourIntervalMeters);
            CreateOverlayIfNeeded();
            scanChargeUi.FullyChargedScanReleased += BeginScannedSnapshot;
            ClearSnapshot();
        }

        private void Update()
        {
            if (map == null || evaluator == null || mapCamera == null)
                return;

            if (scanIsRevealing)
                RevealScanWave();
            else
                UpdatePersistentSnapshot();

            if (refreshInProgress)
                ProcessRefreshBatch();
        }

        private void LateUpdate()
        {
            RenderMarkers();
        }

        private void BeginScannedSnapshot()
        {
            ClearSnapshot();
            if (!map.TrySampleWorldPosition(
                    robot.transform.position,
                    out Vector2 robotMapPosition,
                    out _))
            {
                return;
            }

            contourRegions.TryGetCurrentClosedRegion(
                robotMapPosition,
                out scannedClosedRegion);
            scannedUiRadiusPixels = Mathf.Max(
                1f,
                scanChargeUi.GetUiRingScreenRadiusPixels());
            scanWaveDuration = Mathf.Max(
                0.05f,
                scanChargeUi.ReleaseRingExpansionDuration);
            BuildPendingScreenGrid(scannedUiRadiusPixels);
            scanStartedAt = Time.unscaledTime;
            scanIsRevealing = true;
        }

        private void BuildPendingScreenGrid(float radiusPixels)
        {
            pendingSamples.Clear();
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float canvasScale = radiusPixels
                                / Mathf.Max(1f, scanChargeUi.UiRingRadiusPixels);
            float spacing = Mathf.Max(4f, sampleGridSpacingPixels * canvasScale);
            float minimumX = Mathf.Max(0f, centre.x - radiusPixels);
            float maximumX = Mathf.Min(Screen.width, centre.x + radiusPixels);
            float minimumY = Mathf.Max(0f, centre.y - radiusPixels);
            float maximumY = Mathf.Min(Screen.height, centre.y + radiusPixels);
            float firstX = Mathf.Ceil(minimumX / spacing) * spacing;
            float firstY = Mathf.Ceil(minimumY / spacing) * spacing;
            float radiusSquared = radiusPixels * radiusPixels;
            float exclusion = (centerExclusionRadiusPixels
                               + iconSizePixels * 0.70710678f) * canvasScale;
            float exclusionSquared = exclusion * exclusion;

            for (float y = firstY; y <= maximumY + 0.01f; y += spacing)
            {
                for (float x = firstX; x <= maximumX + 0.01f; x += spacing)
                {
                    Vector2 screenPosition = new Vector2(x, y);
                    float distanceSquared = (screenPosition - centre).sqrMagnitude;
                    if (distanceSquared > radiusSquared
                        || distanceSquared < exclusionSquared)
                    {
                        continue;
                    }

                    pendingSamples.Add(new PendingScreenSample
                    {
                        ScreenPosition = screenPosition,
                        Radius01 = Mathf.Sqrt(distanceSquared) / radiusPixels
                    });
                }
            }

            pendingSamples.Sort((left, right) =>
                left.Radius01.CompareTo(right.Radius01));
        }

        private void RevealScanWave()
        {
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - scanStartedAt)
                / Mathf.Max(0.05f, scanWaveDuration));
            float visibleRadius01 = Mathf.SmoothStep(0f, 1f, progress);
            int calculations = 0;

            while (nextPendingSample < pendingSamples.Count
                   && calculations < scanCalculationsPerFrame
                   && pendingSamples[nextPendingSample].Radius01
                   <= visibleRadius01 + 0.0001f)
            {
                SampleScreenCandidate(pendingSamples[nextPendingSample]);
                nextPendingSample++;
                calculations++;
            }

            if (progress < 1f || nextPendingSample < pendingSamples.Count)
                return;

            scanIsRevealing = false;
            snapshotExpiresAt = Time.unscaledTime
                                + Mathf.Max(0.1f, markerLifetimeSeconds);
            nextStateRefreshAt = Time.unscaledTime
                                 + Mathf.Max(0.05f, stateRefreshIntervalSeconds);
        }

        private void SampleScreenCandidate(PendingScreenSample pending)
        {
            if (!TryProjectScreenPointToMap(pending.ScreenPosition, out Vector2 mapPosition)
                || !TryAnalyzeLocalTerrain(
                    mapPosition,
                    out Vector2 gradientDirection,
                    out bool isNearContour))
            {
                return;
            }

            SlopeTraversalResult result = EvaluateAt(
                mapPosition,
                gradientDirection);
            if (!result.HasData)
                return;

            bool insideClosedRegion = scannedClosedRegion.IsValid
                                      && contourRegions.Contains(
                                          scannedClosedRegion,
                                          mapPosition);
            var candidate = new SampledCandidate
            {
                MapPosition = mapPosition,
                EvaluationDirection = gradientDirection,
                IsInsideClosedRegion = insideClosedRegion,
                IsPassable = result.IsPassable
            };
            sampledCandidates.Add(candidate);

            bool isInteriorUnpassable = insideClosedRegion
                                        && !result.IsPassable;
            if (isInteriorUnpassable)
            {
                unpassableSeeds.Add(mapPosition);
                SelectCandidate(candidate);
                ExpandAroundUnpassableSeed(mapPosition);
            }

            if (isNearContour
                || (insideClosedRegion && IsNearAnyUnpassableSeed(mapPosition)))
            {
                SelectCandidate(candidate);
            }
        }

        private void ExpandAroundUnpassableSeed(Vector2 seedMapPosition)
        {
            float radiusSquared = unpassableNeighborhoodRadiusMeters
                                  * unpassableNeighborhoodRadiusMeters;
            if (radiusSquared <= 0f)
                return;

            for (int index = 0; index < sampledCandidates.Count; index++)
            {
                SampledCandidate candidate = sampledCandidates[index];
                if (!candidate.IsInsideClosedRegion
                    || candidate.IsSelected
                    || (candidate.MapPosition - seedMapPosition).sqrMagnitude
                    > radiusSquared)
                {
                    continue;
                }

                SelectCandidate(candidate);
            }
        }

        private bool IsNearAnyUnpassableSeed(Vector2 mapPosition)
        {
            float radiusSquared = unpassableNeighborhoodRadiusMeters
                                  * unpassableNeighborhoodRadiusMeters;
            if (radiusSquared <= 0f)
                return false;

            for (int index = 0; index < unpassableSeeds.Count; index++)
            {
                if ((unpassableSeeds[index] - mapPosition).sqrMagnitude
                    <= radiusSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private void SelectCandidate(SampledCandidate candidate)
        {
            if (candidate.IsSelected || markers.Count >= maximumScannedSigns)
                return;

            candidate.IsSelected = true;
            markers.Add(new PersistentMarker
            {
                MapPosition = candidate.MapPosition,
                EvaluationDirection = candidate.EvaluationDirection,
                IsPassable = candidate.IsPassable
            });
            EnsureImagePool(markers.Count);
        }

        private bool TryAnalyzeLocalTerrain(
            Vector2 mapPosition,
            out Vector2 gradientDirection,
            out bool isNearContour)
        {
            gradientDirection = Vector2.up;
            isNearContour = false;
            float probeX = Mathf.Max(
                terrainGradientProbeMeters,
                map.HeightField.TexelSizeXMeters);
            float probeY = Mathf.Max(
                terrainGradientProbeMeters,
                map.HeightField.TexelSizeYMeters);
            if (!TrySampleMapHeight(mapPosition, out float centreHeight))
                return false;

            float left = SampleOrCentre(mapPosition + Vector2.left * probeX, centreHeight);
            float right = SampleOrCentre(mapPosition + Vector2.right * probeX, centreHeight);
            float down = SampleOrCentre(mapPosition + Vector2.down * probeY, centreHeight);
            float up = SampleOrCentre(mapPosition + Vector2.up * probeY, centreHeight);
            Vector2 gradient = new Vector2(
                (right - left) / (2f * probeX),
                (up - down) / (2f * probeY));
            float gradientMagnitude = gradient.magnitude;
            if (gradientMagnitude > 0.0001f)
                gradientDirection = gradient / gradientMagnitude;

            float interval = Mathf.Max(0.01f, map.ContourIntervalMeters);
            float relativeHeight = centreHeight
                                   - map.HeightField.MinimumHeightMeters;
            float nearestContourHeight = Mathf.Round(relativeHeight / interval)
                                         * interval
                                         + map.HeightField.MinimumHeightMeters;
            float estimatedDistance = gradientMagnitude > 0.0001f
                ? Mathf.Abs(centreHeight - nearestContourHeight)
                  / gradientMagnitude
                : float.PositiveInfinity;
            isNearContour = estimatedDistance
                            <= contourBoundaryHalfWidthMeters;
            return true;
        }

        private float SampleOrCentre(Vector2 mapPosition, float centreHeight)
        {
            return TrySampleMapHeight(mapPosition, out float height)
                ? height
                : centreHeight;
        }

        private bool TrySampleMapHeight(Vector2 mapPosition, out float height)
        {
            return map.TrySampleMapPosition(mapPosition, out height);
        }

        private SlopeTraversalResult EvaluateAt(
            Vector2 mapPosition,
            Vector2 mapDirection)
        {
            Vector2 direction = mapDirection.sqrMagnitude > 0.0001f
                ? mapDirection.normalized
                : Vector2.up;
            Vector3 worldPosition = map.MapPositionToWorld(mapPosition);
            Vector2 worldDirection = map.MapDirectionToWorldDirection(direction);
            return evaluator.EvaluateCurrentSurface(worldPosition, worldDirection);
        }

        private void UpdatePersistentSnapshot()
        {
            if (markers.Count == 0)
                return;

            if (Time.unscaledTime >= snapshotExpiresAt)
            {
                ClearSnapshot();
                return;
            }

            if (!refreshInProgress
                && Time.unscaledTime >= nextStateRefreshAt)
            {
                refreshInProgress = true;
                nextRefreshMarker = 0;
            }
        }

        private void ProcessRefreshBatch()
        {
            int processed = 0;
            while (nextRefreshMarker < markers.Count
                   && processed < refreshCalculationsPerFrame)
            {
                PersistentMarker marker = markers[nextRefreshMarker++];
                SlopeTraversalResult result = EvaluateAt(
                    marker.MapPosition,
                    marker.EvaluationDirection);
                if (result.HasData)
                    marker.IsPassable = result.IsPassable;
                processed++;
            }

            if (nextRefreshMarker < markers.Count)
                return;

            refreshInProgress = false;
            nextStateRefreshAt = Time.unscaledTime
                                 + Mathf.Max(0.05f, stateRefreshIntervalSeconds);
        }

        private bool TryProjectScreenPointToMap(
            Vector2 screenPosition,
            out Vector2 mapPosition)
        {
            mapPosition = default;
            Ray ray = mapCamera.ScreenPointToRay(
                new Vector3(screenPosition.x, screenPosition.y, 0f));
            float directionZ = ray.direction.z;
            if (Mathf.Abs(directionZ) < 0.000001f)
                return false;

            float distance = (map.WorldBounds.center.z - ray.origin.z) / directionZ;
            if (distance < 0f)
                return false;

            Vector3 worldPosition = ray.GetPoint(distance);
            return map.TrySampleWorldPosition(worldPosition, out mapPosition, out _);
        }

        private void RenderMarkers()
        {
            if (overlayRoot == null || mapCamera == null)
                return;

            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float currentRadius = scanChargeUi != null
                ? scanChargeUi.GetUiRingScreenRadiusPixels()
                : scannedUiRadiusPixels;
            float radiusSquared = currentRadius * currentRadius;
            float exclusionScale = currentRadius
                                   / Mathf.Max(1f, scanChargeUi.UiRingRadiusPixels);
            float exclusion = (centerExclusionRadiusPixels
                               + iconSizePixels * 0.70710678f) * exclusionScale;
            float exclusionSquared = exclusion * exclusion;
            int imageIndex = 0;

            for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
            {
                PersistentMarker marker = markers[markerIndex];
                Vector3 viewport = mapCamera.WorldToViewportPoint(
                    map.MapPositionToWorld(marker.MapPosition));
                if (viewport.z <= 0f
                    || viewport.x < 0f || viewport.x > 1f
                    || viewport.y < 0f || viewport.y > 1f)
                {
                    continue;
                }

                Vector2 screenPosition = new Vector2(
                    viewport.x * Screen.width,
                    viewport.y * Screen.height);
                float distanceSquared = (screenPosition - centre).sqrMagnitude;
                if (distanceSquared > radiusSquared
                    || distanceSquared < exclusionSquared)
                {
                    continue;
                }

                EnsureImagePool(imageIndex + 1);
                Image image = pooledImages[imageIndex++];
                Sprite sprite = marker.IsPassable
                    ? passableSign
                    : unpassableSign;
                RectTransform rect = image.rectTransform;
                rect.anchorMin = new Vector2(viewport.x, viewport.y);
                rect.anchorMax = rect.anchorMin;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.one * iconSizePixels;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                image.sprite = sprite;
                image.color = Color.white;
                image.enabled = sprite != null;
            }

            for (; imageIndex < pooledImages.Count; imageIndex++)
                pooledImages[imageIndex].enabled = false;
        }

        private void CreateOverlayIfNeeded()
        {
            if (overlayRoot != null)
                return;

            overlayRoot = new GameObject(
                "Scanned Traversal Snapshot Overlay",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            overlayRoot.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = overlayRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = canvasSortingOrder;
            CanvasScaler scaler = overlayRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            overlayRoot.GetComponent<GraphicRaycaster>().enabled = false;
        }

        private void EnsureImagePool(int requestedCount)
        {
            CreateOverlayIfNeeded();
            int count = Mathf.Min(maximumScannedSigns, Mathf.Max(0, requestedCount));
            int uiLayer = LayerMask.NameToLayer("UI");
            while (pooledImages.Count < count)
            {
                var imageObject = new GameObject(
                    $"Scanned Traversal Sign {pooledImages.Count + 1:0000}",
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
        }

        private void ClearSnapshot()
        {
            pendingSamples.Clear();
            sampledCandidates.Clear();
            unpassableSeeds.Clear();
            markers.Clear();
            scannedClosedRegion = default;
            nextPendingSample = 0;
            nextRefreshMarker = 0;
            scanIsRevealing = false;
            refreshInProgress = false;
            snapshotExpiresAt = 0f;
            nextStateRefreshAt = 0f;
            for (int index = 0; index < pooledImages.Count; index++)
                pooledImages[index].enabled = false;
        }

        private void UnsubscribeFromScan()
        {
            if (scanChargeUi != null)
                scanChargeUi.FullyChargedScanReleased -= BeginScannedSnapshot;
        }

        private void OnDisable()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(true);
        }

        private void OnDestroy()
        {
            UnsubscribeFromScan();
            if (overlayRoot != null)
                Destroy(overlayRoot);
        }

        private void OnValidate()
        {
            sampleGridSpacingPixels = Mathf.Max(4f, sampleGridSpacingPixels);
            contourBoundaryHalfWidthMeters = Mathf.Max(
                0f,
                contourBoundaryHalfWidthMeters);
            terrainGradientProbeMeters = Mathf.Max(0.1f, terrainGradientProbeMeters);
            centerExclusionRadiusPixels = Mathf.Max(0f, centerExclusionRadiusPixels);
            maximumScannedSigns = Mathf.Clamp(maximumScannedSigns, 32, 4096);
            unpassableNeighborhoodRadiusMeters = Mathf.Max(
                0f,
                unpassableNeighborhoodRadiusMeters);
            markerLifetimeSeconds = Mathf.Max(0.1f, markerLifetimeSeconds);
            stateRefreshIntervalSeconds = Mathf.Max(
                0.05f,
                stateRefreshIntervalSeconds);
            refreshCalculationsPerFrame = Mathf.Clamp(
                refreshCalculationsPerFrame,
                1,
                512);
            scanCalculationsPerFrame = Mathf.Clamp(
                scanCalculationsPerFrame,
                1,
                512);
            iconSizePixels = Mathf.Max(1f, iconSizePixels);
        }
    }
}
