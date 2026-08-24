using System.IO;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

public sealed class HeightMapStaticWaterPainterWindow : EditorWindow
{
    private const float MinimumBrushRadiusMeters = 0.25f;
    private const float DefaultBrushRadiusMeters = 8f;
    private const int BrushOutlineSegments = 64;
    private const string DefaultWaterTexturePath =
        "Assets/Arts/Textures/Texture_Water.png";
    private const string PreviewShaderName =
        "Hidden/AnimalGame/StaticWaterEditorPreview";

    private HeightMapLevelAsset levelAsset;
    private MapTestSceneController mapController;
    private SerializedObject serializedLevel;
    private Vector2 scrollPosition;
    private StaticWaterDepthPaintMode paintMode =
        StaticWaterDepthPaintMode.Set;
    private float brushRadiusMeters = DefaultBrushRadiusMeters;
    private float targetDepthMeters = 1f;
    private float depthStepMeters = 0.25f;
    private float brushStrength = 1f;
    private float brushHardness = 0.75f;
    private bool paintInScene = true;
    private bool strokeActive;
    private StaticWaterDepthPaintMode strokePaintMode;
    private bool strokeChanged;
    private bool hasLastStrokePosition;
    private Vector2 lastStrokeMapPosition;
    private int strokeUndoGroup = -1;
    private int lastObservedAuthoringHash;
    private bool previewDirty = true;
    private double nextPreviewRefreshTime;
    private int wetPixelCount;
    private float minimumWetDepth;
    private float maximumWetDepth;
    private float averageWetDepth;

    private GameObject previewObject;
    private Mesh previewMesh;
    private MeshRenderer previewRenderer;
    private Material previewMaterial;
    private Texture2D previewDepthTexture;

    [MenuItem("Animal Game/Level/Static Water Painter")]
    public static void Open()
    {
        var window = GetWindow<HeightMapStaticWaterPainterWindow>();
        window.titleContent = new GUIContent("Static Water");
        window.minSize = new Vector2(360f, 520f);
        window.TryAdoptCurrentSelection();
        window.Show();
    }

    public static void OpenForAsset(HeightMapLevelAsset asset)
    {
        var window = GetWindow<HeightMapStaticWaterPainterWindow>();
        window.titleContent = new GUIContent("Static Water");
        window.minSize = new Vector2(360f, 520f);
        window.SetLevelAsset(asset);
        window.Show();
        window.Focus();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("Static Water");
        minSize = new Vector2(360f, 520f);
        SceneView.duringSceneGui += HandleSceneGui;
        Undo.undoRedoPerformed += HandleUndoRedo;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        if (levelAsset == null)
            TryAdoptCurrentSelection();
    }

    private void OnDisable()
    {
        if (strokeActive)
            EndStroke();

        SceneView.duringSceneGui -= HandleSceneGui;
        Undo.undoRedoPerformed -= HandleUndoRedo;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        DestroyPreviewResources();
        serializedLevel = null;
    }

    private void OnFocus()
    {
        ResolveSceneMap();
        RefreshPreview(true);
        Repaint();
    }

    private void OnHierarchyChange()
    {
        ResolveSceneMap();
        previewDirty = true;
        Repaint();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.LabelField("Static Water Authoring", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Paints static-water range and depth directly into the fixed map " +
            "asset. Water at or below the passable-depth threshold remains " +
            "traversable; deeper water blocks movement. The Scene overlay is " +
            "temporary. At runtime, an independently baked range/depth mask " +
            "drives animated water above every terrain texture.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        HeightMapLevelAsset selectedAsset = (HeightMapLevelAsset)
            EditorGUILayout.ObjectField(
                "Fixed Map Asset",
                levelAsset,
                typeof(HeightMapLevelAsset),
                false);
        if (EditorGUI.EndChangeCheck())
            SetLevelAsset(selectedAsset);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Scene Map",
                mapController,
                typeof(MapTestSceneController),
                true);
        }

