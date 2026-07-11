using UnityEngine;

namespace AnimalGame.MapTest
{
    public sealed class MapTestSceneController : MonoBehaviour
    {
        [Header("Height Source")]
        [SerializeField] private Texture2D heightMap;
        [SerializeField, Min(1f)] private float mapWidthMeters = 1000f;
        [SerializeField, Min(1f)] private float mapHeightMeters = 1000f;
        [SerializeField] private float minimumHeightMeters;
        [SerializeField] private float maximumHeightMeters = 200f;

        [Header("Visualization")]
        [SerializeField, Range(128, 1024)] private int previewResolution = 512;
        [SerializeField, Min(1f)] private float contourIntervalMeters = 10f;
        [SerializeField, Min(1f)] private float pixelsPerUnit = 64f;

        private Camera mapCamera;
        private SpriteRenderer mapRenderer;
        private LineRenderer crosshair;
        private float sourceMinimum;
        private float sourceMaximum = 1f;
        private bool cursorInsideMap;
        private Vector2 cursorUv;
        private float cursorRawGray;
        private float cursorHeight;

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
            mapCamera.backgroundColor = new Color(0.008f, 0.012f, 0.017f);
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

                    int contour = Mathf.FloorToInt((height - minimumHeightMeters) / contourIntervalMeters);
                    int neighborContour = Mathf.FloorToInt((heightRight - minimumHeightMeters) / contourIntervalMeters);
                    int upperContour = Mathf.FloorToInt((heightUp - minimumHeightMeters) / contourIntervalMeters);
                    if (contour != neighborContour || contour != upperContour)
                        color = Color.Lerp(color, new Color(0.92f, 0.97f, 1f), 0.76f);

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
        }

        private void CreateCrosshair()
        {
            var crosshairObject = new GameObject("Height Probe Crosshair");
            crosshair = crosshairObject.AddComponent<LineRenderer>();
            crosshair.useWorldSpace = true;
            crosshair.positionCount = 4;
            crosshair.startWidth = 0.025f;
            crosshair.endWidth = 0.025f;
            crosshair.startColor = new Color(1f, 0.83f, 0.27f);
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

        private static Color EvaluateHeightColor(float height)
        {
            Color low = new Color(0.025f, 0.09f, 0.12f);
            Color middle = new Color(0.08f, 0.42f, 0.42f);
            Color high = new Color(0.72f, 0.82f, 0.67f);
            return height < 0.55f
                ? Color.Lerp(low, middle, height / 0.55f)
                : Color.Lerp(middle, high, (height - 0.55f) / 0.45f);
        }
    }
}
