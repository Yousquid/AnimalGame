using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Persistent authoring data for one fixed height-map level. Runtime systems
    /// continue to query MapTestSceneController, so assigning this asset changes
    /// the source of the map without changing traversal, scan, or tumble APIs.
    /// </summary>
    [CreateAssetMenu(
        fileName = "HeightMapLevel",
        menuName = "Animal Game/Height Map Level")]
    public sealed class HeightMapLevelAsset : ScriptableObject
    {
        [Header("Height Source")]
        [SerializeField] private Texture2D heightMap;
        [SerializeField, Min(1f)] private float mapWidthMeters = 250f;
        [SerializeField, Min(1f)] private float mapHeightMeters = 250f;
        [SerializeField] private float minimumHeightMeters;
        [SerializeField] private float maximumHeightMeters = 70f;

        [Header("Physical Height Field")]
        [SerializeField, Range(128, 2048)] private int bakedHeightResolution = 2048;
        [SerializeField] private bool normalizeSourceRange = true;
        [SerializeField, Min(0f)] private float surfaceSmoothingSigmaMeters = 0.75f;

        [Header("Map Presentation")]
        [SerializeField, Range(128, 8000)] private int previewResolution = 2160;
        [SerializeField, Min(1f)] private float contourIntervalMeters = 8f;
        [SerializeField, Min(1f)] private float pixelsPerUnit = 32f;
        [SerializeField] private Shader dynamicContourShader;
        [SerializeField, Range(0.1f, 10f)] private float minimumContourWidth = 0.7f;
        [SerializeField, Range(0.1f, 10f)] private float maximumContourWidth = 3.68f;
        [SerializeField, Range(0.1f, 0.7f)] private float maximumContourCoverage = 0.3f;
        [SerializeField, Range(0.1f, 1.5f)] private float contourEdgeSoftness = 0.4f;
        [SerializeField, Range(16, 128)] private int viewportHeightSamples = 64;

        [Header("Color Palette")]
        [SerializeField] private Color backgroundColor =
            new Color(0.008f, 0.012f, 0.017f, 1f);
        [SerializeField] private Color lowHeightColor = Color.black;
        [SerializeField] private Color middleHeightColor = Color.black;
        [SerializeField] private Color highHeightColor = Color.black;
        [SerializeField] private Color contourColor =
            new Color(0.92f, 0.97f, 1f, 1f);

        public Texture2D HeightMap => heightMap;
        public Vector2 MapSizeMeters => new Vector2(mapWidthMeters, mapHeightMeters);
        public float MinimumHeightMeters => minimumHeightMeters;
        public float MaximumHeightMeters => maximumHeightMeters;
        public int BakedHeightResolution => bakedHeightResolution;
        public bool NormalizeSourceRange => normalizeSourceRange;
        public float SurfaceSmoothingSigmaMeters => surfaceSmoothingSigmaMeters;
        public int PreviewResolution => previewResolution;
        public float ContourIntervalMeters => contourIntervalMeters;
        public float PixelsPerUnit => pixelsPerUnit;
        public Shader DynamicContourShader => dynamicContourShader;
        public float MinimumContourWidth => minimumContourWidth;
        public float MaximumContourWidth => maximumContourWidth;
        public float MaximumContourCoverage => maximumContourCoverage;
        public float ContourEdgeSoftness => contourEdgeSoftness;
        public int ViewportHeightSamples => viewportHeightSamples;
        public Color BackgroundColor => backgroundColor;
        public Color LowHeightColor => lowHeightColor;
        public Color MiddleHeightColor => middleHeightColor;
        public Color HighHeightColor => highHeightColor;
        public Color ContourColor => contourColor;
        public bool IsValid => heightMap != null
                               && mapWidthMeters > 0f
                               && mapHeightMeters > 0f
                               && maximumHeightMeters > minimumHeightMeters;

        public int ConfigurationHash
        {
            get
            {
                unchecked
                {
                    int hash = heightMap != null ? heightMap.GetInstanceID() : 0;
                    hash = hash * 397 ^ mapWidthMeters.GetHashCode();
                    hash = hash * 397 ^ mapHeightMeters.GetHashCode();
                    hash = hash * 397 ^ minimumHeightMeters.GetHashCode();
                    hash = hash * 397 ^ maximumHeightMeters.GetHashCode();
                    hash = hash * 397 ^ bakedHeightResolution;
                    hash = hash * 397 ^ normalizeSourceRange.GetHashCode();
                    hash = hash * 397 ^ surfaceSmoothingSigmaMeters.GetHashCode();
                    hash = hash * 397 ^ previewResolution;
                    hash = hash * 397 ^ contourIntervalMeters.GetHashCode();
                    hash = hash * 397 ^ pixelsPerUnit.GetHashCode();
                    hash = hash * 397 ^ (dynamicContourShader != null
                        ? dynamicContourShader.GetInstanceID()
                        : 0);
                    hash = hash * 397 ^ minimumContourWidth.GetHashCode();
                    hash = hash * 397 ^ maximumContourWidth.GetHashCode();
                    hash = hash * 397 ^ maximumContourCoverage.GetHashCode();
                    hash = hash * 397 ^ contourEdgeSoftness.GetHashCode();
                    hash = hash * 397 ^ viewportHeightSamples;
                    hash = hash * 397 ^ backgroundColor.GetHashCode();
                    hash = hash * 397 ^ lowHeightColor.GetHashCode();
                    hash = hash * 397 ^ middleHeightColor.GetHashCode();
                    hash = hash * 397 ^ highHeightColor.GetHashCode();
                    hash = hash * 397 ^ contourColor.GetHashCode();
                    return hash;
                }
            }
        }

        private void OnValidate()
        {
            mapWidthMeters = Mathf.Max(1f, mapWidthMeters);
            mapHeightMeters = Mathf.Max(1f, mapHeightMeters);
            maximumHeightMeters = Mathf.Max(
                minimumHeightMeters + 0.01f,
                maximumHeightMeters);
            bakedHeightResolution = Mathf.Clamp(bakedHeightResolution, 128, 2048);
            surfaceSmoothingSigmaMeters = Mathf.Max(0f, surfaceSmoothingSigmaMeters);
            previewResolution = Mathf.Clamp(previewResolution, 128, 8000);
            contourIntervalMeters = Mathf.Max(1f, contourIntervalMeters);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            minimumContourWidth = Mathf.Clamp(minimumContourWidth, 0.1f, 10f);
            maximumContourWidth = Mathf.Clamp(maximumContourWidth, 0.1f, 10f);
            maximumContourCoverage = Mathf.Clamp(maximumContourCoverage, 0.1f, 0.7f);
            contourEdgeSoftness = Mathf.Clamp(contourEdgeSoftness, 0.1f, 1.5f);
            viewportHeightSamples = Mathf.Clamp(viewportHeightSamples, 16, 128);
        }
    }
}
