using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.MapTest
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-1000)]
    public sealed class MapTestSceneController : MonoBehaviour
    {
        private const float LowestVisibleContourOpacity = 0.15f;
        private const float HighestVisibleContourOpacity = 1f;
        private const float DefaultSurfaceRevealRadiusPixels = 430f;

        [Header("Fixed Level Asset")]
        [Tooltip("Persistent map definition used by this scene. When assigned, its terrain and presentation settings override the legacy fields below.")]
        [SerializeField] private HeightMapLevelAsset levelAsset;

        [Header("Height Source (Legacy Fallback)")]
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

        [Header("Editor Preview")]
        [Tooltip("Lower physical bake resolution used only while editing. Runtime still uses the fixed level asset's full bake resolution.")]
        [SerializeField, Range(128, 1024)] private int editorHeightResolution = 512;

        [Tooltip("Lower color preview resolution used only while editing. World-space map size remains identical to runtime.")]
        [SerializeField, Range(128, 2048)] private int editorPreviewResolution = 512;

        [Tooltip("Direct reference to the contour shader used by standalone builds. Keep this assigned so Unity includes the shader instead of stripping a Shader.Find-only dependency.")]
        [SerializeField] private Shader dynamicContourShader;

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

        private Camera mapCamera;
        private SpriteRenderer mapRenderer;
        private BakedHeightField heightField;
        private Material contourMaterial;
        private Texture2D bakedSurfaceVisual;
        private float surfaceRevealEdgePixels = 4f;
        private ScanChargeUI surfaceRevealUi;
        private Canvas surfaceRevealCanvas;
        private Material surfaceSettingsMaterial;
        private Texture2D appliedSurfaceVisual;
        private Vector2 appliedSurfaceCenterPixels = new Vector2(
            float.NaN,
            float.NaN);
        private float appliedSurfaceRadiusPixels = float.NaN;
        private float appliedSurfaceEdgePixels = float.NaN;
        private bool appliedSurfaceEnabled;
        private bool appliedSurfaceRevealEnabled;
        private int appliedWaterPresentationHash = int.MinValue;
        private Texture2D generatedPreviewTexture;
        private Sprite generatedMapSprite;
        private GameObject generatedMapObject;
        private int lastViewportUpdateFrame = -1;
        private int editorConfigurationHash = int.MinValue;
        private int editorSurfacePresentationHash = int.MinValue;
        private bool rebuildingMap;
        private bool generatedForPlayMode;

        public float VisibleMinimumContourHeight { get; private set; }
        public float VisibleMaximumContourHeight { get; private set; }
        public Vector2 MapSizeMeters => new Vector2(mapWidthMeters, mapHeightMeters);
        public HeightMapLevelAsset LevelAsset => levelAsset;
        public BakedHeightField HeightField => heightField;
        public float ContourIntervalMeters => contourIntervalMeters;
        public Color BackgroundColor => backgroundColor;

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
            RebuildGeneratedMap();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                if (!HasGeneratedMap || !generatedForPlayMode)
                    RebuildGeneratedMap();
                Camera.onPreCull += HandleCameraPreCull;
                RenderPipelineManager.beginCameraRendering +=
                    HandleBeginCameraRendering;
            }
            else if (!HasGeneratedMap || generatedForPlayMode)
            {
                RebuildGeneratedMap();
            }
        }

        private void OnDisable()
        {
            Camera.onPreCull -= HandleCameraPreCull;
            RenderPipelineManager.beginCameraRendering -=
                HandleBeginCameraRendering;
        }

        private void Update()
        {
            if (Application.isPlaying || rebuildingMap)
                return;

            RefreshEditorSurfaceIfChanged();
            int configurationHash = CalculateEditorConfigurationHash();
            if (!HasGeneratedMap
                || generatedForPlayMode
                || configurationHash != editorConfigurationHash)
            {
                RebuildGeneratedMap();
            }
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
            if (!heightField.IsPlayable(uv))
                return false;

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
            if (!heightField.IsPlayable(uv))
                return false;
            heightMeters = SampleHeight(uv);
            return true;
        }

        public bool TrySampleStaticWaterWorldPosition(
            Vector2 worldPosition,
            out float depthMeters)
        {
            depthMeters = 0f;
            if (!TrySampleWorldPosition(
                    worldPosition,
                    out Vector2 mapPositionMeters,
                    out _))
            {
                return false;
            }

            depthMeters = levelAsset != null
                ? levelAsset.SampleStaticWaterDepth(mapPositionMeters)
                : 0f;
            return true;
        }

        public bool TrySampleStaticWaterMapPosition(
            Vector2 mapPositionMeters,
            out float depthMeters)
        {
            depthMeters = 0f;
            if (!TrySampleMapPosition(mapPositionMeters, out _))
                return false;

            depthMeters = levelAsset != null
                ? levelAsset.SampleStaticWaterDepth(mapPositionMeters)
                : 0f;
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
            if (!heightField.IsPlayable(uv))
                return false;
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
            RefreshSurfaceMaterialSettings();
        }

        public void UseSurfaceRevealUi(ScanChargeUI scanUi)
        {
            surfaceRevealUi = scanUi;
            surfaceRevealCanvas = scanUi != null
                ? scanUi.GetComponentInParent<Canvas>()
                : null;
            RefreshSurfaceMaterialSettings();
        }

        private void RebuildGeneratedMap()
        {
            if (rebuildingMap)
                return;

            rebuildingMap = true;
            try
            {
                ApplyFixedLevelAsset();
                ReleaseGeneratedMap();
                if (heightMap == null)
                {
                    if (Application.isPlaying)
                    {
                        Debug.LogError(
                            "MapTestScene is missing its fixed height-map texture.",
                            this);
                        enabled = false;
                    }

                    return;
                }

                BakePhysicalHeightField();
                if (Application.isPlaying)
                    CreateCamera();
                CreateHeightVisualization();
                if (Application.isPlaying)
                {
                    UpdateVisibleContourRange(mapCamera);
                }
                else
                {
                    ShowCompleteContourRange();
                }

                generatedForPlayMode = Application.isPlaying;
                editorConfigurationHash = CalculateEditorConfigurationHash();
            }
            finally
            {
                rebuildingMap = false;
            }
        }

        private void ApplyFixedLevelAsset()
        {
            if (levelAsset == null || !levelAsset.IsValid)
                return;

            heightMap = levelAsset.HeightMap;
            mapWidthMeters = levelAsset.MapSizeMeters.x;
            mapHeightMeters = levelAsset.MapSizeMeters.y;
            minimumHeightMeters = levelAsset.MinimumHeightMeters;
            maximumHeightMeters = levelAsset.MaximumHeightMeters;
            bakedHeightResolution = levelAsset.BakedHeightResolution;
            normalizeSourceRange = levelAsset.NormalizeSourceRange;
            surfaceSmoothingSigmaMeters =
                levelAsset.SurfaceSmoothingSigmaMeters;
            previewResolution = levelAsset.PreviewResolution;
            contourIntervalMeters = levelAsset.ContourIntervalMeters;
            pixelsPerUnit = levelAsset.PixelsPerUnit;
            dynamicContourShader = levelAsset.DynamicContourShader;
            minimumContourWidth = levelAsset.MinimumContourWidth;
            maximumContourWidth = levelAsset.MaximumContourWidth;
            maximumContourCoverage = levelAsset.MaximumContourCoverage;
            contourEdgeSoftness = levelAsset.ContourEdgeSoftness;
            viewportHeightSamples = levelAsset.ViewportHeightSamples;
            bakedSurfaceVisual = levelAsset.BakedSurfaceVisual;
            surfaceRevealEdgePixels = levelAsset.SurfaceRevealEdgePixels;
            editorSurfacePresentationHash = levelAsset.SurfacePresentationHash;
            backgroundColor = levelAsset.BackgroundColor;
            lowHeightColor = levelAsset.LowHeightColor;
            middleHeightColor = levelAsset.MiddleHeightColor;
            highHeightColor = levelAsset.HighHeightColor;
            contourColor = levelAsset.ContourColor;
        }

        private int CalculateEditorConfigurationHash()
        {
            unchecked
            {
                int hash = levelAsset != null
                    ? levelAsset.ConfigurationHash
                    : heightMap != null
                        ? heightMap.GetInstanceID()
                        : 0;
                hash = hash * 397 ^ editorHeightResolution;
                hash = hash * 397 ^ editorPreviewResolution;
                hash = hash * 397 ^ transform.position.GetHashCode();
                hash = hash * 397 ^ transform.rotation.GetHashCode();
                hash = hash * 397 ^ transform.lossyScale.GetHashCode();
                return hash;
            }
        }

        private void RefreshEditorSurfaceIfChanged()
        {
            if (levelAsset == null)
                return;

            int presentationHash = levelAsset.SurfacePresentationHash;
            if (presentationHash == editorSurfacePresentationHash)
                return;

            bakedSurfaceVisual = levelAsset.BakedSurfaceVisual;
            surfaceRevealEdgePixels = levelAsset.SurfaceRevealEdgePixels;
            editorSurfacePresentationHash = presentationHash;
            RefreshSurfaceMaterialSettings();
        }

        private void BakePhysicalHeightField()
        {
            heightField?.Dispose();
            int requestedResolution = Application.isPlaying
                ? bakedHeightResolution
                : Mathf.Min(bakedHeightResolution, editorHeightResolution);
            heightField = BakedHeightField.Bake(
                heightMap,
                requestedResolution,
                MapSizeMeters,
                minimumHeightMeters,
                maximumHeightMeters,
                normalizeSourceRange,
                surfaceSmoothingSigmaMeters,
                levelAsset != null && levelAsset.UseHeightMapBorderMask,
                levelAsset != null
                    ? levelAsset.HeightMapBorderMaskThreshold
                    : 0.01f,
                levelAsset != null
                    ? levelAsset.HeightMapBorderInsetMeters
                    : 0f);
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
            int generatedResolution = Application.isPlaying
                ? previewResolution
                : Mathf.Min(previewResolution, editorPreviewResolution);
            generatedPreviewTexture = new Texture2D(
                generatedResolution,
                generatedResolution,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Generated Height Preview",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // The texture is RGBA32, so retaining four 32-bit floats per channel
            // only multiplies temporary memory without preserving more output data.
            // Color32 keeps the 4096 formal-map preview comfortably bounded.
            var colors = new Color32[generatedResolution * generatedResolution];
            float onePixel = 1f / generatedResolution;
            float heightRange = maximumHeightMeters - minimumHeightMeters;
            for (int y = 0; y < generatedResolution; y++)
            {
                for (int x = 0; x < generatedResolution; x++)
                {
                    Vector2 uv = new Vector2(
                        (x + 0.5f) / generatedResolution,
                        (y + 0.5f) / generatedResolution);
                    if (!heightField.IsPlayable(uv))
                    {
                        colors[y * generatedResolution + x] = backgroundColor;
                        continue;
                    }

                    float height = SampleHeight(uv);
                    float normalized = Mathf.InverseLerp(minimumHeightMeters, maximumHeightMeters, height);
                    float heightRight = SampleHeight(uv + Vector2.right * onePixel);
                    float heightUp = SampleHeight(uv + Vector2.up * onePixel);
                    float lighting = Mathf.Clamp((heightRight - heightUp) / Mathf.Max(1f, heightRange) * 9f, -0.22f, 0.22f);
                    Color color = EvaluateHeightColor(normalized) * (1f + lighting);

                    color.a = 1f;
                    colors[y * generatedResolution + x] = color;
                }
            }

            generatedPreviewTexture.SetPixels32(colors);
            generatedPreviewTexture.Apply(false, false);
            float runtimeWorldSize = previewResolution
                                     / Mathf.Max(1f, pixelsPerUnit);
            float generatedPixelsPerUnit = generatedResolution
                                           / Mathf.Max(0.0001f, runtimeWorldSize);
            generatedMapSprite = Sprite.Create(
                generatedPreviewTexture,
                new Rect(0f, 0f, generatedPreviewTexture.width, generatedPreviewTexture.height),
                new Vector2(0.5f, 0.5f),
                generatedPixelsPerUnit);
            generatedMapSprite.name = "Height Map Visualization";
            generatedMapObject = new GameObject("2D Height Map");
            generatedMapObject.transform.SetParent(transform, false);
            generatedMapObject.transform.localPosition = Vector3.zero;
            generatedMapObject.transform.localRotation = Quaternion.identity;
            generatedMapObject.transform.localScale = Vector3.one;
            if (!Application.isPlaying)
                generatedMapObject.hideFlags = HideFlags.DontSaveInEditor;
            mapRenderer = generatedMapObject.AddComponent<SpriteRenderer>();
            mapRenderer.sprite = generatedMapSprite;
            // The map is the visual background. Keep it well behind every robot
            // marker layer so transparent contour rendering can never win an
            // ambiguous same-plane draw-order comparison.
            mapRenderer.sortingOrder = -1000;
            CreateDynamicContourMaterial();
        }

        private void CreateDynamicContourMaterial()
        {
            Shader shader = dynamicContourShader != null
                ? dynamicContourShader
                : Shader.Find("AnimalGame/Dynamic Height Contours");
            if (shader == null)
            {
                Debug.LogError(
                    "Missing shader: AnimalGame/Dynamic Height Contours. " +
                    "Assign Dynamic Contour Shader on the map prefab so standalone " +
                    "builds keep the shader.",
                    this);
                return;
            }

            contourMaterial = new Material(shader)
            {
                name = Application.isPlaying
                    ? "Runtime Dynamic Contour Material"
                    : "Editor Dynamic Contour Preview"
            };
            if (!Application.isPlaying)
                contourMaterial.hideFlags = HideFlags.DontSaveInEditor;
            contourMaterial.SetTexture("_HeightTex", heightField.SurfaceTexture);
            bool hasPlayableMask = heightField.PlayableMaskTexture != null;
            contourMaterial.SetTexture(
                "_PlayableMaskTex",
                hasPlayableMask
                    ? heightField.PlayableMaskTexture
                    : Texture2D.whiteTexture);
            contourMaterial.SetFloat(
                "_PlayableMaskEnabled",
                hasPlayableMask ? 1f : 0f);
            contourMaterial.SetFloat("_MinimumHeight", minimumHeightMeters);
            contourMaterial.SetFloat("_MaximumHeight", maximumHeightMeters);
            contourMaterial.SetFloat("_ContourInterval", contourIntervalMeters);
            contourMaterial.SetColor("_ContourColor", contourColor);
            RefreshContourMaterialSettings();
            RefreshSurfaceMaterialSettings();
            mapRenderer.material = contourMaterial;
        }

        private void ShowCompleteContourRange()
        {
            if (contourMaterial == null)
                return;

            VisibleMinimumContourHeight = minimumHeightMeters;
            VisibleMaximumContourHeight = maximumHeightMeters;
            RefreshContourMaterialSettings();
            contourMaterial.SetFloat(
                "_VisibleMinimumHeight",
                VisibleMinimumContourHeight);
            contourMaterial.SetFloat(
                "_VisibleMaximumHeight",
                VisibleMaximumContourHeight);
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
            RefreshSurfaceMaterialSettings();
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

        private void RefreshSurfaceMaterialSettings()
        {
            if (contourMaterial == null)
                return;

            bool hasSurface = bakedSurfaceVisual != null;
            bool hasWater = levelAsset != null
                            && levelAsset.BakedStaticWaterMask != null
                            && levelAsset.StaticWaterTexture != null;
            int waterPresentationHash = levelAsset != null
                ? levelAsset.SurfacePresentationHash
                : 0;
            // In Edit Mode the complete baked layer remains visible so the Scene
            // painter can author the fixed map. The player build applies the one
            // inexpensive screen-space cutoff to the otherwise static texture.
            bool revealEnabled = Application.isPlaying
                                 && (hasSurface || hasWater);
            Vector2 centerPixels = surfaceRevealUi != null
                ? surfaceRevealUi.GetUiCenterScreenPoint()
                : new Vector2(
                    Screen.width * 0.5f,
                    Screen.height * 0.5f);
            float radiusPixels = surfaceRevealUi != null
                ? surfaceRevealUi.UiRingRadiusPixels
                  * (surfaceRevealCanvas != null
                      ? surfaceRevealCanvas.scaleFactor
                      : 1f)
                : DefaultSurfaceRevealRadiusPixels;
            radiusPixels = Mathf.Max(1f, radiusPixels);
            float edgePixels = Mathf.Max(0f, surfaceRevealEdgePixels);
            if (surfaceSettingsMaterial == contourMaterial
                && appliedSurfaceVisual == bakedSurfaceVisual
                && appliedSurfaceEnabled == hasSurface
                && appliedSurfaceRevealEnabled == revealEnabled
                && appliedWaterPresentationHash == waterPresentationHash
                && (appliedSurfaceCenterPixels - centerPixels).sqrMagnitude
                <= 0.0001f
                && Mathf.Approximately(appliedSurfaceRadiusPixels, radiusPixels)
                && Mathf.Approximately(appliedSurfaceEdgePixels, edgePixels))
            {
                return;
            }

            contourMaterial.SetTexture(
                "_SurfaceTex",
                hasSurface ? bakedSurfaceVisual : Texture2D.blackTexture);
            contourMaterial.SetFloat("_SurfaceEnabled", hasSurface ? 1f : 0f);
            contourMaterial.SetTexture(
                "_WaterMaskTex",
                hasWater
                    ? levelAsset.BakedStaticWaterMask
                    : Texture2D.blackTexture);
            contourMaterial.SetTexture(
                "_WaterPatternTex",
                hasWater
                    ? levelAsset.StaticWaterTexture
                    : Texture2D.blackTexture);
            contourMaterial.SetFloat("_WaterEnabled", hasWater ? 1f : 0f);
            if (hasWater)
            {
                contourMaterial.SetVector(
                    "_MapSizeMeters",
                    new Vector4(
                        levelAsset.MapSizeMeters.x,
                        levelAsset.MapSizeMeters.y,
                        0f,
                        0f));
                contourMaterial.SetFloat(
                    "_WaterTileSizeMeters",
                    levelAsset.StaticWaterTileSizeMeters);
                Vector2 primarySpeed =
                    levelAsset.StaticWaterLayerOneSpeedMetersPerSecond;
                Vector2 secondarySpeed =
                    levelAsset.StaticWaterLayerTwoSpeedMetersPerSecond;
                contourMaterial.SetVector(
                    "_WaterLayerOneSpeed",
                    new Vector4(primarySpeed.x, primarySpeed.y, 0f, 0f));
                contourMaterial.SetVector(
                    "_WaterLayerTwoSpeed",
                    new Vector4(secondarySpeed.x, secondarySpeed.y, 0f, 0f));
                contourMaterial.SetFloat(
                    "_WaterLayerTwoScale",
                    levelAsset.StaticWaterLayerTwoScale);
                contourMaterial.SetFloat(
                    "_WaterWaveDistortion",
                    levelAsset.StaticWaterWaveDistortion);
                contourMaterial.SetFloat(
                    "_WaterWaveSpeed",
                    levelAsset.StaticWaterWaveSpeed);
                contourMaterial.SetFloat(
                    "_WaterWaveLengthMeters",
                    levelAsset.StaticWaterWaveLengthMeters);
                contourMaterial.SetFloat(
                    "_WaterDeepSpeedMultiplier",
                    levelAsset.StaticWaterDeepSpeedMultiplier);
            }
            contourMaterial.SetFloat(
                "_SurfaceRevealEnabled",
                revealEnabled ? 1f : 0f);
            contourMaterial.SetVector(
                "_SurfaceRevealCenterPixels",
                new Vector4(centerPixels.x, centerPixels.y, 0f, 0f));
            contourMaterial.SetFloat("_SurfaceRevealRadiusPixels", radiusPixels);
            contourMaterial.SetFloat("_SurfaceRevealEdgePixels", edgePixels);
            surfaceSettingsMaterial = contourMaterial;
            appliedSurfaceVisual = bakedSurfaceVisual;
            appliedSurfaceEnabled = hasSurface;
            appliedSurfaceRevealEnabled = revealEnabled;
            appliedWaterPresentationHash = waterPresentationHash;
            appliedSurfaceCenterPixels = centerPixels;
            appliedSurfaceRadiusPixels = radiusPixels;
            appliedSurfaceEdgePixels = edgePixels;
        }

        private Color EvaluateHeightColor(float height)
        {
            return height < 0.55f
                ? Color.Lerp(lowHeightColor, middleHeightColor, height / 0.55f)
                : Color.Lerp(middleHeightColor, highHeightColor, (height - 0.55f) / 0.45f);
        }

        private void OnDestroy()
        {
            ReleaseGeneratedMap();
        }

        private void ReleaseGeneratedMap()
        {
            heightField?.Dispose();
            heightField = null;

            if (contourMaterial != null)
                DestroyGeneratedObject(contourMaterial);
            contourMaterial = null;
            surfaceSettingsMaterial = null;
            appliedWaterPresentationHash = int.MinValue;

            if (generatedMapSprite != null)
                DestroyGeneratedObject(generatedMapSprite);
            generatedMapSprite = null;

            if (generatedPreviewTexture != null)
                DestroyGeneratedObject(generatedPreviewTexture);
            generatedPreviewTexture = null;

            if (generatedMapObject != null)
                DestroyGeneratedObject(generatedMapObject);
            generatedMapObject = null;
            mapRenderer = null;
        }

        private static void DestroyGeneratedObject(Object generatedObject)
        {
            if (generatedObject == null)
                return;

            if (Application.isPlaying)
                Destroy(generatedObject);
            else
                DestroyImmediate(generatedObject);
        }

        private void OnValidate()
        {
            ApplyFixedLevelAsset();
            mapWidthMeters = Mathf.Max(1f, mapWidthMeters);
            mapHeightMeters = Mathf.Max(1f, mapHeightMeters);
            maximumHeightMeters = Mathf.Max(minimumHeightMeters + 0.01f, maximumHeightMeters);
            bakedHeightResolution = Mathf.Clamp(bakedHeightResolution, 128, 2048);
            surfaceSmoothingSigmaMeters = Mathf.Max(0f, surfaceSmoothingSigmaMeters);
            previewResolution = Mathf.Clamp(previewResolution, 128, 8000);
            contourIntervalMeters = Mathf.Max(1f, contourIntervalMeters);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            editorHeightResolution = Mathf.Clamp(
                editorHeightResolution,
                128,
                1024);
            editorPreviewResolution = Mathf.Clamp(
                editorPreviewResolution,
                128,
                2048);
            editorConfigurationHash = int.MinValue;
        }
    }
}
