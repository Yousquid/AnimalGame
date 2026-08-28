using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.MapTest
{
#if UNITY_EDITOR
    public enum StaticWaterDepthPaintMode
    {
        Set = 0,
        Add = 1,
        Subtract = 2,
        Smooth = 3,
        Erase = 4
    }

    public enum TerrainSurfaceTransitionMode
    {
        Hard = 0,
        Alpha = 1,
        Noisy = 2,
        Hybrid = 3
    }

    [Serializable]
    public sealed class TerrainSurfaceDefinition
    {
        [SerializeField, Range(1, 255)] private int terrainId = 1;
        [SerializeField] private string displayName = "Terrain";
        [SerializeField] private Texture2D patternTexture;
        [SerializeField] private Color tint = Color.white;
        [SerializeField, Range(0f, 1f)] private float opacity = 0.35f;
        [SerializeField, Min(0.1f)] private float tileSizeMeters = 8f;
        [SerializeField] private TerrainSurfaceTransitionMode transitionMode =
            TerrainSurfaceTransitionMode.Hybrid;
        [SerializeField, Range(0.25f, 2f)]
        private float transitionWidthMultiplier = 1f;
        [SerializeField, Range(0f, 2f)]
        private float noiseStrengthMultiplier = 1f;

        public int TerrainId => terrainId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? $"Terrain {terrainId}"
            : displayName;
        public Texture2D PatternTexture => patternTexture;
        public Color Tint => tint;
        public float Opacity => opacity;
        public float TileSizeMeters => tileSizeMeters;
        public TerrainSurfaceTransitionMode TransitionMode => transitionMode;
        public float TransitionWidthMultiplier => transitionWidthMultiplier;
        public float NoiseStrengthMultiplier => noiseStrengthMultiplier;
        public bool IsUsable => terrainId > 0 && patternTexture != null;

        public TerrainSurfaceDefinition()
        {
        }

        public TerrainSurfaceDefinition(
            int id,
            string name,
            Texture2D texture,
            Color surfaceTint,
            float surfaceOpacity,
            float surfaceTileSizeMeters)
        {
            terrainId = Mathf.Clamp(id, 1, 255);
            displayName = name;
            patternTexture = texture;
            tint = surfaceTint;
            opacity = Mathf.Clamp01(surfaceOpacity);
            tileSizeMeters = Mathf.Max(0.1f, surfaceTileSizeMeters);
            transitionMode = TerrainSurfaceTransitionMode.Hybrid;
            transitionWidthMultiplier = 1f;
            noiseStrengthMultiplier = 1f;
        }

        public void SetTerrainId(int id)
        {
            terrainId = Mathf.Clamp(id, 1, 255);
        }

        public void Validate()
        {
            terrainId = Mathf.Clamp(terrainId, 1, 255);
            opacity = Mathf.Clamp01(opacity);
            tileSizeMeters = Mathf.Max(0.1f, tileSizeMeters);
            transitionWidthMultiplier = Mathf.Clamp(
                transitionWidthMultiplier,
                0.25f,
                2f);
            noiseStrengthMultiplier = Mathf.Clamp(
                noiseStrengthMultiplier,
                0f,
                2f);
        }

        public int AppendConfigurationHash(int hash)
        {
            unchecked
            {
                hash = hash * 397 ^ terrainId;
                hash = hash * 397 ^ (displayName != null
                    ? displayName.GetHashCode()
                    : 0);
                hash = hash * 397 ^ (patternTexture != null
                    ? patternTexture.GetInstanceID()
                    : 0);
                hash = hash * 397 ^ tint.GetHashCode();
                hash = hash * 397 ^ opacity.GetHashCode();
                hash = hash * 397 ^ tileSizeMeters.GetHashCode();
                hash = hash * 397 ^ (int)transitionMode;
                hash = hash * 397 ^ transitionWidthMultiplier.GetHashCode();
                hash = hash * 397 ^ noiseStrengthMultiplier.GetHashCode();
                return hash;
            }
        }
    }
#endif

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
#if UNITY_EDITOR
        public const int CurrentSurfaceBakeGeneratorVersion = 10;
        public const int CurrentStaticWaterMaskBakeGeneratorVersion = 1;
#endif

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

        [Header("Playable Area")]
        [Tooltip("Treats near-black pixels connected to the height-map border as outside the playable map. Enclosed dark valleys remain playable.")]
        [SerializeField] private bool useHeightMapBorderMask;
        [Tooltip("Maximum source grayscale value considered part of the connected black border.")]
        [SerializeField, Range(0f, 0.25f)]
        private float heightMapBorderMaskThreshold = 0.01f;
        [Tooltip("Additional physical distance removed inside the detected silhouette, keeping the robot away from antialiased edge pixels.")]
        [SerializeField, Min(0f)] private float heightMapBorderInsetMeters;

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

        [Header("Static Water")]
        [Tooltip("Resolution of the persistent static-water depth map. A zero byte is dry land; non-zero values encode water depth.")]
        [SerializeField, Range(64, 1024)]
        private int staticWaterResolution = 512;

        [Tooltip("Deepest water value represented by the depth map, in logical map meters.")]
        [SerializeField, Min(0.1f)]
        private float maximumStaticWaterDepthMeters = 10f;

        [Tooltip("Water at or below this depth remains traversable. Deeper water is treated as a hard movement blocker.")]
        [SerializeField, Min(0f)]
        private float maximumPassableStaticWaterDepthMeters = 1.2f;

        [SerializeField, HideInInspector]
        private byte[] staticWaterDepthMap = Array.Empty<byte>();

        [Header("Static Water Visual")]
        [Tooltip("Pattern animated by the runtime map shader inside the authored water range.")]
        [SerializeField] private Texture2D staticWaterEditorTexture;

        [Tooltip("Editor-generated range/depth texture consumed directly by the runtime map shader.")]
        [SerializeField] private Texture2D bakedStaticWaterMask;

        [Tooltip("Map-space size of one animated water-pattern tile.")]
        [SerializeField, Min(0.1f)]
        private float staticWaterEditorTileSizeMeters = 8f;

        [Tooltip("Map-space movement velocity of the primary water-pattern layer, in meters per second.")]
        [SerializeField] private Vector2 staticWaterLayerOneSpeedMetersPerSecond =
            new Vector2(0.36f, 0.04f);

        [Tooltip("Map-space movement velocity of the secondary water-pattern layer, in meters per second.")]
        [SerializeField] private Vector2 staticWaterLayerTwoSpeedMetersPerSecond =
            new Vector2(-0.14f, 0.22f);

        [Tooltip("Tiling multiplier of the rotated secondary pattern layer.")]
        [SerializeField, Min(0.1f)]
        private float staticWaterLayerTwoScale = 1.35f;

        [Tooltip("Small UV distortion that makes the water lines bend like waves instead of sliding rigidly.")]
        [SerializeField, Range(0f, 0.2f)]
        private float staticWaterWaveDistortion = 0.03f;

        [Tooltip("Speed of the sinusoidal wave distortion.")]
        [SerializeField, Min(0f)] private float staticWaterWaveSpeed = 0.8f;

        [Tooltip("Map-space wavelength of the broad distortion.")]
        [SerializeField, Min(0.1f)]
        private float staticWaterWaveLengthMeters = 12f;

        [Tooltip("Animation-speed multiplier at the maximum authored depth.")]
        [SerializeField, Range(0f, 1f)]
        private float staticWaterDeepSpeedMultiplier = 0.65f;

        [SerializeField, HideInInspector]
        private int staticWaterMaskBakeRevision;

#if UNITY_EDITOR
        [Header("Static Water Scene Preview")]
        [Tooltip("Tint used only by the Scene-view static-water preview.")]
        [SerializeField] private Color staticWaterEditorTint =
            new Color(0.18f, 0.72f, 1f, 1f);

        [SerializeField, HideInInspector]
        private int staticWaterMaskBakeGeneratorVersion;
#endif

#if UNITY_EDITOR
        [Header("Terrain Surface Palette")]
        [Tooltip("Stable ID-based terrain definitions. IDs may range from 1 to 255; zero is reserved for no terrain texture.")]
        [SerializeField] private List<TerrainSurfaceDefinition> surfacePalette =
            new List<TerrainSurfaceDefinition>();

        [Header("Baked Terrain Transitions")]
        [Tooltip("Total map-space width of a transition between two terrain IDs.")]
        [SerializeField, Min(0.1f)]
        private float surfaceTransitionWidthMeters = 3f;

        [Tooltip("Width around the noisy boundary that receives additional premultiplied-alpha smoothing.")]
        [SerializeField, Min(0f)]
        private float surfaceAlphaCoreWidthMeters = 0.8f;

        [Tooltip("How far the continuous premultiplied-alpha fade expands from the narrow core toward the full transition width.")]
        [SerializeField, Range(0f, 1f)]
        private float surfaceAlphaBlendShare = 0.6f;

        [Tooltip("Map-space size of the broad noise that makes biome boundaries irregular.")]
        [SerializeField, Min(0.1f)]
        private float surfaceBoundaryNoiseScaleMeters = 5f;

        [Tooltip("Maximum map-space distance by which broad noise may push the boundary.")]
        [SerializeField, Min(0f)]
        private float surfaceBoundaryNoiseAmplitudeMeters = 0.8f;

        [Tooltip("Map-space scale of the fine noise used to roughen only the terrain boundary. It never cuts up the pattern itself.")]
        [SerializeField, Min(0.05f)]
        private float surfaceScatterCellSizeMeters = 0.5f;

        [Tooltip("Strength of the fine boundary-detail noise. The texture on either side remains continuous.")]
        [SerializeField, Range(0f, 1f)]
        private float surfaceScatterStrength = 0.35f;

        [Tooltip("Deterministic map-space seed. Keeping this fixed makes every rebake reproduce exactly the same boundary.")]
        [SerializeField] private int surfaceNoiseSeed = 1337;

        [Tooltip("Resolution of the permanent RGBA visual generated by the Editor. Runtime only reads this finished texture.")]
        [SerializeField, Range(256, 4096)] private int surfaceBakeResolution = 2048;

        [Tooltip("Resolution of the persistent terrain-ID authoring map. Each byte stores zero or one palette ID and is not sampled at runtime.")]
        [SerializeField, Range(64, 1024)] private int surfaceMaskResolution = 512;

        [Tooltip("How strongly the Editor normalizes each pattern's non-transparent alpha values before baking. Zero preserves source alpha; one makes differently authored textures use a more consistent visual alpha range. Fully transparent pixels stay transparent.")]
        [SerializeField, Range(0f, 1f)]
        private float surfacePatternAlphaNormalizationStrength = 0.75f;

        [Header("Static Closed-Contour Alpha")]
        [Tooltip("Permanently bakes stronger terrain texture alpha near contour-region boundaries and weaker alpha toward each region's centre. The map's four straight sides can close contour lines that reach an edge. This is Editor-only and never reacts to the player at runtime.")]
        [SerializeField] private bool surfaceClosedContourAlphaEnabled = true;

        [Tooltip("Resolution used only while the Editor identifies closed contours and calculates distance from their boundaries.")]
        [SerializeField, Range(128, 1024)]
        private int surfaceClosedContourAlphaResolution = 512;

        [Tooltip("Multiplier applied to terrain texture alpha at a closed contour boundary.")]
        [SerializeField, Range(0f, 2f)]
        private float surfaceClosedContourEdgeAlphaMultiplier = 0.3466667f;

        [Tooltip("Physical distance from a closed contour boundary that keeps the full boundary alpha multiplier before fading begins.")]
        [SerializeField, Min(0f)]
        private float surfaceClosedContourEdgeHoldDistanceMeters = 1.5f;

        [Tooltip("Physical distance from a closed contour boundary where terrain texture alpha reaches the centre multiplier. Larger regions remain at the centre multiplier beyond this distance.")]
        [SerializeField, Min(0.1f)]
        private float surfaceClosedContourFadeDistanceMeters = 10f;

        [Tooltip("Multiplier applied to terrain texture alpha at the deepest point inside a closed contour region.")]
        [SerializeField, Range(0f, 2f)]
        private float surfaceClosedContourCenterAlphaMultiplier = 0.2f;

        [Tooltip("Strength of the permanent edge-to-interior fade. Values above one make the texture disappear faster after leaving the boundary.")]
        [SerializeField, Range(0.1f, 8f)]
        private float surfaceClosedContourDistanceCurve = 2f;

        [Tooltip("Closed contour components smaller than this physical area are ignored to prevent tiny height-map specks from changing alpha.")]
        [SerializeField, Min(0f)]
        private float surfaceClosedContourMinimumAreaSquareMeters = 4f;

        [Tooltip("Alpha multiplier for terrain that is not inside any closed contour region.")]
        [SerializeField, Range(0f, 2f)]
        private float surfaceOutsideClosedContourAlphaMultiplier;

        // Kept under the original serialized name so every area painted by the
        // single-surface prototype migrates without rewriting the map by hand.
        [SerializeField, HideInInspector] private byte[] surfaceMask = Array.Empty<byte>();
        [SerializeField, HideInInspector] private int surfaceDataVersion;

        [Header("Legacy Single-Surface Migration")]
        [SerializeField, HideInInspector] private Texture2D surfacePatternTexture;
        [SerializeField, HideInInspector] private Color surfaceTint = Color.white;
        [SerializeField, HideInInspector, Range(0f, 1f)]
        private float surfaceOpacity = 0.35f;
        [SerializeField, HideInInspector, Min(0.1f)]
        private float surfaceTileSizeMeters = 8f;
#endif

        [Header("Baked Terrain Surface Runtime")]
        [Tooltip("Permanent Editor-baked terrain visual. The runtime never composes or modifies this texture.")]
        [SerializeField] private Texture2D bakedSurfaceVisual;

        [Tooltip("Softness of the screen-space UI reveal boundary in pixels. This affects only the circular display cutoff, not painted terrain boundaries.")]
        [SerializeField, Min(0f)] private float surfaceRevealEdgePixels = 4f;

        [SerializeField, HideInInspector] private int surfaceBakeRevision;
#if UNITY_EDITOR
        [SerializeField, HideInInspector]
        private int surfaceBakeGeneratorVersion;
#endif

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
        public bool UseHeightMapBorderMask => useHeightMapBorderMask;
        public float HeightMapBorderMaskThreshold =>
            heightMapBorderMaskThreshold;
        public float HeightMapBorderInsetMeters => heightMapBorderInsetMeters;
        public int PreviewResolution => previewResolution;
        public float ContourIntervalMeters => contourIntervalMeters;
        public float PixelsPerUnit => pixelsPerUnit;
        public Shader DynamicContourShader => dynamicContourShader;
        public float MinimumContourWidth => minimumContourWidth;
        public float MaximumContourWidth => maximumContourWidth;
        public float MaximumContourCoverage => maximumContourCoverage;
        public float ContourEdgeSoftness => contourEdgeSoftness;
        public int ViewportHeightSamples => viewportHeightSamples;
        public int StaticWaterResolution => staticWaterResolution;
        public float MaximumStaticWaterDepthMeters =>
            maximumStaticWaterDepthMeters;
        public float MaximumPassableStaticWaterDepthMeters =>
            maximumPassableStaticWaterDepthMeters;
        public Texture2D StaticWaterTexture => staticWaterEditorTexture;
        public Texture2D BakedStaticWaterMask => bakedStaticWaterMask;
        public float StaticWaterTileSizeMeters =>
            staticWaterEditorTileSizeMeters;
        public Vector2 StaticWaterLayerOneSpeedMetersPerSecond =>
            staticWaterLayerOneSpeedMetersPerSecond;
        public Vector2 StaticWaterLayerTwoSpeedMetersPerSecond =>
            staticWaterLayerTwoSpeedMetersPerSecond;
        public float StaticWaterLayerTwoScale => staticWaterLayerTwoScale;
        public float StaticWaterWaveDistortion => staticWaterWaveDistortion;
        public float StaticWaterWaveSpeed => staticWaterWaveSpeed;
        public float StaticWaterWaveLengthMeters =>
            staticWaterWaveLengthMeters;
        public float StaticWaterDeepSpeedMultiplier =>
            staticWaterDeepSpeedMultiplier;
#if UNITY_EDITOR
        public Color StaticWaterEditorTint => staticWaterEditorTint;
#endif
        public Texture2D BakedSurfaceVisual => bakedSurfaceVisual;
        public float SurfaceRevealEdgePixels => surfaceRevealEdgePixels;
        public int SurfacePresentationHash
        {
            get
            {
                unchecked
                {
                    int hash = bakedSurfaceVisual != null
                        ? bakedSurfaceVisual.GetInstanceID()
                        : 0;
                    hash = hash * 397 ^ surfaceRevealEdgePixels.GetHashCode();
                    hash = hash * 397 ^ surfaceBakeRevision;
                    hash = hash * 397 ^ (staticWaterEditorTexture != null
                        ? staticWaterEditorTexture.GetInstanceID()
                        : 0);
                    hash = hash * 397 ^ (bakedStaticWaterMask != null
                        ? bakedStaticWaterMask.GetInstanceID()
                        : 0);
                    hash = hash * 397
                           ^ staticWaterEditorTileSizeMeters.GetHashCode();
                    hash = hash * 397
                           ^ staticWaterLayerOneSpeedMetersPerSecond.GetHashCode();
                    hash = hash * 397
                           ^ staticWaterLayerTwoSpeedMetersPerSecond.GetHashCode();
                    hash = hash * 397
                           ^ staticWaterLayerTwoScale.GetHashCode();
                    hash = hash * 397
                           ^ staticWaterWaveDistortion.GetHashCode();
                    hash = hash * 397
                           ^ staticWaterWaveSpeed.GetHashCode();
                    hash = hash * 397
                           ^ staticWaterWaveLengthMeters.GetHashCode();
                    hash = hash * 397
                           ^ staticWaterDeepSpeedMultiplier.GetHashCode();
                    hash = hash * 397 ^ staticWaterMaskBakeRevision;
                    return hash;
                }
            }
        }
        public Color BackgroundColor => backgroundColor;
        public Color LowHeightColor => lowHeightColor;
        public Color MiddleHeightColor => middleHeightColor;
        public Color HighHeightColor => highHeightColor;
        public Color ContourColor => contourColor;
        public bool IsValid => heightMap != null
                               && mapWidthMeters > 0f
                               && mapHeightMeters > 0f
                               && maximumHeightMeters > minimumHeightMeters;

        /// <summary>
        /// Samples the persistent static-water depth map in logical map meters.
        /// A result of zero means dry land. The data is bilinearly sampled so
        /// runtime traversal does not inherit jagged authoring-pixel edges.
        /// </summary>
        public float SampleStaticWaterDepth(Vector2 mapPositionMeters)
        {
            if (staticWaterDepthMap == null
                || staticWaterDepthMap.Length == 0
                || mapPositionMeters.x < 0f
                || mapPositionMeters.x > mapWidthMeters
                || mapPositionMeters.y < 0f
                || mapPositionMeters.y > mapHeightMeters)
            {
                return 0f;
            }

            int resolution = Mathf.RoundToInt(
                Mathf.Sqrt(staticWaterDepthMap.Length));
            if (resolution <= 0
                || resolution * resolution != staticWaterDepthMap.Length)
            {
                return 0f;
            }

            float pixelX = mapPositionMeters.x
                           / Mathf.Max(0.0001f, mapWidthMeters)
                           * resolution - 0.5f;
            float pixelY = mapPositionMeters.y
                           / Mathf.Max(0.0001f, mapHeightMeters)
                           * resolution - 0.5f;
            int unclampedX0 = Mathf.FloorToInt(pixelX);
            int unclampedY0 = Mathf.FloorToInt(pixelY);
            int x0 = Mathf.Clamp(unclampedX0, 0, resolution - 1);
            int y0 = Mathf.Clamp(unclampedY0, 0, resolution - 1);
            int x1 = Mathf.Clamp(unclampedX0 + 1, 0, resolution - 1);
            int y1 = Mathf.Clamp(unclampedY0 + 1, 0, resolution - 1);
            float blendX = Mathf.Clamp01(pixelX - Mathf.Floor(pixelX));
            float blendY = Mathf.Clamp01(pixelY - Mathf.Floor(pixelY));
            float lower = Mathf.Lerp(
                staticWaterDepthMap[y0 * resolution + x0],
                staticWaterDepthMap[y0 * resolution + x1],
                blendX);
            float upper = Mathf.Lerp(
                staticWaterDepthMap[y1 * resolution + x0],
                staticWaterDepthMap[y1 * resolution + x1],
                blendX);
            float encodedDepth = Mathf.Lerp(lower, upper, blendY);
            return encodedDepth / byte.MaxValue
                   * Mathf.Max(0.1f, maximumStaticWaterDepthMeters);
        }

        public bool HasStaticWaterAt(Vector2 mapPositionMeters)
        {
            return SampleStaticWaterDepth(mapPositionMeters) > 0.0001f;
        }

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
                    hash = hash * 397 ^ useHeightMapBorderMask.GetHashCode();
                    hash = hash * 397 ^ heightMapBorderMaskThreshold.GetHashCode();
                    hash = hash * 397 ^ heightMapBorderInsetMeters.GetHashCode();
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

#if UNITY_EDITOR
        public IReadOnlyList<TerrainSurfaceDefinition> SurfacePalette =>
            surfacePalette;
        public float SurfaceTransitionWidthMeters =>
            surfaceTransitionWidthMeters;
        public float SurfaceAlphaCoreWidthMeters =>
            surfaceAlphaCoreWidthMeters;
        public float SurfaceAlphaBlendShare => surfaceAlphaBlendShare;
        public float SurfaceBoundaryNoiseScaleMeters =>
            surfaceBoundaryNoiseScaleMeters;
        public float SurfaceBoundaryNoiseAmplitudeMeters =>
            surfaceBoundaryNoiseAmplitudeMeters;
        public float SurfaceScatterCellSizeMeters =>
            surfaceScatterCellSizeMeters;
        public float SurfaceScatterStrength => surfaceScatterStrength;
        public int SurfaceNoiseSeed => surfaceNoiseSeed;
        public int SurfaceBakeResolution => surfaceBakeResolution;
        public int SurfaceMaskResolution => surfaceMaskResolution;
        public float SurfacePatternAlphaNormalizationStrength =>
            surfacePatternAlphaNormalizationStrength;
        public bool SurfaceClosedContourAlphaEnabled =>
            surfaceClosedContourAlphaEnabled;
        public int SurfaceClosedContourAlphaResolution =>
            surfaceClosedContourAlphaResolution;
        public float SurfaceClosedContourEdgeAlphaMultiplier =>
            surfaceClosedContourEdgeAlphaMultiplier;
        public float SurfaceClosedContourEdgeHoldDistanceMeters =>
            surfaceClosedContourEdgeHoldDistanceMeters;
        public float SurfaceClosedContourFadeDistanceMeters =>
            surfaceClosedContourFadeDistanceMeters;
        public float SurfaceClosedContourCenterAlphaMultiplier =>
            surfaceClosedContourCenterAlphaMultiplier;
        public float SurfaceClosedContourFadeStrength =>
            surfaceClosedContourDistanceCurve;
        public float SurfaceClosedContourMinimumAreaSquareMeters =>
            surfaceClosedContourMinimumAreaSquareMeters;
        public float SurfaceOutsideClosedContourAlphaMultiplier =>
            surfaceOutsideClosedContourAlphaMultiplier;
        public bool SurfaceBakeNeedsUpgrade =>
            surfaceBakeGeneratorVersion < CurrentSurfaceBakeGeneratorVersion;
        public bool StaticWaterMaskBakeNeedsUpgrade =>
            staticWaterMaskBakeGeneratorVersion
            < CurrentStaticWaterMaskBakeGeneratorVersion;

        public bool EnsureStaticWaterAuthoringData()
        {
            int requestedResolution = Mathf.Clamp(
                staticWaterResolution,
                64,
                1024);
            int requestedLength = requestedResolution * requestedResolution;
            if (staticWaterDepthMap != null
                && staticWaterDepthMap.Length == requestedLength)
            {
                return false;
            }

            byte[] previousMap = staticWaterDepthMap;
            staticWaterDepthMap = new byte[requestedLength];
            if (previousMap == null || previousMap.Length == 0)
                return true;

            int previousResolution = Mathf.RoundToInt(
                Mathf.Sqrt(previousMap.Length));
            if (previousResolution <= 0
                || previousResolution * previousResolution
                != previousMap.Length)
            {
                return true;
            }

            for (int y = 0; y < requestedResolution; y++)
            {
                int sourceY = Mathf.Clamp(
                    Mathf.FloorToInt(
                        (y + 0.5f) / requestedResolution
                        * previousResolution),
                    0,
                    previousResolution - 1);
                for (int x = 0; x < requestedResolution; x++)
                {
                    int sourceX = Mathf.Clamp(
                        Mathf.FloorToInt(
                            (x + 0.5f) / requestedResolution
                            * previousResolution),
                        0,
                        previousResolution - 1);
                    staticWaterDepthMap[y * requestedResolution + x] =
                        previousMap[sourceY * previousResolution + sourceX];
                }
            }

            return true;
        }

        public byte[] GetOrCreateStaticWaterDepthMap()
        {
            EnsureStaticWaterAuthoringData();
            return staticWaterDepthMap;
        }

        public int CalculateStaticWaterAuthoringHash()
        {
            unchecked
            {
                int hash = staticWaterResolution;
                hash = hash * 397
                       ^ maximumStaticWaterDepthMeters.GetHashCode();
                hash = hash * 397
                       ^ maximumPassableStaticWaterDepthMeters.GetHashCode();
                if (staticWaterDepthMap != null)
                {
                    for (int index = 0;
                         index < staticWaterDepthMap.Length;
                         index++)
                    {
                        hash = hash * 31 ^ staticWaterDepthMap[index];
                    }
                }

                return hash;
            }
        }

        public bool PaintStaticWaterDepth(
            Vector2 mapPositionMeters,
            float brushRadiusMeters,
            StaticWaterDepthPaintMode mode,
            float targetDepthMeters,
            float depthStepMeters,
            float strength,
            float hardness)
        {
            byte[] depthMap = GetOrCreateStaticWaterDepthMap();
            int resolution = Mathf.Clamp(staticWaterResolution, 64, 1024);
            Vector2 mapSize = MapSizeMeters;
            float radius = Mathf.Max(0.01f, brushRadiusMeters);
            float radiusSquared = radius * radius;
            float clampedStrength = Mathf.Clamp01(strength);
            float clampedHardness = Mathf.Clamp01(hardness);
            float maximumDepth = Mathf.Max(
                0.1f,
                maximumStaticWaterDepthMeters);
            float targetDepth = Mathf.Clamp(
                targetDepthMeters,
                0f,
                maximumDepth);
            float depthStep = Mathf.Max(0f, depthStepMeters);
            int minimumX = Mathf.Clamp(
                Mathf.FloorToInt(
                    (mapPositionMeters.x - radius)
                    / mapSize.x * resolution),
                0,
                resolution - 1);
            int maximumX = Mathf.Clamp(
                Mathf.CeilToInt(
                    (mapPositionMeters.x + radius)
                    / mapSize.x * resolution),
                0,
                resolution - 1);
            int minimumY = Mathf.Clamp(
                Mathf.FloorToInt(
                    (mapPositionMeters.y - radius)
                    / mapSize.y * resolution),
                0,
                resolution - 1);
            int maximumY = Mathf.Clamp(
                Mathf.CeilToInt(
                    (mapPositionMeters.y + radius)
                    / mapSize.y * resolution),
                0,
                resolution - 1);
            byte[] smoothingSource = mode == StaticWaterDepthPaintMode.Smooth
                ? (byte[])depthMap.Clone()
                : null;
            bool changed = false;

            for (int y = minimumY; y <= maximumY; y++)
            {
                float pixelMapY = (y + 0.5f) / resolution * mapSize.y;
                for (int x = minimumX; x <= maximumX; x++)
                {
                    float pixelMapX = (x + 0.5f) / resolution * mapSize.x;
                    float deltaX = pixelMapX - mapPositionMeters.x;
                    float deltaY = pixelMapY - mapPositionMeters.y;
                    float distanceSquared = deltaX * deltaX + deltaY * deltaY;
                    if (distanceSquared > radiusSquared)
                        continue;

                    float normalizedDistance = Mathf.Sqrt(distanceSquared)
                                               / radius;
                    float falloff = CalculateStaticWaterBrushFalloff(
                        normalizedDistance,
                        clampedHardness);
                    float weight = falloff * clampedStrength;
                    if (weight <= 0.0001f)
                        continue;

                    int index = y * resolution + x;
                    float previousDepth = DecodeStaticWaterDepth(
                        depthMap[index],
                        maximumDepth);
                    float nextDepth;
                    switch (mode)
                    {
                        case StaticWaterDepthPaintMode.Add:
                            nextDepth = previousDepth + depthStep * weight;
                            break;
                        case StaticWaterDepthPaintMode.Subtract:
                            nextDepth = previousDepth - depthStep * weight;
                            break;
                        case StaticWaterDepthPaintMode.Smooth:
                            float averageDepth = CalculateNeighbourDepthAverage(
                                smoothingSource,
                                resolution,
                                x,
                                y,
                                maximumDepth);
                            nextDepth = Mathf.Lerp(
                                previousDepth,
                                averageDepth,
                                weight);
                            break;
                        case StaticWaterDepthPaintMode.Erase:
                            nextDepth = Mathf.Lerp(previousDepth, 0f, weight);
                            break;
                        default:
                            nextDepth = Mathf.Lerp(
                                previousDepth,
                                targetDepth,
                                weight);
                            break;
                    }

                    byte encodedDepth = EncodeStaticWaterDepth(
                        Mathf.Clamp(nextDepth, 0f, maximumDepth),
                        maximumDepth);
                    if (depthMap[index] == encodedDepth)
                        continue;

                    depthMap[index] = encodedDepth;
                    changed = true;
                }
            }

            return changed;
        }

        public bool FillStaticWaterDepth(float depthMeters)
        {
            byte[] depthMap = GetOrCreateStaticWaterDepthMap();
            float maximumDepth = Mathf.Max(
                0.1f,
                maximumStaticWaterDepthMeters);
            byte targetValue = EncodeStaticWaterDepth(
                Mathf.Clamp(depthMeters, 0f, maximumDepth),
                maximumDepth);
            bool changed = false;
            for (int index = 0; index < depthMap.Length; index++)
            {
                if (depthMap[index] == targetValue)
                    continue;

                depthMap[index] = targetValue;
                changed = true;
            }

            return changed;
        }

        private static float CalculateStaticWaterBrushFalloff(
            float normalizedDistance,
            float hardness)
        {
            if (normalizedDistance <= hardness)
                return 1f;
            if (hardness >= 0.9999f)
                return normalizedDistance <= 1f ? 1f : 0f;

            float edgeProgress = Mathf.InverseLerp(
                hardness,
                1f,
                normalizedDistance);
            return 1f - Mathf.SmoothStep(0f, 1f, edgeProgress);
        }

        private static float CalculateNeighbourDepthAverage(
            byte[] source,
            int resolution,
            int centreX,
            int centreY,
            float maximumDepth)
        {
            if (source == null || source.Length == 0)
                return 0f;

            float total = 0f;
            int count = 0;
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                int y = centreY + offsetY;
                if (y < 0 || y >= resolution)
                    continue;

                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int x = centreX + offsetX;
                    if (x < 0 || x >= resolution)
                        continue;

                    total += DecodeStaticWaterDepth(
                        source[y * resolution + x],
                        maximumDepth);
                    count++;
                }
            }

            return count > 0 ? total / count : 0f;
        }

        private static float DecodeStaticWaterDepth(
            byte encodedDepth,
            float maximumDepth)
        {
            return encodedDepth / (float)byte.MaxValue * maximumDepth;
        }

        private static byte EncodeStaticWaterDepth(
            float depthMeters,
            float maximumDepth)
        {
            if (depthMeters <= 0.0001f)
                return 0;

            return (byte)Mathf.Clamp(
                Mathf.RoundToInt(
                    depthMeters / Mathf.Max(0.0001f, maximumDepth)
                    * byte.MaxValue),
                1,
                byte.MaxValue);
        }

        public bool HasUsableSurfaceDefinitions
        {
            get
            {
                if (surfacePalette == null)
                    return false;

                foreach (TerrainSurfaceDefinition definition in surfacePalette)
                {
                    if (definition != null && definition.IsUsable)
                        return true;
                }

                return false;
            }
        }

        public TerrainSurfaceDefinition GetSurfaceDefinition(int terrainId)
        {
            if (surfacePalette == null || terrainId <= 0)
                return null;

            foreach (TerrainSurfaceDefinition definition in surfacePalette)
            {
                if (definition != null && definition.TerrainId == terrainId)
                    return definition;
            }

            return null;
        }

        public bool EnsureSurfaceAuthoringData()
        {
            bool changed = false;
            if (surfacePalette == null)
            {
                surfacePalette = new List<TerrainSurfaceDefinition>();
                changed = true;
            }

            if (surfacePalette.Count == 0)
            {
                surfacePalette.Add(new TerrainSurfaceDefinition(
                    1,
                    "Terrain 1",
                    surfacePatternTexture,
                    surfaceTint,
                    surfaceOpacity,
                    surfaceTileSizeMeters));
                changed = true;
            }

            if (surfacePalette.Count > 255)
            {
                surfacePalette.RemoveRange(255, surfacePalette.Count - 255);
                changed = true;
            }

            var usedIds = new HashSet<int>();
            for (int index = 0; index < surfacePalette.Count; index++)
            {
                TerrainSurfaceDefinition definition = surfacePalette[index];
                if (definition == null)
                {
                    definition = new TerrainSurfaceDefinition();
                    surfacePalette[index] = definition;
                    changed = true;
                }

                definition.Validate();
                int id = definition.TerrainId;
                if (!usedIds.Add(id))
                {
                    int availableId = FindAvailableTerrainId(usedIds);
                    definition.SetTerrainId(availableId);
                    usedIds.Add(availableId);
                    changed = true;
                }
            }

            if (ResizeSurfaceTypeMapIfNeeded())
                changed = true;

            if (surfaceDataVersion < 1)
            {
                // The old format stored 255 for every painted pixel. ID zero stays
                // empty and every previous painted pixel becomes palette entry 1.
                for (int index = 0; index < surfaceMask.Length; index++)
                {
                    if (surfaceMask[index] != 0)
                        surfaceMask[index] = 1;
                }

                surfaceDataVersion = 1;
                changed = true;
            }

            return changed;
        }

        public int CalculateSurfaceAuthoringHash()
        {
            unchecked
            {
                int hash = surfaceDataVersion;
                hash = hash * 397 ^ surfaceTransitionWidthMeters.GetHashCode();
                hash = hash * 397 ^ surfaceAlphaCoreWidthMeters.GetHashCode();
                hash = hash * 397 ^ surfaceAlphaBlendShare.GetHashCode();
                hash = hash * 397 ^ surfaceBoundaryNoiseScaleMeters.GetHashCode();
                hash = hash * 397 ^ surfaceBoundaryNoiseAmplitudeMeters.GetHashCode();
                hash = hash * 397 ^ surfaceScatterCellSizeMeters.GetHashCode();
                hash = hash * 397 ^ surfaceScatterStrength.GetHashCode();
                hash = hash * 397 ^ surfaceNoiseSeed;
                hash = hash * 397 ^ surfaceBakeResolution;
                hash = hash * 397 ^ surfaceMaskResolution;
                hash = hash * 397
                       ^ surfacePatternAlphaNormalizationStrength.GetHashCode();
                hash = hash * 397 ^ surfaceClosedContourAlphaEnabled.GetHashCode();
                hash = hash * 397 ^ surfaceClosedContourAlphaResolution;
                hash = hash * 397 ^ surfaceClosedContourEdgeAlphaMultiplier.GetHashCode();
                hash = hash * 397 ^ surfaceClosedContourEdgeHoldDistanceMeters.GetHashCode();
                hash = hash * 397 ^ surfaceClosedContourFadeDistanceMeters.GetHashCode();
                hash = hash * 397 ^ surfaceClosedContourCenterAlphaMultiplier.GetHashCode();
                hash = hash * 397 ^ surfaceClosedContourDistanceCurve.GetHashCode();
                hash = hash * 397 ^ surfaceClosedContourMinimumAreaSquareMeters.GetHashCode();
                hash = hash * 397 ^ surfaceOutsideClosedContourAlphaMultiplier.GetHashCode();
                hash = hash * 397 ^ (heightMap != null
                    ? heightMap.GetInstanceID()
                    : 0);
                hash = hash * 397 ^ mapWidthMeters.GetHashCode();
                hash = hash * 397 ^ mapHeightMeters.GetHashCode();
                hash = hash * 397 ^ minimumHeightMeters.GetHashCode();
                hash = hash * 397 ^ maximumHeightMeters.GetHashCode();
                hash = hash * 397 ^ normalizeSourceRange.GetHashCode();
                hash = hash * 397 ^ surfaceSmoothingSigmaMeters.GetHashCode();
                hash = hash * 397 ^ contourIntervalMeters.GetHashCode();
                if (surfacePalette != null)
                {
                    hash = hash * 397 ^ surfacePalette.Count;
                    foreach (TerrainSurfaceDefinition definition in surfacePalette)
                    {
                        hash = definition != null
                            ? definition.AppendConfigurationHash(hash)
                            : hash * 397;
                    }
                }

                if (surfaceMask != null)
                {
                    for (int index = 0; index < surfaceMask.Length; index++)
                        hash = hash * 31 ^ surfaceMask[index];
                }

                return hash;
            }
        }

        public byte[] GetOrCreateSurfaceTypeMap()
        {
            EnsureSurfaceAuthoringData();
            return surfaceMask;
        }

        public bool PaintSurfaceType(
            Vector2 mapPositionMeters,
            float brushRadiusMeters,
            int terrainId)
        {
            if (terrainId < 0 || terrainId > 255)
                return false;
            if (terrainId != 0 && GetSurfaceDefinition(terrainId) == null)
                return false;

            byte[] typeMap = GetOrCreateSurfaceTypeMap();
            int resolution = Mathf.Clamp(surfaceMaskResolution, 64, 1024);
            Vector2 mapSize = MapSizeMeters;
            float radius = Mathf.Max(0.01f, brushRadiusMeters);
            float radiusSquared = radius * radius;
            int minimumX = Mathf.Clamp(
                Mathf.FloorToInt(
                    (mapPositionMeters.x - radius) / mapSize.x * resolution),
                0,
                resolution - 1);
            int maximumX = Mathf.Clamp(
                Mathf.CeilToInt(
                    (mapPositionMeters.x + radius) / mapSize.x * resolution),
                0,
                resolution - 1);
            int minimumY = Mathf.Clamp(
                Mathf.FloorToInt(
                    (mapPositionMeters.y - radius) / mapSize.y * resolution),
                0,
                resolution - 1);
            int maximumY = Mathf.Clamp(
                Mathf.CeilToInt(
                    (mapPositionMeters.y + radius) / mapSize.y * resolution),
                0,
                resolution - 1);
            byte targetValue = (byte)terrainId;
            bool changed = false;

            for (int y = minimumY; y <= maximumY; y++)
            {
                float pixelMapY = (y + 0.5f) / resolution * mapSize.y;
                for (int x = minimumX; x <= maximumX; x++)
                {
                    float pixelMapX = (x + 0.5f) / resolution * mapSize.x;
                    float deltaX = pixelMapX - mapPositionMeters.x;
                    float deltaY = pixelMapY - mapPositionMeters.y;
                    if (deltaX * deltaX + deltaY * deltaY > radiusSquared)
                        continue;

                    int index = y * resolution + x;
                    if (typeMap[index] == targetValue)
                        continue;

                    typeMap[index] = targetValue;
                    changed = true;
                }
            }

            return changed;
        }

        public bool FillSurfaceType(int terrainId)
        {
            if (terrainId < 0 || terrainId > 255)
                return false;
            if (terrainId != 0 && GetSurfaceDefinition(terrainId) == null)
                return false;

            byte[] typeMap = GetOrCreateSurfaceTypeMap();
            byte targetValue = (byte)terrainId;
            bool changed = false;
            for (int index = 0; index < typeMap.Length; index++)
            {
                if (typeMap[index] == targetValue)
                    continue;

                typeMap[index] = targetValue;
                changed = true;
            }

            return changed;
        }

        private bool ResizeSurfaceTypeMapIfNeeded()
        {
            int requestedResolution = Mathf.Clamp(surfaceMaskResolution, 64, 1024);
            int requestedLength = requestedResolution * requestedResolution;
            if (surfaceMask != null && surfaceMask.Length == requestedLength)
                return false;

            byte[] previousMap = surfaceMask;
            surfaceMask = new byte[requestedLength];
            if (previousMap == null || previousMap.Length == 0)
                return true;

            int previousResolution = Mathf.RoundToInt(
                Mathf.Sqrt(previousMap.Length));
            if (previousResolution * previousResolution != previousMap.Length)
                return true;

            for (int y = 0; y < requestedResolution; y++)
            {
                int sourceY = Mathf.Clamp(
                    Mathf.FloorToInt(
                        (y + 0.5f) / requestedResolution * previousResolution),
                    0,
                    previousResolution - 1);
                for (int x = 0; x < requestedResolution; x++)
                {
                    int sourceX = Mathf.Clamp(
                        Mathf.FloorToInt(
                            (x + 0.5f) / requestedResolution * previousResolution),
                        0,
                        previousResolution - 1);
                    surfaceMask[y * requestedResolution + x] =
                        previousMap[sourceY * previousResolution + sourceX];
                }
            }

            return true;
        }

        private static int FindAvailableTerrainId(HashSet<int> usedIds)
        {
            for (int id = 1; id <= 255; id++)
            {
                if (!usedIds.Contains(id))
                    return id;
            }

            // A palette with more than 255 entries cannot be represented by R8.
            // Reuse 255 only as a final validation fallback; the Editor warns and
            // prevents that entry from becoming independently paintable.
            return 255;
        }

        public void SetBakedSurfaceVisual(Texture2D texture)
        {
            bakedSurfaceVisual = texture;
            surfaceBakeGeneratorVersion = CurrentSurfaceBakeGeneratorVersion;
            unchecked
            {
                surfaceBakeRevision++;
            }
        }

        public void SetBakedStaticWaterMask(Texture2D texture)
        {
            bakedStaticWaterMask = texture;
            staticWaterMaskBakeGeneratorVersion =
                CurrentStaticWaterMaskBakeGeneratorVersion;
            unchecked
            {
                staticWaterMaskBakeRevision++;
            }
        }
