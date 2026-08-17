using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.MapTest
{
#if UNITY_EDITOR
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
        public const int CurrentSurfaceBakeGeneratorVersion = 5;
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

        [Header("Static Closed-Contour Alpha")]
        [Tooltip("Permanently bakes stronger terrain texture alpha near contour-region boundaries and weaker alpha toward each region's centre. The map's four straight sides can close contour lines that reach an edge. This is Editor-only and never reacts to the player at runtime.")]
        [SerializeField] private bool surfaceClosedContourAlphaEnabled = true;

        [Tooltip("Resolution used only while the Editor identifies closed contours and calculates distance from their boundaries.")]
        [SerializeField, Range(128, 1024)]
        private int surfaceClosedContourAlphaResolution = 512;

        [Tooltip("Multiplier applied to terrain texture alpha at a closed contour boundary.")]
        [SerializeField, Range(0f, 2f)]
        private float surfaceClosedContourEdgeAlphaMultiplier = 0.52f;

        [Tooltip("Multiplier applied to terrain texture alpha at the deepest point inside a closed contour region.")]
        [SerializeField, Range(0f, 2f)]
        private float surfaceClosedContourCenterAlphaMultiplier = 0.015f;

        [Tooltip("Shape of the permanent boundary-to-centre alpha falloff. Values above one preserve the stronger edge alpha for longer.")]
        [SerializeField, Range(0.1f, 4f)]
        private float surfaceClosedContourDistanceCurve = 0.22f;

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
        public int PreviewResolution => previewResolution;
        public float ContourIntervalMeters => contourIntervalMeters;
        public float PixelsPerUnit => pixelsPerUnit;
        public Shader DynamicContourShader => dynamicContourShader;
        public float MinimumContourWidth => minimumContourWidth;
        public float MaximumContourWidth => maximumContourWidth;
        public float MaximumContourCoverage => maximumContourCoverage;
        public float ContourEdgeSoftness => contourEdgeSoftness;
        public int ViewportHeightSamples => viewportHeightSamples;
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
        public bool SurfaceClosedContourAlphaEnabled =>
            surfaceClosedContourAlphaEnabled;
        public int SurfaceClosedContourAlphaResolution =>
            surfaceClosedContourAlphaResolution;
        public float SurfaceClosedContourEdgeAlphaMultiplier =>
            surfaceClosedContourEdgeAlphaMultiplier;
        public float SurfaceClosedContourCenterAlphaMultiplier =>
            surfaceClosedContourCenterAlphaMultiplier;
        public float SurfaceClosedContourDistanceCurve =>
            surfaceClosedContourDistanceCurve;
        public float SurfaceClosedContourMinimumAreaSquareMeters =>
            surfaceClosedContourMinimumAreaSquareMeters;
        public float SurfaceOutsideClosedContourAlphaMultiplier =>
            surfaceOutsideClosedContourAlphaMultiplier;
        public bool SurfaceBakeNeedsUpgrade =>
            surfaceBakeGeneratorVersion < CurrentSurfaceBakeGeneratorVersion;
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
                hash = hash * 397 ^ surfaceClosedContourAlphaEnabled.GetHashCode();
                hash = hash * 397 ^ surfaceClosedContourAlphaResolution;
                hash = hash * 397 ^ surfaceClosedContourEdgeAlphaMultiplier.GetHashCode();
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
            previewResolution = Mathf.Clamp(previewResolution, 128, 8000);
            contourIntervalMeters = Mathf.Max(1f, contourIntervalMeters);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            minimumContourWidth = Mathf.Clamp(minimumContourWidth, 0.1f, 10f);
            maximumContourWidth = Mathf.Clamp(maximumContourWidth, 0.1f, 10f);
            maximumContourCoverage = Mathf.Clamp(maximumContourCoverage, 0.1f, 0.7f);
            contourEdgeSoftness = Mathf.Clamp(contourEdgeSoftness, 0.1f, 1.5f);
            viewportHeightSamples = Mathf.Clamp(viewportHeightSamples, 16, 128);
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
            surfaceClosedContourAlphaResolution = Mathf.Clamp(
                surfaceClosedContourAlphaResolution,
                128,
                1024);
            surfaceClosedContourEdgeAlphaMultiplier = Mathf.Clamp(
                surfaceClosedContourEdgeAlphaMultiplier,
                0f,
                2f);
            surfaceClosedContourCenterAlphaMultiplier = Mathf.Clamp(
                surfaceClosedContourCenterAlphaMultiplier,
                0f,
                2f);
            surfaceClosedContourDistanceCurve = Mathf.Clamp(
                surfaceClosedContourDistanceCurve,
                0.1f,
                4f);
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
