using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.MapTest
{
    public sealed class MapTestSceneController : MonoBehaviour
    {
        private const float LowestVisibleContourOpacity = 0.15f;
        private const float HighestVisibleContourOpacity = 1f;

        [Header("Height Source")]
        [Tooltip("Original readable 8-bit grayscale image used only as the source for the runtime physical-height bake. It is no longer sampled directly by movement or contours.")]
        [SerializeField] private Texture2D heightMap;

        [Tooltip("Physical width represented by the complete height source, in logical map meters.")]
        [SerializeField, Min(1f)] private float mapWidthMeters = 1000f;

        [Tooltip("Physical height represented by the complete height source, in logical map meters.")]
        [SerializeField, Min(1f)] private float mapHeightMeters = 1000f;

        [Tooltip("Elevation assigned to the lowest normalized value in the baked height field.")]
        [SerializeField] private float minimumHeightMeters;

        [Tooltip("Elevation assigned to the highest normalized value in the baked height field.")]
        [SerializeField] private float maximumHeightMeters = 200f;

        [Header("Physical Height Field Bake")]
        [Tooltip("Square resolution of the shared runtime height field. Higher values retain smaller terrain details but increase bake time and memory.")]
        [SerializeField, Range(128, 2048)] private int bakedHeightResolution = 1024;

        [Tooltip("Maps the darkest and brightest values actually present in the 8-bit source to the configured minimum and maximum elevations. Disable to preserve the full 0-to-1 grayscale scale.")]
        [SerializeField] private bool normalizeSourceRange = true;

        [Tooltip("Gaussian standard deviation in logical map meters used to reconstruct a continuous physical surface from 8-bit height steps. About 99.7% of the smoothing kernel lies within three times this distance.")]
        [SerializeField, Min(0f)] private float surfaceSmoothingSigmaMeters = 0.75f;

        [Header("Visualization")]
        [Tooltip("Resolution of the generated color-map sprite. This changes visual sharpness only; physical height precision is controlled by Baked Height Resolution.")]
        [SerializeField, Range(128, 8000)] private int previewResolution = 512;

        [Tooltip("Vertical elevation difference in meters between neighboring contour lines.")]
        [SerializeField, Min(1f)] private float contourIntervalMeters = 10f;

        [Tooltip("Generated preview pixels per Unity world unit. Together with Preview Resolution, this determines the rendered map object's world-space size, not its logical meter size.")]
        [SerializeField, Min(1f)] private float pixelsPerUnit = 16f;

        [Header("Viewport Contours")]
        [Tooltip("Width of the lowest contour currently visible in the camera.")]
        [SerializeField, Range(0.1f, 10f)] private float minimumContourWidth = 0.75f;

        [Tooltip("Width of the highest contour currently visible in the camera.")]
        [SerializeField, Range(0.1f, 10f)] private float maximumContourWidth = 3f;

        [Tooltip("Maximum fraction of the gap between neighboring contours that one line may occupy.")]
        [SerializeField, Range(0.1f, 0.7f)] private float maximumContourCoverage = 0.45f;

        [Tooltip("Softness of contour edges in screen pixels. Lower values produce crisper lines.")]
        [SerializeField, Range(0.1f, 1.5f)] private float contourEdgeSoftness = 0.4f;

        [Tooltip("Number of samples used on each camera axis to find the visible height range.")]
        [SerializeField, Range(16, 128)] private int viewportHeightSamples = 64;

        [Header("Color Palette")]
        [Tooltip("Color outside the generated map.")]
        [SerializeField] private Color backgroundColor = new Color(0.008f, 0.012f, 0.017f);

        [Tooltip("Color used at the minimum map height.")]
        [SerializeField] private Color lowHeightColor = new Color(0.025f, 0.09f, 0.12f);

        [Tooltip("Color used around the middle map height.")]
        [SerializeField] private Color middleHeightColor = new Color(0.08f, 0.42f, 0.42f);

        [Tooltip("Color used at the maximum map height.")]
        [SerializeField] private Color highHeightColor = new Color(0.72f, 0.82f, 0.67f);

        [Tooltip("Base color of all dynamic contour lines before height-dependent opacity is applied.")]
        [SerializeField] private Color contourColor = new Color(0.92f, 0.97f, 1f);

        [Tooltip("Color of the mouse height-probe crosshair in the standalone map inspection scene.")]
        [SerializeField] private Color probeColor = new Color(1f, 0.83f, 0.27f);

        private Camera mapCamera;
        private SpriteRenderer mapRenderer;
        private LineRenderer crosshair;
        private BakedHeightField heightField;
        private bool cursorInsideMap;
        private Vector2 cursorUv;
        private float cursorRawGray;
        private float cursorHeight;
        private Material contourMaterial;
        private Texture2D generatedPreviewTexture;
        private Sprite generatedMapSprite;
        private int lastViewportUpdateFrame = -1;

        public float VisibleMinimumContourHeight { get; private set; }
        public float VisibleMaximumContourHeight { get; private set; }
        public Vector2 MapSizeMeters => new Vector2(mapWidthMeters, mapHeightMeters);
        public BakedHeightField HeightField => heightField;

        public bool HasGeneratedMap => mapRenderer != null && heightField != null;

        public Bounds WorldBounds
        {
            get
            {
                return HasGeneratedMap
                    ? mapRenderer.bounds
                    : new Bounds(Vector3.zero, Vector3.zero);
            }
        }

        private void Awake()
        {
            if (heightMap == null)
            {
                Debug.LogError("MapTestScene is missing its height-map texture.");
                enabled = false;
                return;
            }

            BakePhysicalHeightField();
            CreateCamera();
            CreateHeightVisualization();
            CreateCrosshair();
            UpdateVisibleContourRange(mapCamera);
        }

        private void OnEnable()
        {
            Camera.onPreCull += HandleCameraPreCull;
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= HandleCameraPreCull;
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        }

        private void Update()
        {
            UpdateHeightProbe();
        }

        public float SampleHeight(Vector2 uv)
        {
            return heightField != null
                ? heightField.SampleSurfaceHeight(uv)
                : minimumHeightMeters;
        }

        public float SampleDetailHeight(Vector2 uv)
        {
            return heightField != null
                ? heightField.SampleDetailHeight(uv)
                : minimumHeightMeters;
        }

        public bool TrySampleWorldPosition(
            Vector2 worldPosition,
            out Vector2 mapPositionMeters,
            out float heightMeters)
        {
            mapPositionMeters = Vector2.zero;
            heightMeters = 0f;

            if (!HasGeneratedMap)
                return false;

            Bounds bounds = WorldBounds;
            bool inside = worldPosition.x >= bounds.min.x && worldPosition.x <= bounds.max.x
                          && worldPosition.y >= bounds.min.y && worldPosition.y <= bounds.max.y;
            if (!inside)
                return false;

            Vector2 uv = new Vector2(
                Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPosition.x),
                Mathf.InverseLerp(bounds.min.y, bounds.max.y, worldPosition.y));

            mapPositionMeters = new Vector2(
                uv.x * mapWidthMeters,
                uv.y * mapHeightMeters);
            heightMeters = SampleHeight(uv);
            return true;
        }

        public bool TrySampleMapPosition(Vector2 mapPositionMeters, out float heightMeters)
        {
            heightMeters = 0f;

            if (!HasGeneratedMap
                || mapPositionMeters.x < 0f
                || mapPositionMeters.x > mapWidthMeters
                || mapPositionMeters.y < 0f
                || mapPositionMeters.y > mapHeightMeters)
            {
                return false;
            }

            Vector2 uv = new Vector2(
                mapPositionMeters.x / Mathf.Max(0.0001f, mapWidthMeters),
                mapPositionMeters.y / Mathf.Max(0.0001f, mapHeightMeters));
            heightMeters = SampleHeight(uv);
            return true;
        }

        public bool TrySampleDetailMapPosition(
            Vector2 mapPositionMeters,
            out float heightMeters)
        {
            heightMeters = 0f;

            if (!HasGeneratedMap
                || mapPositionMeters.x < 0f
                || mapPositionMeters.x > mapWidthMeters
                || mapPositionMeters.y < 0f
                || mapPositionMeters.y > mapHeightMeters)
            {
                return false;
            }

            Vector2 uv = new Vector2(
                mapPositionMeters.x / Mathf.Max(0.0001f, mapWidthMeters),
                mapPositionMeters.y / Mathf.Max(0.0001f, mapHeightMeters));
            heightMeters = SampleDetailHeight(uv);
            return true;
        }

        public Vector3 MapPositionToWorld(Vector2 mapPositionMeters)
        {
            if (!HasGeneratedMap)
                return transform.position;

            Vector2 clampedMapPosition = new Vector2(
                Mathf.Clamp(mapPositionMeters.x, 0f, mapWidthMeters),
                Mathf.Clamp(mapPositionMeters.y, 0f, mapHeightMeters));
            Vector2 uv = new Vector2(
                clampedMapPosition.x / Mathf.Max(0.0001f, mapWidthMeters),
                clampedMapPosition.y / Mathf.Max(0.0001f, mapHeightMeters));
            Bounds bounds = WorldBounds;
            return new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, uv.x),
                Mathf.Lerp(bounds.min.y, bounds.max.y, uv.y),
                bounds.center.z);
        }

        public float MapMetersToWorldDistance(Vector2 worldDirection, float distanceMeters)
        {
            if (!HasGeneratedMap || worldDirection.sqrMagnitude < 0.000001f)
                return 0f;

            Bounds bounds = WorldBounds;
            Vector2 direction = worldDirection.normalized;
            float mapMetersPerWorldUnit = Mathf.Sqrt(
                Mathf.Pow(direction.x * mapWidthMeters / Mathf.Max(0.0001f, bounds.size.x), 2f)
                + Mathf.Pow(direction.y * mapHeightMeters / Mathf.Max(0.0001f, bounds.size.y), 2f));
            return mapMetersPerWorldUnit > 0.0001f
                ? distanceMeters / mapMetersPerWorldUnit
                : 0f;
        }

        public Vector2 WorldDirectionToMapDirection(Vector2 worldDirection)
        {
            if (!HasGeneratedMap || worldDirection.sqrMagnitude < 0.000001f)
                return Vector2.zero;

            Bounds bounds = WorldBounds;
            Vector2 mapDirection = new Vector2(
                worldDirection.x * mapWidthMeters / Mathf.Max(0.0001f, bounds.size.x),
                worldDirection.y * mapHeightMeters / Mathf.Max(0.0001f, bounds.size.y));
            return mapDirection.normalized;
        }

        public Vector2 MapDirectionToWorldDirection(Vector2 mapDirection)
        {
            if (!HasGeneratedMap || mapDirection.sqrMagnitude < 0.000001f)
                return Vector2.zero;

            Bounds bounds = WorldBounds;
            Vector2 worldDirection = new Vector2(
                mapDirection.x * bounds.size.x / Mathf.Max(0.0001f, mapWidthMeters),
                mapDirection.y * bounds.size.y / Mathf.Max(0.0001f, mapHeightMeters));
            return worldDirection.normalized;
        }

        public void UseCamera(Camera cameraToUse)
        {
            if (cameraToUse == null)
                return;

            if (mapCamera != null && mapCamera != cameraToUse)
                mapCamera.gameObject.SetActive(false);

            mapCamera = cameraToUse;
            mapCamera.backgroundColor = backgroundColor;
            UpdateVisibleContourRange(mapCamera);
        }

        private void BakePhysicalHeightField()
        {
            heightField?.Dispose();
            heightField = BakedHeightField.Bake(
                heightMap,
                bakedHeightResolution,
                MapSizeMeters,
                minimumHeightMeters,
                maximumHeightMeters,
                normalizeSourceRange,
                surfaceSmoothingSigmaMeters);
        }

        private void CreateCamera()
        {
            Camera existingCamera = Camera.main;
            if (existingCamera != null)
                existingCamera.gameObject.SetActive(false);

            var cameraObject = new GameObject("Map Test Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            mapCamera = cameraObject.AddComponent<Camera>();
            mapCamera.orthographic = true;
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = backgroundColor;
            mapCamera.orthographicSize = previewResolution / pixelsPerUnit * 0.58f;
        }

        private void CreateHeightVisualization()
        {
            generatedPreviewTexture = new Texture2D(previewResolution, previewResolution, TextureFormat.RGBA32, false, true)
            {
                name = "Generated Height Preview",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var colors = new Color[previewResolution * previewResolution];
            float onePixel = 1f / previewResolution;
            float heightRange = maximumHeightMeters - minimumHeightMeters;
            for (int y = 0; y < previewResolution; y++)
            {
                for (int x = 0; x < previewResolution; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / previewResolution, (y + 0.5f) / previewResolution);
                    float height = SampleHeight(uv);
                    float normalized = Mathf.InverseLerp(minimumHeightMeters, maximumHeightMeters, height);
                    float heightRight = SampleHeight(uv + Vector2.right * onePixel);
                    float heightUp = SampleHeight(uv + Vector2.up * onePixel);
                    float lighting = Mathf.Clamp((heightRight - heightUp) / Mathf.Max(1f, heightRange) * 9f, -0.22f, 0.22f);
                    Color color = EvaluateHeightColor(normalized) * (1f + lighting);

                    color.a = 1f;
                    colors[y * previewResolution + x] = color;
                }
            }

            generatedPreviewTexture.SetPixels(colors);
            generatedPreviewTexture.Apply(false, false);
            generatedMapSprite = Sprite.Create(
                generatedPreviewTexture,
                new Rect(0f, 0f, generatedPreviewTexture.width, generatedPreviewTexture.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
            generatedMapSprite.name = "Height Map Visualization";
            var mapObject = new GameObject("2D Height Map");
            mapRenderer = mapObject.AddComponent<SpriteRenderer>();
            mapRenderer.sprite = generatedMapSprite;
            CreateDynamicContourMaterial();
        }

        private void CreateDynamicContourMaterial()
        {
            Shader shader = Shader.Find("AnimalGame/Dynamic Height Contours");
            if (shader == null)
            {
                Debug.LogError("Missing shader: AnimalGame/Dynamic Height Contours");
                return;
            }

            contourMaterial = new Material(shader) { name = "Runtime Dynamic Contour Material" };
            contourMaterial.SetTexture("_HeightTex", heightField.SurfaceTexture);
            contourMaterial.SetFloat("_MinimumHeight", minimumHeightMeters);
            contourMaterial.SetFloat("_MaximumHeight", maximumHeightMeters);
            contourMaterial.SetFloat("_ContourInterval", contourIntervalMeters);
            contourMaterial.SetColor("_ContourColor", contourColor);
            RefreshContourMaterialSettings();
            mapRenderer.material = contourMaterial;
        }

        private void HandleCameraPreCull(Camera cameraToRender)
        {
            UpdateVisibleRangeOncePerFrame(cameraToRender);
        }

        private void HandleBeginCameraRendering(
            ScriptableRenderContext context,
            Camera cameraToRender)
        {
            UpdateVisibleRangeOncePerFrame(cameraToRender);
        }

        private void UpdateVisibleRangeOncePerFrame(Camera cameraToRender)
        {
            if (cameraToRender != mapCamera || lastViewportUpdateFrame == Time.frameCount)
                return;

            lastViewportUpdateFrame = Time.frameCount;
            UpdateVisibleContourRange(cameraToRender);
        }

        private void UpdateVisibleContourRange(Camera cameraToSample)
        {
            if (cameraToSample == null || contourMaterial == null || !HasGeneratedMap)
                return;

            if (!TryFindVisibleTerrainRange(
                    cameraToSample,
                    out float minimumTerrain,
                    out float maximumTerrain))
                return;

            float interval = Mathf.Max(0.0001f, contourIntervalMeters);
            int lowestIndex = Mathf.CeilToInt(
                (minimumTerrain - minimumHeightMeters) / interval - 0.0001f);
            int highestIndex = Mathf.FloorToInt(
                (maximumTerrain - minimumHeightMeters) / interval + 0.0001f);
            int maximumMapIndex = Mathf.FloorToInt(
                (maximumHeightMeters - minimumHeightMeters) / interval + 0.0001f);

            lowestIndex = Mathf.Clamp(lowestIndex, 0, maximumMapIndex);
            highestIndex = Mathf.Clamp(highestIndex, 0, maximumMapIndex);
            if (lowestIndex > highestIndex)
            {
                int nearestIndex = Mathf.Clamp(
                    Mathf.RoundToInt(
                        ((minimumTerrain + maximumTerrain) * 0.5f - minimumHeightMeters) / interval),
                    0,
                    maximumMapIndex);
                lowestIndex = nearestIndex;
                highestIndex = nearestIndex;
            }

            VisibleMinimumContourHeight = minimumHeightMeters + lowestIndex * interval;
            VisibleMaximumContourHeight = minimumHeightMeters + highestIndex * interval;

            RefreshContourMaterialSettings();
            contourMaterial.SetFloat("_VisibleMinimumHeight", VisibleMinimumContourHeight);
            contourMaterial.SetFloat("_VisibleMaximumHeight", VisibleMaximumContourHeight);
        }

        private bool TryFindVisibleTerrainRange(
            Camera cameraToSample,
            out float minimumTerrain,
            out float maximumTerrain)
        {
            minimumTerrain = float.PositiveInfinity;
            maximumTerrain = float.NegativeInfinity;
            Bounds bounds = WorldBounds;
            int verticalSamples = Mathf.Max(2, viewportHeightSamples);
            int horizontalSamples = Mathf.Max(
                2,
                Mathf.CeilToInt(verticalSamples * Mathf.Max(0.1f, cameraToSample.aspect)));
            int validSamples = 0;

            for (int y = 0; y < verticalSamples; y++)
            {
                float viewportY = y / (float)(verticalSamples - 1);
                for (int x = 0; x < horizontalSamples; x++)
                {
                    float viewportX = x / (float)(horizontalSamples - 1);
                    if (!TryProjectViewportPointToMapPlane(
                            cameraToSample,
                            new Vector2(viewportX, viewportY),
                            mapRenderer.transform.position.z,
                            out Vector3 world))
                    {
                        continue;
                    }

                    if (world.x < bounds.min.x || world.x > bounds.max.x
                        || world.y < bounds.min.y || world.y > bounds.max.y)
                    {
                        continue;
                    }

                    Vector2 uv = new Vector2(
                        Mathf.InverseLerp(bounds.min.x, bounds.max.x, world.x),
                        Mathf.InverseLerp(bounds.min.y, bounds.max.y, world.y));
                    float height = SampleHeight(uv);
                    minimumTerrain = Mathf.Min(minimumTerrain, height);
                    maximumTerrain = Mathf.Max(maximumTerrain, height);
                    validSamples++;
                }
            }

            return validSamples > 0;
        }

        private static bool TryProjectViewportPointToMapPlane(
            Camera cameraToSample,
            Vector2 viewportPoint,
            float mapPlaneZ,
            out Vector3 worldPoint)
        {
            Ray ray = cameraToSample.ViewportPointToRay(
                new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            return TryProjectRayToMapPlane(ray, mapPlaneZ, out worldPoint);
        }

        private static bool TryProjectRayToMapPlane(
            Ray ray,
            float mapPlaneZ,
            out Vector3 worldPoint)
        {
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

        private void RefreshContourMaterialSettings()
        {
            if (contourMaterial == null)
                return;

            contourMaterial.SetFloat("_MinimumLineWidth", minimumContourWidth);
            contourMaterial.SetFloat("_MaximumLineWidth", maximumContourWidth);
            contourMaterial.SetFloat("_MaximumCoverage", maximumContourCoverage);
            contourMaterial.SetFloat("_EdgeSoftness", contourEdgeSoftness);
            contourMaterial.SetFloat("_MinimumOpacity", LowestVisibleContourOpacity);
            contourMaterial.SetFloat("_MaximumOpacity", HighestVisibleContourOpacity);
        }

        private void CreateCrosshair()
        {
            var crosshairObject = new GameObject("Height Probe Crosshair");
            crosshair = crosshairObject.AddComponent<LineRenderer>();
            crosshair.useWorldSpace = true;
            crosshair.positionCount = 4;
            crosshair.startWidth = 0.025f;
            crosshair.endWidth = 0.025f;
            crosshair.startColor = probeColor;
            crosshair.endColor = crosshair.startColor;
            crosshair.material = new Material(Shader.Find("Sprites/Default"));
            crosshair.sortingOrder = 10;
            crosshair.enabled = false;
        }

        private void UpdateHeightProbe()
        {
            Ray cursorRay = mapCamera.ScreenPointToRay(Input.mousePosition);
            if (!TryProjectRayToMapPlane(
                    cursorRay,
                    mapRenderer.transform.position.z,
                    out Vector3 world))
            {
                cursorInsideMap = false;
                crosshair.enabled = false;
                return;
            }

            Bounds bounds = mapRenderer.bounds;
            cursorInsideMap = world.x >= bounds.min.x && world.x <= bounds.max.x
                              && world.y >= bounds.min.y && world.y <= bounds.max.y;
            crosshair.enabled = cursorInsideMap;
            if (!cursorInsideMap)
                return;

            cursorUv = new Vector2(
                Mathf.InverseLerp(bounds.min.x, bounds.max.x, world.x),
                Mathf.InverseLerp(bounds.min.y, bounds.max.y, world.y));
            cursorRawGray = heightMap.GetPixelBilinear(cursorUv.x, cursorUv.y).grayscale;
            cursorHeight = SampleHeight(cursorUv);

            const float arm = 0.16f;
            const float gap = 0.04f;
            crosshair.SetPosition(0, new Vector3(world.x - arm, world.y));
            crosshair.SetPosition(1, new Vector3(world.x - gap, world.y));
            crosshair.SetPosition(2, new Vector3(world.x + gap, world.y));
            crosshair.SetPosition(3, new Vector3(world.x + arm, world.y));
        }

        private void OnGUI()
        {
            GUIStyle title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(0.9f, 0.97f, 1f);
            GUIStyle data = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            data.normal.textColor = new Color(0.75f, 0.9f, 0.93f);

            GUI.Box(new Rect(18f, 18f, 330f, cursorInsideMap ? 150f : 92f), GUIContent.none);
            GUI.Label(new Rect(34f, 28f, 290f, 28f), "MAP HEIGHT PROBE", title);
            if (!cursorInsideMap)
            {
                GUI.Label(new Rect(34f, 61f, 290f, 24f), "Move the mouse over the map", data);
                return;
            }

            float mapX = cursorUv.x * mapWidthMeters;
            float mapY = cursorUv.y * mapHeightMeters;
            GUI.Label(new Rect(34f, 61f, 290f, 24f), $"MAP POSITION   X {mapX:F1}m   Y {mapY:F1}m", data);
            GUI.Label(new Rect(34f, 86f, 290f, 24f), $"SOURCE GRAY    {cursorRawGray:F3}", data);
            GUI.Label(new Rect(34f, 111f, 290f, 30f), $"HEIGHT         {cursorHeight:F1}m", data);
        }

        private Color EvaluateHeightColor(float height)
        {
            return height < 0.55f
                ? Color.Lerp(lowHeightColor, middleHeightColor, height / 0.55f)
                : Color.Lerp(middleHeightColor, highHeightColor, (height - 0.55f) / 0.45f);
        }

        private void OnDestroy()
        {
            heightField?.Dispose();
            heightField = null;

            if (contourMaterial != null)
                Destroy(contourMaterial);

            if (generatedMapSprite != null)
                Destroy(generatedMapSprite);

            if (generatedPreviewTexture != null)
                Destroy(generatedPreviewTexture);
        }

        private void OnValidate()
        {
            mapWidthMeters = Mathf.Max(1f, mapWidthMeters);
            mapHeightMeters = Mathf.Max(1f, mapHeightMeters);
            maximumHeightMeters = Mathf.Max(minimumHeightMeters + 0.01f, maximumHeightMeters);
            bakedHeightResolution = Mathf.Clamp(bakedHeightResolution, 128, 2048);
            surfaceSmoothingSigmaMeters = Mathf.Max(0f, surfaceSmoothingSigmaMeters);
            previewResolution = Mathf.Clamp(previewResolution, 128, 8000);
            contourIntervalMeters = Mathf.Max(1f, contourIntervalMeters);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
        }
    }
}