#endif

        private void OnValidate()
        {
            mapWidthMeters = Mathf.Max(1f, mapWidthMeters);
            mapHeightMeters = Mathf.Max(1f, mapHeightMeters);
            maximumHeightMeters = Mathf.Max(
                minimumHeightMeters + 0.01f,
                maximumHeightMeters);
            bakedHeightResolution = Mathf.Clamp(bakedHeightResolution, 128, 2048);
            surfaceSmoothingSigmaMeters = Mathf.Max(0f, surfaceSmoothingSigmaMeters);
            heightMapBorderMaskThreshold = Mathf.Clamp(
                heightMapBorderMaskThreshold,
                0f,
                0.25f);
            heightMapBorderInsetMeters = Mathf.Max(
                0f,
                heightMapBorderInsetMeters);
            previewResolution = Mathf.Clamp(previewResolution, 128, 8000);
            contourIntervalMeters = Mathf.Max(1f, contourIntervalMeters);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            minimumContourWidth = Mathf.Clamp(minimumContourWidth, 0.1f, 10f);
            maximumContourWidth = Mathf.Clamp(maximumContourWidth, 0.1f, 10f);
            maximumContourCoverage = Mathf.Clamp(maximumContourCoverage, 0.1f, 0.7f);
            contourEdgeSoftness = Mathf.Clamp(contourEdgeSoftness, 0.1f, 1.5f);
            viewportHeightSamples = Mathf.Clamp(viewportHeightSamples, 16, 128);
            staticWaterResolution = Mathf.Clamp(
                staticWaterResolution,
                64,
                1024);
            maximumStaticWaterDepthMeters = Mathf.Max(
                0.1f,
                maximumStaticWaterDepthMeters);
            maximumPassableStaticWaterDepthMeters = Mathf.Clamp(
                maximumPassableStaticWaterDepthMeters,
                0f,
                maximumStaticWaterDepthMeters);
            staticWaterEditorTileSizeMeters = Mathf.Max(
                0.1f,
                staticWaterEditorTileSizeMeters);
            staticWaterLayerTwoScale = Mathf.Max(
                0.1f,
                staticWaterLayerTwoScale);
            staticWaterWaveDistortion = Mathf.Clamp(
                staticWaterWaveDistortion,
                0f,
                0.2f);
            staticWaterWaveSpeed = Mathf.Max(0f, staticWaterWaveSpeed);
            staticWaterWaveLengthMeters = Mathf.Max(
                0.1f,
                staticWaterWaveLengthMeters);
            staticWaterDeepSpeedMultiplier = Mathf.Clamp01(
                staticWaterDeepSpeedMultiplier);
            surfaceRevealEdgePixels = Mathf.Max(0f, surfaceRevealEdgePixels);
#if UNITY_EDITOR
            surfaceOpacity = Mathf.Clamp01(surfaceOpacity);
            surfaceTileSizeMeters = Mathf.Max(0.1f, surfaceTileSizeMeters);
            surfaceTransitionWidthMeters = Mathf.Max(
                0.1f,
                surfaceTransitionWidthMeters);
            surfaceAlphaCoreWidthMeters = Mathf.Max(
                0f,
                surfaceAlphaCoreWidthMeters);
            surfaceAlphaBlendShare = Mathf.Clamp01(surfaceAlphaBlendShare);
            surfaceBoundaryNoiseScaleMeters = Mathf.Max(
                0.1f,
                surfaceBoundaryNoiseScaleMeters);
            surfaceBoundaryNoiseAmplitudeMeters = Mathf.Max(
                0f,
                surfaceBoundaryNoiseAmplitudeMeters);
            surfaceScatterCellSizeMeters = Mathf.Max(
                0.05f,
                surfaceScatterCellSizeMeters);
            surfaceScatterStrength = Mathf.Clamp01(surfaceScatterStrength);
            surfaceBakeResolution = Mathf.Clamp(surfaceBakeResolution, 256, 4096);
            surfaceMaskResolution = Mathf.Clamp(surfaceMaskResolution, 64, 1024);
            surfacePatternAlphaNormalizationStrength = Mathf.Clamp01(
                surfacePatternAlphaNormalizationStrength);
            surfaceClosedContourAlphaResolution = Mathf.Clamp(
                surfaceClosedContourAlphaResolution,
                128,
                1024);
            surfaceClosedContourEdgeAlphaMultiplier = Mathf.Clamp(
                surfaceClosedContourEdgeAlphaMultiplier,
                0f,
                2f);
            surfaceClosedContourEdgeHoldDistanceMeters = Mathf.Max(
                0f,
                surfaceClosedContourEdgeHoldDistanceMeters);
            surfaceClosedContourFadeDistanceMeters = Mathf.Max(
                0.1f,
                surfaceClosedContourFadeDistanceMeters);
            surfaceClosedContourCenterAlphaMultiplier = Mathf.Clamp(
                surfaceClosedContourCenterAlphaMultiplier,
                0f,
                2f);
            surfaceClosedContourDistanceCurve = Mathf.Clamp(
                surfaceClosedContourDistanceCurve,
                0.1f,
                8f);
            surfaceClosedContourMinimumAreaSquareMeters = Mathf.Max(
                0f,
                surfaceClosedContourMinimumAreaSquareMeters);
            surfaceOutsideClosedContourAlphaMultiplier = Mathf.Clamp(
                surfaceOutsideClosedContourAlphaMultiplier,
                0f,
                2f);
            if (surfacePalette != null)
            {
                foreach (TerrainSurfaceDefinition definition in surfacePalette)
                    definition?.Validate();
            }
#endif
        }
    }
}