        if (levelAsset == null)
        {
            EditorGUILayout.HelpBox(
                "Select a HeightMapLevelAsset or a scene MapTestController.",
                MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawWaterMapSettings();
        DrawBrushSettings();
        DrawWaterStatistics();
        DrawBulkActions();

        if (mapController == null || !mapController.HasGeneratedMap)
        {
            EditorGUILayout.HelpBox(
                "Open the scene that uses this level asset to paint and preview " +
                "water in the Scene view.",
                MessageType.Warning);
        }

        EditorGUILayout.EndScrollView();

        if (previewDirty
            && EditorApplication.timeSinceStartup >= nextPreviewRefreshTime)
        {
            RefreshPreview(false);
        }
    }

    private void DrawWaterMapSettings()
    {
        if (serializedLevel == null)
            serializedLevel = new SerializedObject(levelAsset);

        serializedLevel.Update();
        SerializedProperty resolution = serializedLevel.FindProperty(
            "staticWaterResolution");
        SerializedProperty maximumDepth = serializedLevel.FindProperty(
            "maximumStaticWaterDepthMeters");
        SerializedProperty passableDepth = serializedLevel.FindProperty(
            "maximumPassableStaticWaterDepthMeters");
        SerializedProperty previewTexture = serializedLevel.FindProperty(
            "staticWaterEditorTexture");
        SerializedProperty previewTint = serializedLevel.FindProperty(
            "staticWaterEditorTint");
        SerializedProperty previewTileSize = serializedLevel.FindProperty(
            "staticWaterEditorTileSizeMeters");
        SerializedProperty layerOneSpeed = serializedLevel.FindProperty(
            "staticWaterLayerOneSpeedMetersPerSecond");
        SerializedProperty layerTwoSpeed = serializedLevel.FindProperty(
            "staticWaterLayerTwoSpeedMetersPerSecond");
        SerializedProperty layerTwoScale = serializedLevel.FindProperty(
            "staticWaterLayerTwoScale");
        SerializedProperty waveDistortion = serializedLevel.FindProperty(
            "staticWaterWaveDistortion");
        SerializedProperty waveSpeed = serializedLevel.FindProperty(
            "staticWaterWaveSpeed");
        SerializedProperty waveLength = serializedLevel.FindProperty(
            "staticWaterWaveLengthMeters");
        SerializedProperty deepSpeedMultiplier = serializedLevel.FindProperty(
            "staticWaterDeepSpeedMultiplier");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Water Map", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(resolution, new GUIContent("Resolution"));
        EditorGUILayout.PropertyField(
            maximumDepth,
            new GUIContent("Maximum Depth (m)"));
        EditorGUILayout.PropertyField(
            passableDepth,
            new GUIContent("Maximum Passable Depth (m)"));
        EditorGUILayout.PropertyField(
            previewTexture,
            new GUIContent("Preview Texture"));
        EditorGUILayout.PropertyField(
            previewTint,
            new GUIContent("Preview Tint"));
        EditorGUILayout.PropertyField(
            previewTileSize,
            new GUIContent("Water Tile Size (m)"));
        EditorGUILayout.PropertyField(
            layerOneSpeed,
            new GUIContent("Primary Speed (m/s)"));
        EditorGUILayout.PropertyField(
            layerTwoSpeed,
            new GUIContent("Secondary Speed (m/s)"));
        EditorGUILayout.PropertyField(
            layerTwoScale,
            new GUIContent("Secondary Scale"));
        EditorGUILayout.PropertyField(
            waveDistortion,
            new GUIContent("Wave Distortion"));
        EditorGUILayout.PropertyField(
            waveSpeed,
            new GUIContent("Wave Speed"));
        EditorGUILayout.PropertyField(
            waveLength,
            new GUIContent("Wave Length (m)"));
        EditorGUILayout.PropertyField(
            deepSpeedMultiplier,
            new GUIContent("Deep Speed Multiplier"));
        bool settingsChanged = EditorGUI.EndChangeCheck();
        serializedLevel.ApplyModifiedProperties();

        if (!settingsChanged)
            return;

        bool resized = levelAsset.EnsureStaticWaterAuthoringData();
        targetDepthMeters = Mathf.Clamp(
            targetDepthMeters,
            0f,
            levelAsset.MaximumStaticWaterDepthMeters);
        EditorUtility.SetDirty(levelAsset);
        if (resized)
            AssetDatabase.SaveAssetIfDirty(levelAsset);
        BakeRuntimeWaterMask();
        RefreshPreview(true);
    }

    private void DrawBrushSettings()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Depth Brush", EditorStyles.boldLabel);
        paintInScene = EditorGUILayout.Toggle("Paint In Scene", paintInScene);
        paintMode = (StaticWaterDepthPaintMode)EditorGUILayout.EnumPopup(
            "Mode",
            paintMode);
        brushRadiusMeters = Mathf.Max(
            MinimumBrushRadiusMeters,
            EditorGUILayout.FloatField("Radius (m)", brushRadiusMeters));

        using (new EditorGUI.DisabledScope(
                   paintMode != StaticWaterDepthPaintMode.Set))
        {
            targetDepthMeters = EditorGUILayout.Slider(
                "Target Depth (m)",
                targetDepthMeters,
                0f,
                levelAsset.MaximumStaticWaterDepthMeters);
        }

        using (new EditorGUI.DisabledScope(
                   paintMode != StaticWaterDepthPaintMode.Add
                   && paintMode != StaticWaterDepthPaintMode.Subtract))
        {
            depthStepMeters = Mathf.Max(
                0.01f,
                EditorGUILayout.FloatField(
                    "Depth Change Per Dab (m)",
                    depthStepMeters));
        }

        brushStrength = EditorGUILayout.Slider(
            "Strength",
            brushStrength,
            0.01f,
            1f);
        brushHardness = EditorGUILayout.Slider(
            "Hardness",
            brushHardness,
            0f,
            1f);
        EditorGUILayout.HelpBox(
            "Left-drag applies the selected brush. Hold Shift while left-dragging " +
            "to erase, regardless of the selected mode. Alt remains available " +
            "for normal Scene-view navigation.",
            MessageType.None);
    }

