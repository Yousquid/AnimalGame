using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.MapTest
{
    public sealed class MapTestSceneController : MonoBehaviour
    {
        private const float LowestVisibleContourOpacity = 0.15f;
        private const float HighestVisibleContourOpacity = 1f;

        [Header("Height Source")]
        [SerializeField] private Texture2D heightMap;
        [SerializeField, Min(1f)] private float mapWidthMeters = 1000f;
        [SerializeField, Min(1f)] private float mapHeightMeters = 1000f;
        [SerializeField] private float minimumHeightMeters;
        [SerializeField] private float maximumHeightMeters = 200f;

        [Header("Visualization")]
        [SerializeField, Range(128, 8000)] private int previewResolution = 512;
        [SerializeField, Min(1f)] private float contourIntervalMeters = 10f;
        [SerializeField, Min(1f)] private float pixelsPerUnit = 16f;

        [Header("Viewport Contours")]
        [Tooltip("Width of the lowest contour currently visible in the camera.")]
        [SerializeField, Range(0.1f, 10f)] private float minimumContourWidth = 0.75f;

        [Tooltip("Width of the highest contour currently visible in the camera.")]
        [SerializeField, Range(0.1f, 10f)] private float maximumContourWidth = 3f;

        [Tooltip("Smooths small 8-bit height steps before drawing contours. Higher values remove more echo lines.")]
        [SerializeField, Range(0f, 1f)] private float contourHeightSmoothing = 0.65f;

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

        [SerializeField] private Color contourColor = new Color(0.92f, 0.97f, 1f);
        [SerializeField] private Color probeColor = new Color(1f, 0.83f, 0.27f);

        private Camera mapCamera;
        private SpriteRenderer mapRenderer;
        private LineRenderer crosshair;
        private float sourceMinimum;
        private float sourceMaximum = 1f;
        private bool cursorInsideMap;
        private Vector2 cursorUv;
        private float cursorRawGray;
        private float cursorHeight;
        private Material contourMaterial;
        private int lastViewportUpdateFrame = -1;

        public float VisibleMinimumContourHeight { get; private set; }
        public float VisibleMaximumContourHeight { get; private set; }

        public bool HasGeneratedMap => mapRenderer != null;

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

            FindSourceRange();
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
            float gray = heightMap.GetPixelBilinear(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y)).grayscale;
            float normalized = Mathf.InverseLerp(sourceMinimum, sourceMaximum, gray);
            return Mathf.Lerp(minimumHeightMeters, maximumHeightMeters, normalized);
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

        private void FindSourceRange()
        {
            Color[] pixels = heightMap.GetPixels();
            sourceMinimum = 1f;
            sourceMaximum = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                float gray = pixels[i].grayscale;
                sourceMinimum = Mathf.Min(sourceMinimum, gray);
                sourceMaximum = Mathf.Max(sourceMaximum, gray);
            }
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
            var preview = new Texture2D(previewResolution, previewResolution, TextureFormat.RGBA32, false, true)
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

            preview.SetPixels(colors);
            preview.Apply(false, false);
            Sprite sprite = Sprite.Create(preview, new Rect(0f, 0f, preview.width, preview.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            sprite.name = "Height Map Visualization";
            var mapObject = new GameObject("2D Height Map");
            mapRenderer = mapObject.AddComponent<SpriteRenderer>();
            mapRenderer.sprite = sprite;
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
            contourMaterial.SetTexture("_HeightTex", heightMap);
            contourMaterial.SetFloat("_SourceMinimum", sourceMinimum);
            contourMaterial.SetFloat("_SourceMaximum", sourceMaximum);
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

            int heightMipLevel = CalculateHeightMipLevel(cameraToSample);
            if (!TryFindVisibleTerrainRange(
                    cameraToSample,
                    heightMipLevel,
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
            contourMaterial.SetFloat("_HeightMipLevel", heightMipLevel);
            contourMaterial.SetFloat("_VisibleMinimumHeight", VisibleMinimumContourHeight);
            contourMaterial.SetFloat("_VisibleMaximumHeight", VisibleMaximumContourHeight);
        }

        private bool TryFindVisibleTerrainRange(
            Camera cameraToSample,
            int heightMipLevel,
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
            float mapPlaneDistance = Mathf.Abs(
                mapRenderer.transform.position.z - cameraToSample.transform.position.z);
            int validSamples = 0;

            for (int y = 0; y < verticalSamples; y++)
            {
                float viewportY = y / (float)(verticalSamples - 1);
                for (int x = 0; x < horizontalSamples; x++)
                {
                    float viewportX = x / (float)(horizontalSamples - 1);
                    Vector3 world = cameraToSample.ViewportToWorldPoint(
                        new Vector3(viewportX, viewportY, mapPlaneDistance));

                    if (world.x < bounds.min.x || world.x > bounds.max.x
                        || world.y < bounds.min.y || world.y > bounds.max.y)
                    {
                        continue;
                    }

                    Vector2 uv = new Vector2(
                        Mathf.InverseLerp(bounds.min.x, bounds.max.x, world.x),
                        Mathf.InverseLerp(bounds.min.y, bounds.max.y, world.y));
                    float height = SampleContourHeight(uv, heightMipLevel);
                    minimumTerrain = Mathf.Min(minimumTerrain, height);
                    maximumTerrain = Mathf.Max(maximumTerrain, height);
                    validSamples++;
                }
            }

            return validSamples > 0;
        }

        private int CalculateHeightMipLevel(Camera cameraToSample)
        {
            if (!cameraToSample.orthographic || heightMap.mipmapCount <= 1 || !HasGeneratedMap)
                return 0;

            Bounds bounds = WorldBounds;
            float worldUnitsPerScreenPixel =
                cameraToSample.orthographicSize * 2f / Mathf.Max(1, cameraToSample.pixelHeight);
            float texelsPerWorldUnit = Mathf.Max(
                heightMap.width / Mathf.Max(0.0001f, bounds.size.x),
                heightMap.height / Mathf.Max(0.0001f, bounds.size.y));
            float texelsPerScreenPixel = Mathf.Max(
                1f,
                worldUnitsPerScreenPixel * texelsPerWorldUnit);

            return Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Log(texelsPerScreenPixel, 2f)),
                0,
                heightMap.mipmapCount - 1);
        }

        private float SampleContourHeight(Vector2 uv, int mipLevel)
        {
            float mipScale = Mathf.Pow(2f, mipLevel);
            Vector2 texel = new Vector2(
                mipScale / heightMap.width,
                mipScale / heightMap.height);
            float center = heightMap.GetPixelBilinear(uv.x, uv.y, mipLevel).grayscale;
            float crossAverage = (
                heightMap.GetPixelBilinear(uv.x + texel.x, uv.y, mipLevel).grayscale
                + heightMap.GetPixelBilinear(uv.x - texel.x, uv.y, mipLevel).grayscale
                + heightMap.GetPixelBilinear(uv.x, uv.y + texel.y, mipLevel).grayscale
                + heightMap.GetPixelBilinear(uv.x, uv.y - texel.y, mipLevel).grayscale) * 0.25f;
            float blurred = Mathf.Lerp(center, crossAverage, 0.5f);
            float gray = Mathf.Lerp(center, blurred, contourHeightSmoothing);
            float normalized = Mathf.InverseLerp(sourceMinimum, sourceMaximum, gray);
            return Mathf.Lerp(minimumHeightMeters, maximumHeightMeters, normalized);
        }

        private void RefreshContourMaterialSettings()
        {
            if (contourMaterial == null)
                return;

            contourMaterial.SetFloat("_MinimumLineWidth", minimumContourWidth);
            contourMaterial.SetFloat("_MaximumLineWidth", maximumContourWidth);
            contourMaterial.SetFloat("_HeightSmoothing", contourHeightSmoothing);
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
            Vector3 world = mapCamera.ScreenToWorldPoint(Input.mousePosition);
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
    }
}