    private void DrawWaterStatistics()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Current Water", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Wet authoring pixels", wetPixelCount.ToString());
        if (wetPixelCount <= 0)
        {
            EditorGUILayout.LabelField("Depth range", "Dry map");
            return;
        }

        EditorGUILayout.LabelField(
            "Depth range",
            $"{minimumWetDepth:0.00} m - {maximumWetDepth:0.00} m");
        EditorGUILayout.LabelField(
            "Average depth",
            $"{averageWetDepth:0.00} m");
    }

    private void DrawBulkActions()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Whole Map", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Fill At Target Depth"))
            {
                if (EditorUtility.DisplayDialog(
                        "Fill Static Water",
                        $"Fill the complete map with {targetDepthMeters:0.00} m " +
                        "static water?",
                        "Fill",
                        "Cancel"))
                {
                    FillWater(targetDepthMeters, "Fill Static Water");
                }
            }

            if (GUILayout.Button("Clear All Water"))
            {
                if (EditorUtility.DisplayDialog(
                        "Clear Static Water",
                        "Erase all authored static water from this map?",
                        "Clear",
                        "Cancel"))
                {
                    FillWater(0f, "Clear Static Water");
                }
            }
        }
    }

    private void HandleSceneGui(SceneView sceneView)
    {
        if (levelAsset == null
            || mapController == null
            || !mapController.HasGeneratedMap)
        {
            return;
        }

        EnsurePreviewResources();
        if (previewRenderer != null)
            previewRenderer.enabled = paintInScene && !EditorApplication.isPlaying;
        if (!paintInScene || EditorApplication.isPlaying)
            return;

        Event current = Event.current;
        int controlId = GUIUtility.GetControlID(
            "HeightMapStaticWaterPainter".GetHashCode(),
            FocusType.Passive);
        if (!TryGetMapPosition(current.mousePosition, out Vector2 mapPosition))
        {
            if (current.type == EventType.MouseUp
                && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                EndStroke();
                current.Use();
            }

            return;
        }

        DrawBrushOutline(mapPosition, current.shift);
        if (current.type == EventType.Layout)
            HandleUtility.AddDefaultControl(controlId);

        if (current.alt || current.button != 0)
            return;

        if (current.type == EventType.MouseDown)
        {
            GUIUtility.hotControl = controlId;
            BeginStroke(mapPosition, current.shift);
            current.Use();
        }
        else if (current.type == EventType.MouseDrag
                 && GUIUtility.hotControl == controlId)
        {
            PaintStrokeTo(mapPosition);
            SchedulePreviewRefresh();
            current.Use();
            sceneView.Repaint();
        }
        else if (current.type == EventType.MouseUp
                 && GUIUtility.hotControl == controlId)
        {
            PaintStrokeTo(mapPosition);
            GUIUtility.hotControl = 0;
            EndStroke();
            current.Use();
            sceneView.Repaint();
        }

        if (previewDirty
            && EditorApplication.timeSinceStartup >= nextPreviewRefreshTime)
        {
            RefreshPreview(false);
        }
    }

    private bool TryGetMapPosition(
        Vector2 sceneGuiPosition,
        out Vector2 mapPosition)
    {
        mapPosition = default;
        Bounds bounds = mapController.WorldBounds;
        Ray ray = HandleUtility.GUIPointToWorldRay(sceneGuiPosition);
        var mapPlane = new Plane(Vector3.forward, bounds.center);
        if (!mapPlane.Raycast(ray, out float distance))
            return false;

        Vector3 world = ray.GetPoint(distance);
        return mapController.TrySampleWorldPosition(
            (Vector2)world,
            out mapPosition,
            out _);
    }

    private void DrawBrushOutline(Vector2 centreMapPosition, bool erase)
    {
        var points = new Vector3[BrushOutlineSegments + 1];
        for (int index = 0; index <= BrushOutlineSegments; index++)
        {
            float angle = index / (float)BrushOutlineSegments
                          * Mathf.PI * 2f;
            Vector2 mapPoint = centreMapPosition + new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) * brushRadiusMeters;
            points[index] = mapController.MapPositionToWorld(mapPoint);
            points[index].z -= 0.08f;
        }

        Color outlineColor = erase
            ? new Color(1f, 0.28f, 0.2f, 0.98f)
            : levelAsset.StaticWaterEditorTint;
        outlineColor.a = 0.98f;
        Handles.color = outlineColor;
        Handles.DrawAAPolyLine(3f, points);

        string label = erase
            ? "Erase water"
            : paintMode == StaticWaterDepthPaintMode.Set
                ? $"Set {targetDepthMeters:0.00} m"
                : paintMode.ToString();
        Vector3 labelPosition = mapController.MapPositionToWorld(
            centreMapPosition + Vector2.up * brushRadiusMeters);
        labelPosition.z -= 0.09f;
        Handles.Label(labelPosition, label, EditorStyles.miniBoldLabel);
    }

    private void BeginStroke(Vector2 mapPosition, bool erase)
    {
        strokeActive = true;
        strokeChanged = false;
        hasLastStrokePosition = false;
        strokePaintMode = erase
            ? StaticWaterDepthPaintMode.Erase
            : paintMode;

        Undo.IncrementCurrentGroup();
        strokeUndoGroup = Undo.GetCurrentGroup();
        string operationName = strokePaintMode
                               == StaticWaterDepthPaintMode.Erase
            ? "Erase Static Water"
            : $"{strokePaintMode} Static Water Depth";
        Undo.SetCurrentGroupName(operationName);
        Undo.RegisterCompleteObjectUndo(levelAsset, operationName);
        PaintStrokeTo(mapPosition);
    }

    private void PaintStrokeTo(Vector2 mapPosition)
    {
        if (!strokeActive)
            return;

        if (!hasLastStrokePosition)
        {
            PaintDab(mapPosition);
            lastStrokeMapPosition = mapPosition;
            hasLastStrokePosition = true;
            return;
        }

        float distance = Vector2.Distance(lastStrokeMapPosition, mapPosition);
        float spacing = Mathf.Max(0.05f, brushRadiusMeters * 0.25f);
        int stepCount = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
        for (int step = 1; step <= stepCount; step++)
        {
            PaintDab(Vector2.Lerp(
                lastStrokeMapPosition,
                mapPosition,
                step / (float)stepCount));
        }

        lastStrokeMapPosition = mapPosition;
    }

    private void PaintDab(Vector2 mapPosition)
    {
        if (!levelAsset.PaintStaticWaterDepth(
                mapPosition,
                brushRadiusMeters,
                strokePaintMode,
                targetDepthMeters,
                depthStepMeters,
                brushStrength,
                brushHardness))
        {
            return;
        }

        strokeChanged = true;
        previewDirty = true;
        EditorUtility.SetDirty(levelAsset);
    }

    private void EndStroke()
    {
        if (!strokeActive)
            return;

        strokeActive = false;
        hasLastStrokePosition = false;
        if (strokeUndoGroup >= 0)
            Undo.CollapseUndoOperations(strokeUndoGroup);
        strokeUndoGroup = -1;

        if (strokeChanged)
        {
            AssetDatabase.SaveAssetIfDirty(levelAsset);
            BakeRuntimeWaterMask();
            RefreshPreview(true);
        }

        strokeChanged = false;
        Repaint();
    }

    private void FillWater(float depthMeters, string operationName)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(operationName);
        Undo.RegisterCompleteObjectUndo(levelAsset, operationName);
        if (levelAsset.FillStaticWaterDepth(depthMeters))
        {
            EditorUtility.SetDirty(levelAsset);
            AssetDatabase.SaveAssetIfDirty(levelAsset);
            BakeRuntimeWaterMask();
            RefreshPreview(true);
        }

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
    }

    private void SchedulePreviewRefresh()
    {
        previewDirty = true;
        nextPreviewRefreshTime = EditorApplication.timeSinceStartup + 0.05d;
    }

    private void RefreshPreview(bool force)
    {
        if (levelAsset == null)
            return;
        if (!force
            && EditorApplication.timeSinceStartup < nextPreviewRefreshTime)
        {
            return;
        }

        EnsurePreviewResources();
        byte[] depthMap = levelAsset.GetOrCreateStaticWaterDepthMap();
        int resolution = levelAsset.StaticWaterResolution;
        if (previewDepthTexture == null
            || previewDepthTexture.width != resolution
            || previewDepthTexture.height != resolution)
        {
            if (previewDepthTexture != null)
                DestroyImmediate(previewDepthTexture);
            previewDepthTexture = new Texture2D(
                resolution,
                resolution,
                TextureFormat.R8,
                false,
                true)
            {
                name = "Static Water Depth Preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        previewDepthTexture.LoadRawTextureData(depthMap);
        previewDepthTexture.Apply(false, false);
        UpdateStatistics(depthMap);
        UpdatePreviewMaterial();
        lastObservedAuthoringHash =
            levelAsset.CalculateStaticWaterAuthoringHash();
        previewDirty = false;
        nextPreviewRefreshTime = EditorApplication.timeSinceStartup;
        SceneView.RepaintAll();
        Repaint();
    }

    private void UpdateStatistics(byte[] depthMap)
    {
        wetPixelCount = 0;
        minimumWetDepth = float.PositiveInfinity;
        maximumWetDepth = 0f;
        double depthSum = 0d;
        float maximumDepth = levelAsset.MaximumStaticWaterDepthMeters;
        foreach (byte encodedDepth in depthMap)
        {
            if (encodedDepth == 0)
                continue;

            float depth = encodedDepth / (float)byte.MaxValue * maximumDepth;
            wetPixelCount++;
            minimumWetDepth = Mathf.Min(minimumWetDepth, depth);
            maximumWetDepth = Mathf.Max(maximumWetDepth, depth);
            depthSum += depth;
        }

        if (wetPixelCount <= 0)
        {
            minimumWetDepth = 0f;
            averageWetDepth = 0f;
            return;
        }

        averageWetDepth = (float)(depthSum / wetPixelCount);
    }

    private void EnsurePreviewResources()
    {
        if (mapController == null || !mapController.HasGeneratedMap)
            return;

        Shader previewShader = Shader.Find(PreviewShaderName);
        if (previewShader == null)
            return;

        if (previewMaterial == null)
        {
            previewMaterial = new Material(previewShader)
            {
                name = "Static Water Editor Preview Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (previewObject == null)
        {
            previewObject = new GameObject("Static Water Editor Preview")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var meshFilter = previewObject.AddComponent<MeshFilter>();
            previewRenderer = previewObject.AddComponent<MeshRenderer>();
            previewRenderer.sharedMaterial = previewMaterial;
            previewMesh = new Mesh
            {
                name = "Static Water Editor Preview Mesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            meshFilter.sharedMesh = previewMesh;
        }

        UpdatePreviewMesh();
        if (previewRenderer != null)
            previewRenderer.enabled = paintInScene && !EditorApplication.isPlaying;
    }

    private void UpdatePreviewMesh()
    {
        if (previewMesh == null || mapController == null)
            return;

        Vector3 bottomLeft = mapController.MapPositionToWorld(Vector2.zero);
        Vector3 bottomRight = mapController.MapPositionToWorld(
            new Vector2(levelAsset.MapSizeMeters.x, 0f));
        Vector3 topLeft = mapController.MapPositionToWorld(
            new Vector2(0f, levelAsset.MapSizeMeters.y));
        Vector3 topRight = mapController.MapPositionToWorld(
            levelAsset.MapSizeMeters);
        bottomLeft.z -= 0.04f;
        bottomRight.z -= 0.04f;
        topLeft.z -= 0.04f;
        topRight.z -= 0.04f;

        previewMesh.Clear();
        previewMesh.vertices = new[]
        {
            bottomLeft,
            bottomRight,
            topLeft,
            topRight
        };
        previewMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        previewMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        previewMesh.RecalculateBounds();
    }

    private void UpdatePreviewMaterial()
    {
        if (previewMaterial == null || levelAsset == null)
            return;

        Texture2D pattern = levelAsset.StaticWaterTexture;
        if (pattern == null)
        {
            pattern = AssetDatabase.LoadAssetAtPath<Texture2D>(
                DefaultWaterTexturePath);
        }

        previewMaterial.SetTexture("_DepthTex", previewDepthTexture);
        previewMaterial.SetTexture("_MainTex", pattern != null
            ? pattern
            : Texture2D.whiteTexture);
        previewMaterial.SetColor("_Tint", levelAsset.StaticWaterEditorTint);
        float tileSize = Mathf.Max(
            0.1f,
            levelAsset.StaticWaterTileSizeMeters);
        previewMaterial.SetVector(
            "_TileScale",
            new Vector4(
                levelAsset.MapSizeMeters.x / tileSize,
                levelAsset.MapSizeMeters.y / tileSize,
                0f,
                0f));
        previewMaterial.SetFloat(
            "_PassableDepthNormalized",
            levelAsset.MaximumPassableStaticWaterDepthMeters
            / Mathf.Max(0.1f, levelAsset.MaximumStaticWaterDepthMeters));
    }

    private void DestroyPreviewResources()
    {
        if (previewObject != null)
            DestroyImmediate(previewObject);
        if (previewMesh != null)
            DestroyImmediate(previewMesh);
        if (previewMaterial != null)
            DestroyImmediate(previewMaterial);
        if (previewDepthTexture != null)
            DestroyImmediate(previewDepthTexture);

        previewObject = null;
        previewMesh = null;
        previewRenderer = null;
        previewMaterial = null;
        previewDepthTexture = null;
    }

    private void HandleUndoRedo()
    {
        if (levelAsset == null)
            return;

        levelAsset.EnsureStaticWaterAuthoringData();
        int currentHash = levelAsset.CalculateStaticWaterAuthoringHash();
        if (currentHash == lastObservedAuthoringHash)
            return;

        BakeRuntimeWaterMask();
        RefreshPreview(true);
    }

    private void BakeRuntimeWaterMask()
    {
        if (levelAsset == null)
            return;

        HeightMapStaticWaterMaskBaker.Bake(levelAsset, false);
    }

    private void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (previewRenderer == null)
            return;

        previewRenderer.enabled = paintInScene
                                  && state == PlayModeStateChange.EnteredEditMode;
    }

    private void TryAdoptCurrentSelection()
    {
        if (Selection.activeObject is HeightMapLevelAsset selectedLevel)
        {
            SetLevelAsset(selectedLevel);
            return;
        }

        GameObject selectedObject = Selection.activeGameObject;
        MapTestSceneController selectedMap = selectedObject != null
            ? selectedObject.GetComponentInParent<MapTestSceneController>()
            : null;
        if (selectedMap != null)
        {
            SetLevelAsset(selectedMap.LevelAsset);
            return;
        }

        MapTestSceneController[] openMaps =
            Object.FindObjectsOfType<MapTestSceneController>(true);
        if (openMaps.Length > 0)
            SetLevelAsset(openMaps[0].LevelAsset);
    }

    private void SetLevelAsset(HeightMapLevelAsset asset)
    {
        if (levelAsset == asset && mapController != null)
            return;

        if (strokeActive)
            EndStroke();
        DestroyPreviewResources();
        levelAsset = asset;
        serializedLevel = levelAsset != null
            ? new SerializedObject(levelAsset)
            : null;
        if (levelAsset != null && levelAsset.EnsureStaticWaterAuthoringData())
        {
            EditorUtility.SetDirty(levelAsset);
            AssetDatabase.SaveAssetIfDirty(levelAsset);
        }

        ResolveSceneMap();
        lastObservedAuthoringHash = levelAsset != null
            ? levelAsset.CalculateStaticWaterAuthoringHash()
            : 0;
        RefreshPreview(true);
        Repaint();
        SceneView.RepaintAll();
    }

    private void ResolveSceneMap()
    {
        MapTestSceneController previousMap = mapController;
        mapController = null;
        if (levelAsset != null)
        {
            MapTestSceneController[] openMaps =
                Object.FindObjectsOfType<MapTestSceneController>(true);
            foreach (MapTestSceneController candidate in openMaps)
            {
                if (candidate != null
                    && !EditorUtility.IsPersistent(candidate)
                    && candidate.LevelAsset == levelAsset)
                {
                    mapController = candidate;
                    break;
                }
            }
        }

        if (previousMap != mapController)
        {
            DestroyPreviewResources();
            previewDirty = true;
        }
    }
}

internal static class HeightMapStaticWaterMaskBaker
{
    public static bool Bake(HeightMapLevelAsset level, bool registerUndo)
    {
        if (level == null)
        {
            Debug.LogError(
                "Static-water mask baking requires a fixed map asset.");
            return false;
        }

        if (level.EnsureStaticWaterAuthoringData())
            EditorUtility.SetDirty(level);

        string levelPath = AssetDatabase.GetAssetPath(level);
        if (string.IsNullOrEmpty(levelPath))
        {
            Debug.LogError(
                "Save the HeightMapLevelAsset before baking static water.");
            return false;
        }

        byte[] depthMap = level.GetOrCreateStaticWaterDepthMap();
        int resolution = level.StaticWaterResolution;
        var pixels = new Color32[depthMap.Length];
        for (int index = 0; index < depthMap.Length; index++)
        {
            byte encodedDepth = depthMap[index];
            pixels[index] = new Color32(
                encodedDepth > 0 ? byte.MaxValue : (byte)0,
                encodedDepth,
                0,
                byte.MaxValue);
        }

        Texture2D output = null;
        try
        {
            output = new Texture2D(
                resolution,
                resolution,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Baked Static Water Data",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            output.SetPixels32(pixels);
            output.Apply(false, false);

            string directory = Path.GetDirectoryName(levelPath);
            string fileName = Path.GetFileNameWithoutExtension(levelPath)
                              + "_WaterMask.png";
            string outputPath = Path.Combine(directory ?? "Assets", fileName)
                .Replace('\\', '/');
            File.WriteAllBytes(outputPath, output.EncodeToPNG());
            AssetDatabase.ImportAsset(
                outputPath,
                ImportAssetOptions.ForceSynchronousImport
                | ImportAssetOptions.ForceUpdate);
            ConfigureImporter(outputPath, resolution);

            Texture2D bakedMask = AssetDatabase.LoadAssetAtPath<Texture2D>(
                outputPath);
            if (bakedMask == null)
            {
                Debug.LogError(
                    $"Unity could not load the static-water mask at {outputPath}.");
                return false;
            }

            if (registerUndo)
                Undo.RecordObject(level, "Bake Static Water Mask");
            level.SetBakedStaticWaterMask(bakedMask);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            Debug.Log($"Baked static-water range and depth: {outputPath}", level);
            return true;
        }
        finally
        {
            if (output != null)
                Object.DestroyImmediate(output);
        }
    }

    private static void ConfigureImporter(string assetPath, int resolution)
    {
        TextureImporter importer =
            AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = Mathf.Clamp(resolution, 64, 8192);
        importer.SaveAndReimport();
    }
}
