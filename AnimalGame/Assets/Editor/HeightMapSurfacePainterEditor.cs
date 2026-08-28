using System;
using System.Collections.Generic;
using System.IO;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HeightMapLevelAsset))]
public sealed class HeightMapLevelAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Open Terrain Surface Painter"))
        {
            HeightMapSurfacePainterWindow.OpenForAsset(
                (HeightMapLevelAsset)target);
        }

        if (GUILayout.Button("Open Static Water Painter"))
        {
            HeightMapStaticWaterPainterWindow.OpenForAsset(
                (HeightMapLevelAsset)target);
        }
    }
}

public sealed class HeightMapSurfacePainterWindow : EditorWindow
{
    private const float MinimumBrushRadiusMeters = 0.25f;
    private const float DefaultBrushRadiusMeters = 10f;
    private const int BrushOutlineSegments = 64;

    private HeightMapLevelAsset levelAsset;
    private MapTestSceneController mapController;
    private Vector2 scrollPosition;
    private float brushRadiusMeters = DefaultBrushRadiusMeters;
    private int selectedTerrainId = 1;
    private bool paintInScene = true;
    private bool strokeActive;
    private int strokeTerrainId;
    private bool strokeChanged;
    private bool hasLastStrokePosition;
    private Vector2 lastStrokeMapPosition;
    private int strokeUndoGroup = -1;
    private int lastObservedAuthoringHash;
    private bool delayedUpgradeBakeQueued;
    private SerializedObject serializedLevel;

    [MenuItem("Animal Game/Level/Terrain Surface Painter")]
    public static void Open()
    {
        var window = GetWindow<HeightMapSurfacePainterWindow>();
        window.titleContent = new GUIContent("Surface Painter");
        window.minSize = new Vector2(390f, 520f);
        window.TryAdoptCurrentSelection();
        window.Show();
    }

    public static void OpenForAsset(HeightMapLevelAsset asset)
    {
        Open();
        var window = GetWindow<HeightMapSurfacePainterWindow>();
        window.SetLevelAsset(asset);
        window.Focus();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("Surface Painter");
        minSize = new Vector2(390f, 520f);
        SceneView.duringSceneGui += HandleSceneGui;
        Undo.undoRedoPerformed += HandleUndoRedo;
        if (levelAsset == null)
            TryAdoptCurrentSelection();
        QueueBakeUpgradeIfNeeded();
    }

    private void OnDisable()
    {
        if (strokeActive)
            EndStroke();

        SceneView.duringSceneGui -= HandleSceneGui;
        Undo.undoRedoPerformed -= HandleUndoRedo;
        EditorApplication.delayCall -= HandleDelayedUpgradeBake;
        delayedUpgradeBakeQueued = false;
        serializedLevel = null;
    }

    private void OnFocus()
    {
        ResolveSceneMap();
        Repaint();
    }

    private void OnHierarchyChange()
    {
        ResolveSceneMap();
        Repaint();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.LabelField(
            "Permanent Multi-Terrain Surface",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Paints stable terrain IDs directly into the fixed map asset. " +
            "The Editor bakes every pattern and transition into one static PNG; " +
            "runtime only displays that finished image inside the robot UI range.",
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
                "Select MainHeightMapLevel or a scene MapTestController, then " +
                "open this window again.",
                MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawSurfaceSettings();
        DrawPaletteSelector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
        brushRadiusMeters = EditorGUILayout.Slider(
            "Radius (map metres)",
            brushRadiusMeters,
            MinimumBrushRadiusMeters,
            Mathf.Max(25f, levelAsset.MapSizeMeters.magnitude * 0.25f));
        paintInScene = GUILayout.Toggle(
            paintInScene,
            paintInScene ? "Scene Painting Enabled" : "Scene Painting Disabled",
            "Button");

        TerrainSurfaceDefinition selectedDefinition =
            levelAsset.GetSurfaceDefinition(selectedTerrainId);
        bool canPaintSelected = selectedDefinition != null
                                && selectedDefinition.IsUsable;
        using (new EditorGUI.DisabledScope(
                   !levelAsset.HasUsableSurfaceDefinitions))
        {
            if (GUILayout.Button("Bake Current Terrain Map"))
                BakeAndObserve(true);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canPaintSelected))
                {
                    if (GUILayout.Button("Fill With Selected"))
                        FillType(selectedTerrainId);
                }

                if (GUILayout.Button("Clear All Terrain"))
                    FillType(0);
            }
        }

        if (!levelAsset.HasUsableSurfaceDefinitions)
        {
            EditorGUILayout.HelpBox(
                "Add at least one Palette entry with a Pattern Texture before " +
                "painting or baking.",
                MessageType.Error);
        }
        else if (!canPaintSelected)
        {
            EditorGUILayout.HelpBox(
                "The selected terrain has no Pattern Texture. Assign one or " +
                "select another Palette entry.",
                MessageType.Warning);
        }
        else if (mapController == null)
        {
            EditorGUILayout.HelpBox(
                "The selected level is not present in the open scene. Baking " +
                "still works, but Scene painting needs its MapTestController.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Scene controls\n" +
            "• Choose a terrain ID above, then left-drag to paint it\n" +
            "• Shift + left-drag: erase to ID 0\n" +
            "• Alt + mouse: normal Scene camera navigation\n" +
            "• Ctrl+Z / Ctrl+Y: undo or redo and automatically rebake",
            MessageType.None);

        EditorGUILayout.EndScrollView();
        if (GUI.changed)
            SceneView.RepaintAll();
    }

    private void DrawSurfaceSettings()
    {
        if (serializedLevel == null
            || serializedLevel.targetObject != levelAsset)
        {
            serializedLevel = new SerializedObject(levelAsset);
        }

        SerializedProperty palette = serializedLevel.FindProperty(
            "surfacePalette");
        SerializedProperty transitionWidth = serializedLevel.FindProperty(
            "surfaceTransitionWidthMeters");
        SerializedProperty alphaCoreWidth = serializedLevel.FindProperty(
            "surfaceAlphaCoreWidthMeters");
        SerializedProperty alphaBlendShare = serializedLevel.FindProperty(
            "surfaceAlphaBlendShare");
        SerializedProperty boundaryNoiseScale = serializedLevel.FindProperty(
            "surfaceBoundaryNoiseScaleMeters");
        SerializedProperty boundaryNoiseAmplitude = serializedLevel.FindProperty(
            "surfaceBoundaryNoiseAmplitudeMeters");
        SerializedProperty scatterCellSize = serializedLevel.FindProperty(
            "surfaceScatterCellSizeMeters");
        SerializedProperty scatterStrength = serializedLevel.FindProperty(
            "surfaceScatterStrength");
        SerializedProperty noiseSeed = serializedLevel.FindProperty(
            "surfaceNoiseSeed");
        SerializedProperty bakeResolution = serializedLevel.FindProperty(
            "surfaceBakeResolution");
        SerializedProperty maskResolution = serializedLevel.FindProperty(
            "surfaceMaskResolution");
        SerializedProperty patternAlphaNormalization =
            serializedLevel.FindProperty(
                "surfacePatternAlphaNormalizationStrength");
        SerializedProperty closedContourAlphaEnabled =
            serializedLevel.FindProperty(
                "surfaceClosedContourAlphaEnabled");
        SerializedProperty closedContourAlphaResolution =
            serializedLevel.FindProperty(
                "surfaceClosedContourAlphaResolution");
        SerializedProperty closedContourEdgeAlpha =
            serializedLevel.FindProperty(
                "surfaceClosedContourEdgeAlphaMultiplier");
        SerializedProperty closedContourEdgeHoldDistance =
            serializedLevel.FindProperty(
                "surfaceClosedContourEdgeHoldDistanceMeters");
        SerializedProperty closedContourFadeDistance =
            serializedLevel.FindProperty(
                "surfaceClosedContourFadeDistanceMeters");
        SerializedProperty closedContourCenterAlpha =
            serializedLevel.FindProperty(
                "surfaceClosedContourCenterAlphaMultiplier");
        SerializedProperty closedContourDistanceCurve =
            serializedLevel.FindProperty(
                "surfaceClosedContourDistanceCurve");
        SerializedProperty closedContourMinimumArea =
            serializedLevel.FindProperty(
                "surfaceClosedContourMinimumAreaSquareMeters");
        SerializedProperty outsideClosedContourAlpha =
            serializedLevel.FindProperty(
                "surfaceOutsideClosedContourAlphaMultiplier");
        SerializedProperty revealEdge = serializedLevel.FindProperty(
            "surfaceRevealEdgePixels");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Terrain Palette", EditorStyles.boldLabel);
        // Keep the same SerializedObject alive while a text field owns keyboard
        // focus. Recreating it every IMGUI repaint discarded an unfinished
        // numeric edit and made text-only values such as Tile Size Meters snap
        // back to their previous serialized value (usually the default 8).
        serializedLevel.UpdateIfRequiredOrScript();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(palette, true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Baked Transition Settings",
            EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(transitionWidth);
        EditorGUILayout.PropertyField(alphaCoreWidth);
        EditorGUILayout.PropertyField(alphaBlendShare);
        EditorGUILayout.PropertyField(boundaryNoiseScale);
        EditorGUILayout.PropertyField(boundaryNoiseAmplitude);
        EditorGUILayout.PropertyField(
            scatterCellSize,
            new GUIContent("Boundary Detail Scale (metres)"));
        EditorGUILayout.PropertyField(
            scatterStrength,
            new GUIContent("Boundary Detail Strength"));
        EditorGUILayout.PropertyField(noiseSeed);
        EditorGUILayout.PropertyField(
            bakeResolution,
            new GUIContent("Baked Texture Resolution"));
        EditorGUILayout.PropertyField(
            maskResolution,
            new GUIContent("Terrain ID Map Resolution"));
        EditorGUILayout.PropertyField(
            patternAlphaNormalization,
            new GUIContent("Pattern Alpha Normalization"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Static Closed-Contour Alpha",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "This gradient is calculated only when the fixed map is baked. " +
            "It is stored permanently in the runtime texture and never reads " +
            "the player position or changes during gameplay. The map's four " +
            "straight sides close contour lines that reach an edge.",
            MessageType.Info);
        EditorGUILayout.PropertyField(
            closedContourAlphaEnabled,
            new GUIContent("Enable Static Contour Alpha"));
        using (new EditorGUI.DisabledScope(
                   !closedContourAlphaEnabled.boolValue))
        {
            EditorGUILayout.PropertyField(
                closedContourAlphaResolution,
                new GUIContent("Contour Analysis Resolution"));
            EditorGUILayout.PropertyField(
                closedContourEdgeAlpha,
                new GUIContent("Boundary Alpha Multiplier"));
            EditorGUILayout.PropertyField(
                closedContourEdgeHoldDistance,
                new GUIContent("Full Edge Width (metres)"));
            EditorGUILayout.PropertyField(
                closedContourFadeDistance,
                new GUIContent("Transparent Distance (metres)"));
            EditorGUILayout.PropertyField(
                closedContourCenterAlpha,
                new GUIContent("Centre Alpha Multiplier"));
            EditorGUILayout.PropertyField(
                closedContourDistanceCurve,
                new GUIContent("Interior Fade Strength"));
            EditorGUILayout.PropertyField(
                closedContourMinimumArea,
                new GUIContent("Minimum Region Area (m²)"));
            EditorGUILayout.PropertyField(
                outsideClosedContourAlpha,
                new GUIContent("Outside Region Alpha Multiplier"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Reveal", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            revealEdge,
            new GUIContent("UI Reveal Edge (pixels)"));
        bool propertiesChanged = EditorGUI.EndChangeCheck();
        serializedLevel.ApplyModifiedProperties();

        bool normalized = levelAsset.EnsureSurfaceAuthoringData();
        if (propertiesChanged || normalized)
            EditorUtility.SetDirty(levelAsset);
        EnsureSelectedTerrain();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Baked Runtime Texture",
                levelAsset.BakedSurfaceVisual,
                typeof(Texture2D),
                false);
        }
    }

    private void DrawPaletteSelector()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Paint Terrain", EditorStyles.boldLabel);
        IReadOnlyList<TerrainSurfaceDefinition> palette =
            levelAsset.SurfacePalette;
        if (palette == null || palette.Count == 0)
            return;

        foreach (TerrainSurfaceDefinition definition in palette)
        {
            if (definition == null)
                continue;

            Color previousBackground = GUI.backgroundColor;
            Color buttonColor = definition.Tint;
            buttonColor.a = 1f;
            GUI.backgroundColor = buttonColor;
            bool selected = definition.TerrainId == selectedTerrainId;
            string textureStatus = definition.PatternTexture != null
                ? string.Empty
                : " (texture missing)";
            if (GUILayout.Toggle(
                    selected,
                    $"ID {definition.TerrainId}: {definition.DisplayName}" +
                    textureStatus,
                    "Button"))
            {
                selectedTerrainId = definition.TerrainId;
            }

            GUI.backgroundColor = previousBackground;
        }
    }

    private void EnsureSelectedTerrain()
    {
        if (levelAsset == null)
            return;
        if (levelAsset.GetSurfaceDefinition(selectedTerrainId) != null)
            return;

        IReadOnlyList<TerrainSurfaceDefinition> palette =
            levelAsset.SurfacePalette;
        if (palette == null)
            return;

        foreach (TerrainSurfaceDefinition definition in palette)
        {
            if (definition == null)
                continue;

            selectedTerrainId = definition.TerrainId;
            return;
        }
    }

    private void HandleSceneGui(SceneView sceneView)
    {
        TerrainSurfaceDefinition selectedDefinition = levelAsset != null
            ? levelAsset.GetSurfaceDefinition(selectedTerrainId)
            : null;
        if (!paintInScene
            || levelAsset == null
            || mapController == null
            || selectedDefinition == null
            || !selectedDefinition.IsUsable
            || !mapController.HasGeneratedMap)
        {
            return;
        }

        Event current = Event.current;
        int controlId = GUIUtility.GetControlID(
            "HeightMapSurfacePainter".GetHashCode(),
            FocusType.Passive);
        if (!TryGetMapPosition(current.mousePosition, out Vector2 mapPosition))
            return;

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
            float angle = index / (float)BrushOutlineSegments * Mathf.PI * 2f;
            Vector2 mapPoint = centreMapPosition + new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) * brushRadiusMeters;
            points[index] = mapController.MapPositionToWorld(mapPoint);
        }

        Color outlineColor;
        if (erase)
        {
            outlineColor = new Color(1f, 0.28f, 0.2f, 0.95f);
        }
        else
        {
            TerrainSurfaceDefinition definition =
                levelAsset.GetSurfaceDefinition(selectedTerrainId);
            outlineColor = definition != null
                ? definition.Tint
                : new Color(1f, 0.84f, 0.22f, 1f);
            outlineColor.a = 0.95f;
        }

        Handles.color = outlineColor;
        Handles.DrawAAPolyLine(3f, points);
    }

    private void BeginStroke(Vector2 mapPosition, bool erase)
    {
        strokeTerrainId = erase ? 0 : selectedTerrainId;
        if (strokeTerrainId != 0)
        {
            TerrainSurfaceDefinition definition =
                levelAsset.GetSurfaceDefinition(strokeTerrainId);
            if (definition == null || !definition.IsUsable)
                return;
        }

        strokeActive = true;
        strokeChanged = false;
        hasLastStrokePosition = false;
        Undo.IncrementCurrentGroup();
        strokeUndoGroup = Undo.GetCurrentGroup();
        string operationName = erase
            ? "Erase Terrain Surface"
            : $"Paint {levelAsset.GetSurfaceDefinition(strokeTerrainId).DisplayName}";
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
        float spacing = Mathf.Max(0.05f, brushRadiusMeters * 0.3f);
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
        if (!levelAsset.PaintSurfaceType(
                mapPosition,
                brushRadiusMeters,
                strokeTerrainId))
        {
            return;
        }

        strokeChanged = true;
        EditorUtility.SetDirty(levelAsset);
    }

    private void EndStroke()
    {
        if (!strokeActive)
            return;

        strokeActive = false;
        hasLastStrokePosition = false;
        if (strokeChanged)
            BakeAndObserve(false);

        if (strokeUndoGroup >= 0)
            Undo.CollapseUndoOperations(strokeUndoGroup);
        strokeUndoGroup = -1;
        strokeChanged = false;
        Repaint();
    }

    private void FillType(int terrainId)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        string operationName = terrainId == 0
            ? "Clear Terrain Surface"
            : "Fill Terrain Surface";
        Undo.SetCurrentGroupName(operationName);
        Undo.RegisterCompleteObjectUndo(levelAsset, operationName);
        if (levelAsset.FillSurfaceType(terrainId))
        {
            EditorUtility.SetDirty(levelAsset);
            BakeAndObserve(false);
        }

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
    }

    private void BakeAndObserve(bool registerUndo)
    {
        if (!HeightMapSurfaceBaker.Bake(levelAsset, registerUndo))
            return;

        lastObservedAuthoringHash = levelAsset.CalculateSurfaceAuthoringHash();
        SceneView.RepaintAll();
        Repaint();
    }

    private void HandleUndoRedo()
    {
        if (levelAsset == null)
            return;

        levelAsset.EnsureSurfaceAuthoringData();
        int currentHash = levelAsset.CalculateSurfaceAuthoringHash();
        if (currentHash == lastObservedAuthoringHash)
            return;

        HeightMapSurfaceBaker.Bake(levelAsset, false);
        lastObservedAuthoringHash = currentHash;
        SceneView.RepaintAll();
        Repaint();
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
            UnityEngine.Object.FindObjectsOfType<MapTestSceneController>(true);
        if (openMaps.Length > 0)
            SetLevelAsset(openMaps[0].LevelAsset);
    }

    private void SetLevelAsset(HeightMapLevelAsset asset)
    {
        if (levelAsset == asset && mapController != null)
            return;

        levelAsset = asset;
        serializedLevel = levelAsset != null
            ? new SerializedObject(levelAsset)
            : null;
        if (levelAsset != null && levelAsset.EnsureSurfaceAuthoringData())
        {
            EditorUtility.SetDirty(levelAsset);
            AssetDatabase.SaveAssets();
        }

        EnsureSelectedTerrain();
        ResolveSceneMap();
        lastObservedAuthoringHash = levelAsset != null
            ? levelAsset.CalculateSurfaceAuthoringHash()
            : 0;
        QueueBakeUpgradeIfNeeded();
        Repaint();
        SceneView.RepaintAll();
    }

    private void QueueBakeUpgradeIfNeeded()
    {
        if (delayedUpgradeBakeQueued
            || levelAsset == null
            || !levelAsset.HasUsableSurfaceDefinitions
            || !levelAsset.SurfaceBakeNeedsUpgrade)
        {
            return;
        }

        delayedUpgradeBakeQueued = true;
        EditorApplication.delayCall += HandleDelayedUpgradeBake;
    }

    private void HandleDelayedUpgradeBake()
    {
        EditorApplication.delayCall -= HandleDelayedUpgradeBake;
        delayedUpgradeBakeQueued = false;
        if (levelAsset == null
            || !levelAsset.HasUsableSurfaceDefinitions
            || !levelAsset.SurfaceBakeNeedsUpgrade)
        {
            return;
        }

        BakeAndObserve(false);
    }

    private void ResolveSceneMap()
    {
        mapController = null;
        if (levelAsset == null)
            return;

        MapTestSceneController[] openMaps =
            UnityEngine.Object.FindObjectsOfType<MapTestSceneController>(true);
        foreach (MapTestSceneController candidate in openMaps)
        {
            if (candidate != null
                && !EditorUtility.IsPersistent(candidate)
                && candidate.LevelAsset == levelAsset)
            {
                mapController = candidate;
                return;
            }
        }
    }
}

[InitializeOnLoad]
internal static class HeightMapSurfaceBakeUpgrade
{
    static HeightMapSurfaceBakeUpgrade()
    {
        EditorApplication.delayCall += UpgradeOutdatedBakes;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        EditorApplication.delayCall -= UpgradeOutdatedBakes;
        EditorApplication.delayCall += UpgradeOutdatedBakes;
    }

    private static void UpgradeOutdatedBakes()
    {
        EditorApplication.delayCall -= UpgradeOutdatedBakes;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        string[] levelGuids = AssetDatabase.FindAssets(
            "t:HeightMapLevelAsset");
        foreach (string levelGuid in levelGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(levelGuid);
            HeightMapLevelAsset level =
                AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(assetPath);
            if (level == null)
                continue;

            if (level.SurfaceBakeNeedsUpgrade
                && level.HasUsableSurfaceDefinitions)
            {
                HeightMapSurfaceBaker.Bake(level, false);
            }

            if (level.StaticWaterMaskBakeNeedsUpgrade
                && level.StaticWaterTexture != null)
            {
                HeightMapStaticWaterMaskBaker.Bake(level, false);
            }
        }
    }
}

internal static class HeightMapSurfaceBaker
{
    private const string BakeShaderName =
        "Hidden/AnimalGame/Bake Terrain Surface Palette";
    private const int PaletteSize = 256;
    private const int AtlasGridSize = 16;
    private const int AtlasCellSize = 128;
    private const int AtlasPadding = 2;
    private const int AtlasSize = AtlasGridSize * AtlasCellSize;

    private static readonly int[] NeighbourX =
        { -1, 0, 1, -1, 1, -1, 0, 1 };
    private static readonly int[] NeighbourY =
        { -1, -1, -1, 0, 0, 1, 1, 1 };

    public static bool Bake(HeightMapLevelAsset level, bool registerUndo)
    {
        if (level == null)
        {
            Debug.LogError(
                "Terrain surface baking requires a fixed map asset.");
            return false;
        }

        if (level.EnsureSurfaceAuthoringData())
            EditorUtility.SetDirty(level);
        if (!level.HasUsableSurfaceDefinitions)
        {
            Debug.LogError(
                "Terrain surface baking requires at least one Palette entry " +
                "with a Pattern Texture.");
            return false;
        }

        string levelPath = AssetDatabase.GetAssetPath(level);
        if (string.IsNullOrEmpty(levelPath))
        {
            Debug.LogError("Save the HeightMapLevelAsset before baking its surface.");
            return false;
        }

        Shader bakeShader = Shader.Find(BakeShaderName);
        if (bakeShader == null)
        {
            Debug.LogError($"Missing Editor bake shader: {BakeShaderName}");
            return false;
        }

        byte[] typeMap = level.GetOrCreateSurfaceTypeMap();
        int bakeResolution = level.SurfaceBakeResolution;
        float maximumBoundaryDistance = CalculateMaximumBoundaryDistance(level);
        Texture2D patternAtlas = null;
        Texture2D pairTexture = null;
        Texture2D staticContourAlphaTexture = null;
        Texture2D paletteTintTexture = null;
        Texture2D paletteSettingsTexture = null;
        Texture2D readableOutput = null;
        Material bakeMaterial = null;
        RenderTexture renderTarget = null;
        RenderTexture previousTarget = RenderTexture.active;

        try
        {
            patternAtlas = BuildPatternAtlas(level);
            pairTexture = BuildPairTexture(
                level,
                typeMap,
                maximumBoundaryDistance);
            if (level.SurfaceClosedContourAlphaEnabled)
            {
                staticContourAlphaTexture =
                    StaticClosedContourAlphaBuilder.Build(level);
            }
            BuildPaletteTextures(
                level,
                out paletteTintTexture,
                out paletteSettingsTexture);

            bakeMaterial = new Material(bakeShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            bakeMaterial.SetTexture("_PatternAtlas", patternAtlas);
            bakeMaterial.SetTexture("_TerrainPairTex", pairTexture);
            bakeMaterial.SetTexture(
                "_StaticContourAlphaTex",
                staticContourAlphaTexture != null
                    ? staticContourAlphaTexture
                    : Texture2D.whiteTexture);
            bakeMaterial.SetFloat(
                "_StaticContourAlphaEnabled",
                staticContourAlphaTexture != null ? 1f : 0f);
            bakeMaterial.SetTexture("_PaletteTintTex", paletteTintTexture);
            bakeMaterial.SetTexture(
                "_PaletteSettingsTex",
                paletteSettingsTexture);
            bakeMaterial.SetVector(
                "_MapSizeMeters",
                new Vector4(
                    level.MapSizeMeters.x,
                    level.MapSizeMeters.y,
                    0f,
                    0f));
            bakeMaterial.SetFloat(
                "_MaximumBoundaryDistanceMeters",
                maximumBoundaryDistance);
            bakeMaterial.SetFloat(
                "_TransitionWidthMeters",
                level.SurfaceTransitionWidthMeters);
            bakeMaterial.SetFloat(
                "_AlphaCoreWidthMeters",
                level.SurfaceAlphaCoreWidthMeters);
            bakeMaterial.SetFloat(
                "_AlphaBlendShare",
                level.SurfaceAlphaBlendShare);
            bakeMaterial.SetFloat(
                "_BoundaryNoiseScaleMeters",
                level.SurfaceBoundaryNoiseScaleMeters);
            bakeMaterial.SetFloat(
                "_BoundaryNoiseAmplitudeMeters",
                level.SurfaceBoundaryNoiseAmplitudeMeters);
            bakeMaterial.SetFloat(
                "_ScatterCellSizeMeters",
                level.SurfaceScatterCellSizeMeters);
            bakeMaterial.SetFloat(
                "_ScatterStrength",
                level.SurfaceScatterStrength);
            bakeMaterial.SetFloat("_NoiseSeed", level.SurfaceNoiseSeed);

            renderTarget = RenderTexture.GetTemporary(
                bakeResolution,
                bakeResolution,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            renderTarget.name = "Temporary Baked Terrain Surface";
            renderTarget.filterMode = FilterMode.Bilinear;
            renderTarget.wrapMode = TextureWrapMode.Clamp;
            Graphics.Blit(Texture2D.whiteTexture, renderTarget, bakeMaterial);

            RenderTexture.active = renderTarget;
            readableOutput = new Texture2D(
                bakeResolution,
                bakeResolution,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = "Baked Terrain Surface"
            };
            readableOutput.ReadPixels(
                new Rect(0f, 0f, bakeResolution, bakeResolution),
                0,
                0,
                false);
            readableOutput.Apply(false, false);

            string directory = Path.GetDirectoryName(levelPath);
            string fileName = Path.GetFileNameWithoutExtension(levelPath)
                              + "_SurfaceVisual.png";
            string outputPath = Path.Combine(directory ?? "Assets", fileName)
                .Replace('\\', '/');
            File.WriteAllBytes(outputPath, readableOutput.EncodeToPNG());
            AssetDatabase.ImportAsset(
                outputPath,
                ImportAssetOptions.ForceSynchronousImport
                | ImportAssetOptions.ForceUpdate);
            ConfigureBakedTextureImporter(outputPath, bakeResolution);

            Texture2D bakedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                outputPath);
            if (bakedTexture == null)
            {
                Debug.LogError(
                    $"Unity could not load the baked terrain texture at {outputPath}.");
                return false;
            }

            if (registerUndo)
                Undo.RecordObject(level, "Bake Terrain Surface");
            level.SetBakedSurfaceVisual(bakedTexture);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Baked permanent multi-terrain surface: {outputPath}",
                level);
            return true;
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (renderTarget != null)
                RenderTexture.ReleaseTemporary(renderTarget);
            DestroyTemporary(bakeMaterial);
            DestroyTemporary(patternAtlas);
            DestroyTemporary(pairTexture);
            DestroyTemporary(staticContourAlphaTexture);
            DestroyTemporary(paletteTintTexture);
            DestroyTemporary(paletteSettingsTexture);
            DestroyTemporary(readableOutput);
        }
    }

    private static Texture2D BuildPatternAtlas(HeightMapLevelAsset level)
    {
        var atlasPixels = new Color32[AtlasSize * AtlasSize];
        IReadOnlyList<TerrainSurfaceDefinition> palette = level.SurfacePalette;
        if (palette != null)
        {
            foreach (TerrainSurfaceDefinition definition in palette)
            {
                if (definition == null || !definition.IsUsable)
                    continue;

                Texture2D readable = CreateReadableCopy(
                    definition.PatternTexture);
                try
                {
                    CopyRepeatedPatternIntoAtlas(
                        readable,
                        atlasPixels,
                        definition.TerrainId,
                        level.SurfacePatternAlphaNormalizationStrength);
                }
                finally
                {
                    DestroyTemporary(readable);
                }
            }
        }

        var atlas = new Texture2D(
            AtlasSize,
            AtlasSize,
            TextureFormat.RGBA32,
            false,
            false)
        {
            name = "Temporary Terrain Pattern Atlas",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        atlas.SetPixels32(atlasPixels);
        atlas.Apply(false, true);
        return atlas;
    }

    private static void CopyRepeatedPatternIntoAtlas(
        Texture2D source,
        Color32[] atlasPixels,
        int terrainId,
        float alphaNormalizationStrength)
    {
        int cellX = terrainId % AtlasGridSize;
        int cellY = terrainId / AtlasGridSize;
        int cellOriginX = cellX * AtlasCellSize;
        int cellOriginY = cellY * AtlasCellSize;
        int interiorSize = AtlasCellSize - AtlasPadding * 2;
        float alphaReference = CalculatePatternAlphaReference(source);
        float normalizationStrength = Mathf.Clamp01(
            alphaNormalizationStrength);

        for (int y = 0; y < AtlasCellSize; y++)
        {
            float sourceV = Mathf.Repeat(
                (y - AtlasPadding + 0.5f) / interiorSize,
                1f);
            for (int x = 0; x < AtlasCellSize; x++)
            {
                float sourceU = Mathf.Repeat(
                    (x - AtlasPadding + 0.5f) / interiorSize,
                    1f);
                Color sampled = source.GetPixelBilinear(sourceU, sourceV);
                if (sampled.a > 0.0001f && normalizationStrength > 0f)
                {
                    float normalizedAlpha = Mathf.Clamp01(
                        sampled.a / alphaReference);
                    sampled.a = Mathf.Lerp(
                        sampled.a,
                        normalizedAlpha,
                        normalizationStrength);
                }
                int targetX = cellOriginX + x;
                int targetY = cellOriginY + y;
                atlasPixels[targetY * AtlasSize + targetX] = sampled;
            }
        }
    }

    private static float CalculatePatternAlphaReference(Texture2D source)
    {
        Color32[] pixels = source.GetPixels32();
        var histogram = new int[256];
        int nonTransparentCount = 0;
        foreach (Color32 pixel in pixels)
        {
            if (pixel.a == 0)
                continue;

            histogram[pixel.a]++;
            nonTransparentCount++;
        }

        if (nonTransparentCount == 0)
            return 1f;

        int medianRank = (nonTransparentCount - 1) / 2;
        int accumulated = 0;
        for (int alpha = 1; alpha < histogram.Length; alpha++)
        {
            accumulated += histogram[alpha];
            if (accumulated > medianRank)
                return alpha / 255f;
        }

        return 1f;
    }

    private static Texture2D CreateReadableCopy(Texture2D source)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D readable = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = $"Readable Copy of {source.name}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            readable.ReadPixels(
                new Rect(0f, 0f, source.width, source.height),
                0,
                0,
                false);
            readable.Apply(false, false);
            return readable;
        }
        catch
        {
            DestroyTemporary(readable);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private static void BuildPaletteTextures(
        HeightMapLevelAsset level,
        out Texture2D tintTexture,
        out Texture2D settingsTexture)
    {
        var tints = new Color[PaletteSize];
        var settings = new Color[PaletteSize];
        settings[0] = new Color(1f, 0f, 1f, 1f);

        IReadOnlyList<TerrainSurfaceDefinition> palette = level.SurfacePalette;
        if (palette != null)
        {
            foreach (TerrainSurfaceDefinition definition in palette)
            {
                if (definition == null)
                    continue;

                int id = Mathf.Clamp(definition.TerrainId, 1, 255);
                Color tint = definition.Tint;
                tint.a *= definition.Opacity;
                if (!definition.IsUsable)
                    tint.a = 0f;
                tints[id] = tint;
                settings[id] = new Color(
                    definition.TileSizeMeters,
                    (float)definition.TransitionMode,
                    definition.TransitionWidthMultiplier,
                    definition.NoiseStrengthMultiplier);
            }
        }

        tintTexture = new Texture2D(
            PaletteSize,
            1,
            TextureFormat.RGBAFloat,
            false,
            true)
        {
            name = "Temporary Terrain Palette Tint",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        tintTexture.SetPixels(tints);
        tintTexture.Apply(false, true);

        settingsTexture = new Texture2D(
            PaletteSize,
            1,
            TextureFormat.RGBAFloat,
            false,
            true)
        {
            name = "Temporary Terrain Palette Settings",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        settingsTexture.SetPixels(settings);
        settingsTexture.Apply(false, true);
    }

    private static Texture2D BuildPairTexture(
        HeightMapLevelAsset level,
        byte[] typeMap,
        float maximumDistanceMeters)
    {
        int resolution = level.SurfaceMaskResolution;
        int pixelCount = resolution * resolution;
        var distances = new float[pixelCount];
        var secondaryTypes = new byte[pixelCount];
        for (int index = 0; index < pixelCount; index++)
            distances[index] = float.PositiveInfinity;

        Vector2 mapSize = level.MapSizeMeters;
        float pixelWidth = mapSize.x / resolution;
        float pixelHeight = mapSize.y / resolution;
        var heap = new DistanceHeap(Mathf.Max(1024, pixelCount / 8));

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = y * resolution + x;
                byte primaryType = typeMap[index];
                for (int neighbour = 0;
                     neighbour < NeighbourX.Length;
                     neighbour++)
                {
                    int neighbourX = x + NeighbourX[neighbour];
                    int neighbourY = y + NeighbourY[neighbour];
                    if (neighbourX < 0
                        || neighbourX >= resolution
                        || neighbourY < 0
                        || neighbourY >= resolution)
                    {
                        continue;
                    }

                    int neighbourIndex = neighbourY * resolution + neighbourX;
                    byte otherType = typeMap[neighbourIndex];
                    if (otherType == primaryType)
                        continue;

                    float deltaX = NeighbourX[neighbour] * pixelWidth;
                    float deltaY = NeighbourY[neighbour] * pixelHeight;
                    float candidateDistance = 0.5f * Mathf.Sqrt(
                        deltaX * deltaX + deltaY * deltaY);
                    if (!IsBetterPair(
                            candidateDistance,
                            otherType,
                            distances[index],
                            secondaryTypes[index]))
                    {
                        continue;
                    }

                    distances[index] = candidateDistance;
                    secondaryTypes[index] = otherType;
                    heap.Push(index, candidateDistance, otherType);
                }
            }
        }

        while (heap.Count > 0)
        {
            DistanceNode node = heap.Pop();
            if (node.Distance > maximumDistanceMeters)
                continue;
            if (node.Distance > distances[node.Index] + 0.0001f
                || secondaryTypes[node.Index] != node.SecondaryType)
            {
                continue;
            }

            int x = node.Index % resolution;
            int y = node.Index / resolution;
            byte primaryType = typeMap[node.Index];
            for (int neighbour = 0;
                 neighbour < NeighbourX.Length;
                 neighbour++)
            {
                int neighbourX = x + NeighbourX[neighbour];
                int neighbourY = y + NeighbourY[neighbour];
                if (neighbourX < 0
                    || neighbourX >= resolution
                    || neighbourY < 0
                    || neighbourY >= resolution)
                {
                    continue;
                }

                int neighbourIndex = neighbourY * resolution + neighbourX;
                if (typeMap[neighbourIndex] != primaryType)
                    continue;

                float deltaX = NeighbourX[neighbour] * pixelWidth;
                float deltaY = NeighbourY[neighbour] * pixelHeight;
                float candidateDistance = node.Distance + Mathf.Sqrt(
                    deltaX * deltaX + deltaY * deltaY);
                if (candidateDistance > maximumDistanceMeters
                    || !IsBetterPair(
                        candidateDistance,
                        node.SecondaryType,
                        distances[neighbourIndex],
                        secondaryTypes[neighbourIndex]))
                {
                    continue;
                }

                distances[neighbourIndex] = candidateDistance;
                secondaryTypes[neighbourIndex] = node.SecondaryType;
                heap.Push(
                    neighbourIndex,
                    candidateDistance,
                    node.SecondaryType);
            }
        }

        var pairPixels = new Color32[pixelCount];
        float inverseMaximumDistance = 1f / Mathf.Max(
            0.0001f,
            maximumDistanceMeters);
        for (int index = 0; index < pixelCount; index++)
        {
            bool hasPair = !float.IsInfinity(distances[index]);
            byte encodedDistance = hasPair
                ? (byte)Mathf.RoundToInt(Mathf.Clamp01(
                    distances[index] * inverseMaximumDistance) * 255f)
                : (byte)255;
            pairPixels[index] = new Color32(
                typeMap[index],
                secondaryTypes[index],
                encodedDistance,
                hasPair ? (byte)255 : (byte)0);
        }

        var pairTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = "Temporary Terrain Pair Distance Map",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        pairTexture.SetPixels32(pairPixels);
        pairTexture.Apply(false, true);
        return pairTexture;
    }

    private static bool IsBetterPair(
        float candidateDistance,
        byte candidateType,
        float currentDistance,
        byte currentType)
    {
        if (candidateDistance < currentDistance - 0.0001f)
            return true;

        return Mathf.Abs(candidateDistance - currentDistance) <= 0.0001f
               && candidateType < currentType;
    }

    private static float CalculateMaximumBoundaryDistance(
        HeightMapLevelAsset level)
    {
        float maximumWidthMultiplier = 1f;
        float maximumNoiseMultiplier = 1f;
        IReadOnlyList<TerrainSurfaceDefinition> palette = level.SurfacePalette;
        if (palette != null)
        {
            foreach (TerrainSurfaceDefinition definition in palette)
            {
                if (definition == null)
                    continue;

                maximumWidthMultiplier = Mathf.Max(
                    maximumWidthMultiplier,
                    definition.TransitionWidthMultiplier);
                maximumNoiseMultiplier = Mathf.Max(
                    maximumNoiseMultiplier,
                    definition.NoiseStrengthMultiplier);
            }
        }

        float pixelWidth = level.MapSizeMeters.x
                           / level.SurfaceMaskResolution;
        float pixelHeight = level.MapSizeMeters.y
                            / level.SurfaceMaskResolution;
        float pixelDiagonal = Mathf.Sqrt(
            pixelWidth * pixelWidth + pixelHeight * pixelHeight);
        return Mathf.Max(
            pixelDiagonal,
            level.SurfaceTransitionWidthMeters
            * 0.5f
            * maximumWidthMultiplier
            + level.SurfaceBoundaryNoiseAmplitudeMeters
            * maximumNoiseMultiplier
            * (1f + level.SurfaceScatterStrength * 0.35f)
            + pixelDiagonal);
    }

    private static void ConfigureBakedTextureImporter(
        string assetPath,
        int resolution)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath)
            as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.isReadable = false;
        // The map surface is already baked at its final display resolution.
        // Mip generation softens thin terrain marks and can create broken,
        // dirty-looking lines in low-alpha patterns.
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = Mathf.Clamp(
            Mathf.NextPowerOfTwo(resolution),
            256,
            8192);
        importer.SaveAndReimport();
    }

    private static void DestroyTemporary(UnityEngine.Object temporary)
    {
        if (temporary != null)
            UnityEngine.Object.DestroyImmediate(temporary);
    }

    private readonly struct DistanceNode
    {
        public readonly int Index;
        public readonly float Distance;
        public readonly byte SecondaryType;

        public DistanceNode(int index, float distance, byte secondaryType)
        {
            Index = index;
            Distance = distance;
            SecondaryType = secondaryType;
        }
    }

    private sealed class DistanceHeap
    {
        private DistanceNode[] nodes;

        public DistanceHeap(int initialCapacity)
        {
            nodes = new DistanceNode[Mathf.Max(4, initialCapacity)];
        }

        public int Count { get; private set; }

        public void Push(int index, float distance, byte secondaryType)
        {
            if (Count == nodes.Length)
                Array.Resize(ref nodes, nodes.Length * 2);

            int insertionIndex = Count++;
            var node = new DistanceNode(index, distance, secondaryType);
            while (insertionIndex > 0)
            {
                int parentIndex = (insertionIndex - 1) / 2;
                if (!ComesBefore(node, nodes[parentIndex]))
                    break;

                nodes[insertionIndex] = nodes[parentIndex];
                insertionIndex = parentIndex;
            }

            nodes[insertionIndex] = node;
        }

        public DistanceNode Pop()
        {
            DistanceNode result = nodes[0];
            DistanceNode tail = nodes[--Count];
            if (Count == 0)
                return result;

            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= Count)
                    break;

                int right = left + 1;
                int child = right < Count
                            && ComesBefore(nodes[right], nodes[left])
                    ? right
                    : left;
                if (!ComesBefore(nodes[child], tail))
                    break;

                nodes[index] = nodes[child];
                index = child;
            }

            nodes[index] = tail;
            return result;
        }

        private static bool ComesBefore(DistanceNode left, DistanceNode right)
        {
            if (left.Distance < right.Distance)
                return true;
            if (left.Distance > right.Distance)
                return false;
            if (left.SecondaryType < right.SecondaryType)
                return true;
            if (left.SecondaryType > right.SecondaryType)
                return false;
            return left.Index < right.Index;
        }
    }
}

/// <summary>
/// Builds a temporary, Editor-only alpha multiplier field from the fixed map's
/// smoothed height data. The result is consumed by the surface bake shader and
/// written permanently into the baked PNG; none of this work exists at runtime.
/// </summary>
internal static class StaticClosedContourAlphaBuilder
{
    private static readonly int[] CardinalX = { -1, 1, 0, 0 };
    private static readonly int[] CardinalY = { 0, 0, -1, 1 };
    private static readonly int[] NeighbourX =
        { -1, 0, 1, -1, 1, -1, 0, 1 };
    private static readonly int[] NeighbourY =
        { -1, -1, -1, 0, 0, 1, 1, 1 };

    public static Texture2D Build(HeightMapLevelAsset level)
    {
        if (level == null || !level.IsValid)
        {
            throw new InvalidOperationException(
                "Static closed-contour alpha requires a valid fixed height map.");
        }

        int resolution = Mathf.Clamp(
            level.SurfaceClosedContourAlphaResolution,
            128,
            1024);
        int pixelCount = resolution * resolution;
        var heights = new float[pixelCount];

        using (BakedHeightField heightField = BakedHeightField.Bake(
                   level.HeightMap,
                   resolution,
                   level.MapSizeMeters,
                   level.MinimumHeightMeters,
                   level.MaximumHeightMeters,
                   level.NormalizeSourceRange,
                   level.SurfaceSmoothingSigmaMeters))
        {
            for (int y = 0; y < resolution; y++)
            {
                int row = y * resolution;
                for (int x = 0; x < resolution; x++)
                {
                    heights[row + x] =
                        heightField.GetSurfaceHeightSample(x, y);
                }
            }
        }

        var smallestContainingArea = new float[pixelCount];
        var distanceWithinSmallestRegionMeters = new float[pixelCount];
        var maximumDistanceWithinSmallestRegionMeters =
            new float[pixelCount];
        var labels = new int[pixelCount];
        var floodQueue = new int[pixelCount];
        var distances = new float[pixelCount];
        var distanceHeap = new ContourDistanceHeap(
            Mathf.Max(1024, pixelCount / 8));
        for (int index = 0; index < pixelCount; index++)
            smallestContainingArea[index] = float.PositiveInfinity;

        float interval = Mathf.Max(0.0001f, level.ContourIntervalMeters);
        float minimumHeight = level.MinimumHeightMeters;
        float maximumHeight = level.MaximumHeightMeters;
        int contourCount = Mathf.FloorToInt(
            (maximumHeight - minimumHeight) / interval);
        float maximumThreshold = maximumHeight - 0.0001f;
        for (int contourIndex = 1;
             contourIndex <= contourCount;
             contourIndex++)
        {
            float threshold = minimumHeight + contourIndex * interval;
            if (threshold >= maximumThreshold)
                break;

            // A contour region can surround either a hill (highland component)
            // or a depression (lowland component), so both sides are indexed.
            // Lines reaching the rectangular map border are closed by that border.
            AccumulateClosedComponents(
                heights,
                resolution,
                level.MapSizeMeters,
                threshold,
                true,
                level.SurfaceClosedContourMinimumAreaSquareMeters,
                labels,
                floodQueue,
                distances,
                distanceHeap,
                smallestContainingArea,
                distanceWithinSmallestRegionMeters,
                maximumDistanceWithinSmallestRegionMeters);
            AccumulateClosedComponents(
                heights,
                resolution,
                level.MapSizeMeters,
                threshold,
                false,
                level.SurfaceClosedContourMinimumAreaSquareMeters,
                labels,
                floodQueue,
                distances,
                distanceHeap,
                smallestContainingArea,
                distanceWithinSmallestRegionMeters,
                maximumDistanceWithinSmallestRegionMeters);
        }

        float edgeMultiplier =
            level.SurfaceClosedContourEdgeAlphaMultiplier;
        float centreMultiplier =
            level.SurfaceClosedContourCenterAlphaMultiplier;
        float outsideMultiplier =
            level.SurfaceOutsideClosedContourAlphaMultiplier;
        float edgeHoldDistance = Mathf.Max(
            0f,
            level.SurfaceClosedContourEdgeHoldDistanceMeters);
        float configuredFadeDistance = Mathf.Max(
            0.1f,
            level.SurfaceClosedContourFadeDistanceMeters);
        float fadeStrength = Mathf.Max(
            0.1f,
            level.SurfaceClosedContourFadeStrength);
        var multipliers = new float[pixelCount];
        for (int index = 0; index < pixelCount; index++)
        {
            if (float.IsPositiveInfinity(smallestContainingArea[index]))
            {
                multipliers[index] = outsideMultiplier;
                continue;
            }

            float regionMaximumDistance =
                maximumDistanceWithinSmallestRegionMeters[index];
            if (regionMaximumDistance <= 0.0001f)
            {
                multipliers[index] = edgeMultiplier;
                continue;
            }

            // Large regions reach the centre multiplier at a fixed physical
            // distance, producing a truly transparent interior plateau. Small
            // regions use their own depth so their deepest point still fades
            // fully instead of disappearing as one uniformly bright patch.
            float effectiveFadeDistance = Mathf.Min(
                configuredFadeDistance,
                regionMaximumDistance);
            float effectiveHoldDistance = Mathf.Min(
                edgeHoldDistance,
                effectiveFadeDistance * 0.25f);
            float fadeDistanceRange = Mathf.Max(
                0.0001f,
                effectiveFadeDistance - effectiveHoldDistance);
            float normalizedDistance = Mathf.Clamp01(
                (distanceWithinSmallestRegionMeters[index]
                 - effectiveHoldDistance)
                / fadeDistanceRange);
            float smoothDistance = Mathf.SmoothStep(
                0f,
                1f,
                normalizedDistance);
            float fadeAmount = 1f - Mathf.Pow(
                1f - smoothDistance,
                fadeStrength);
            multipliers[index] = Mathf.Lerp(
                edgeMultiplier,
                centreMultiplier,
                fadeAmount);
        }

        var texture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RFloat,
            false,
            true)
        {
            name = "Temporary Static Closed-Contour Alpha",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixelData(multipliers, 0);
        texture.Apply(false, true);
        return texture;
    }

    private static void AccumulateClosedComponents(
        float[] heights,
        int resolution,
        Vector2 mapSizeMeters,
        float threshold,
        bool highland,
        float minimumAreaSquareMeters,
        int[] labels,
        int[] floodQueue,
        float[] distances,
        ContourDistanceHeap distanceHeap,
        float[] smallestContainingArea,
        float[] distanceWithinSmallestRegionMeters,
        float[] maximumDistanceWithinSmallestRegionMeters)
    {
        int pixelCount = heights.Length;
        for (int index = 0; index < pixelCount; index++)
            labels[index] = -1;

        var componentPixelCounts = new List<int>();
        var componentHasContourBoundary = new List<bool>();
        for (int seed = 0; seed < pixelCount; seed++)
        {
            if (labels[seed] >= 0
                || !IsInsideThreshold(heights[seed], threshold, highland))
            {
                continue;
            }

            int component = componentPixelCounts.Count;
            int head = 0;
            int tail = 0;
            int count = 0;
            bool hasContourBoundary = false;
            labels[seed] = component;
            floodQueue[tail++] = seed;

            while (head < tail)
            {
                int index = floodQueue[head++];
                count++;
                int x = index % resolution;
                int y = index / resolution;
                for (int neighbour = 0;
                     neighbour < CardinalX.Length;
                     neighbour++)
                {
                    int neighbourX = x + CardinalX[neighbour];
                    int neighbourY = y + CardinalY[neighbour];
                    if (neighbourX < 0
                        || neighbourX >= resolution
                        || neighbourY < 0
                        || neighbourY >= resolution)
                    {
                        continue;
                    }

                    int neighbourIndex =
                        neighbourY * resolution + neighbourX;
                    if (!IsInsideThreshold(
                            heights[neighbourIndex],
                            threshold,
                            highland))
                    {
                        hasContourBoundary = true;
                        continue;
                    }

                    if (labels[neighbourIndex] >= 0)
                    {
                        continue;
                    }

                    labels[neighbourIndex] = component;
                    floodQueue[tail++] = neighbourIndex;
                }
            }

            componentPixelCounts.Add(count);
            componentHasContourBoundary.Add(hasContourBoundary);
        }

        int componentCount = componentPixelCounts.Count;
        if (componentCount == 0)
            return;

        float pixelWidth = mapSizeMeters.x / resolution;
        float pixelHeight = mapSizeMeters.y / resolution;
        float pixelArea = pixelWidth * pixelHeight;
        var validComponents = new bool[componentCount];
        bool hasValidComponent = false;
        for (int component = 0; component < componentCount; component++)
        {
            float area = componentPixelCounts[component] * pixelArea;
            // The rectangular map boundary closes edge-reaching contours. A
            // real threshold transition is still required so a uniform map is
            // not mistaken for one giant contour region.
            bool valid = componentHasContourBoundary[component]
                         && area >= minimumAreaSquareMeters;
            validComponents[component] = valid;
            hasValidComponent |= valid;
        }

        if (!hasValidComponent)
            return;

        distanceHeap.Clear();
        for (int index = 0; index < pixelCount; index++)
            distances[index] = float.PositiveInfinity;

        // Every valid component boundary is a simultaneous source. Distance is
        // propagated only through pixels with the same component label.
        for (int index = 0; index < pixelCount; index++)
        {
            int component = labels[index];
            if (component < 0 || !validComponents[component])
                continue;

            int x = index % resolution;
            int y = index / resolution;
            bool isBoundary = false;
            for (int neighbour = 0;
                 neighbour < NeighbourX.Length;
                 neighbour++)
            {
                int neighbourX = x + NeighbourX[neighbour];
                int neighbourY = y + NeighbourY[neighbour];
                if (neighbourX < 0
                    || neighbourX >= resolution
                    || neighbourY < 0
                    || neighbourY >= resolution
                    || labels[neighbourY * resolution + neighbourX]
                    != component)
                {
                    isBoundary = true;
                    break;
                }
            }

            if (!isBoundary)
                continue;

            distances[index] = 0f;
            distanceHeap.Push(index, 0f);
        }

        while (distanceHeap.Count > 0)
        {
            ContourDistanceNode node = distanceHeap.Pop();
            if (node.Distance > distances[node.Index] + 0.0001f)
                continue;

            int component = labels[node.Index];
            int x = node.Index % resolution;
            int y = node.Index / resolution;
            for (int neighbour = 0;
                 neighbour < NeighbourX.Length;
                 neighbour++)
            {
                int neighbourX = x + NeighbourX[neighbour];
                int neighbourY = y + NeighbourY[neighbour];
                if (neighbourX < 0
                    || neighbourX >= resolution
                    || neighbourY < 0
                    || neighbourY >= resolution)
                {
                    continue;
                }

                int neighbourIndex =
                    neighbourY * resolution + neighbourX;
                if (labels[neighbourIndex] != component)
                    continue;

                float deltaX = NeighbourX[neighbour] * pixelWidth;
                float deltaY = NeighbourY[neighbour] * pixelHeight;
                float candidate = node.Distance + Mathf.Sqrt(
                    deltaX * deltaX + deltaY * deltaY);
                if (candidate >= distances[neighbourIndex] - 0.0001f)
                    continue;

                distances[neighbourIndex] = candidate;
                distanceHeap.Push(neighbourIndex, candidate);
            }
        }

        var componentMaximumDistances = new float[componentCount];
        for (int index = 0; index < pixelCount; index++)
        {
            int component = labels[index];
            if (component < 0 || !validComponents[component])
                continue;

            componentMaximumDistances[component] = Mathf.Max(
                componentMaximumDistances[component],
                distances[index]);
        }

        for (int index = 0; index < pixelCount; index++)
        {
            int component = labels[index];
            if (component < 0 || !validComponents[component])
                continue;

            float area = componentPixelCounts[component] * pixelArea;
            if (area >= smallestContainingArea[index] - 0.0001f)
                continue;

            float maximumDistance =
                componentMaximumDistances[component];
            smallestContainingArea[index] = area;
            distanceWithinSmallestRegionMeters[index] = distances[index];
            maximumDistanceWithinSmallestRegionMeters[index] =
                maximumDistance;
        }
    }

    private static bool IsInsideThreshold(
        float height,
        float threshold,
        bool highland)
    {
        return highland ? height >= threshold : height <= threshold;
    }

    private readonly struct ContourDistanceNode
    {
        public readonly int Index;
        public readonly float Distance;

        public ContourDistanceNode(int index, float distance)
        {
            Index = index;
            Distance = distance;
        }
    }

    private sealed class ContourDistanceHeap
    {
        private ContourDistanceNode[] nodes;

        public ContourDistanceHeap(int initialCapacity)
        {
            nodes = new ContourDistanceNode[
                Mathf.Max(4, initialCapacity)];
        }

        public int Count { get; private set; }

        public void Clear()
        {
            Count = 0;
        }

        public void Push(int index, float distance)
        {
            if (Count == nodes.Length)
                Array.Resize(ref nodes, nodes.Length * 2);

            int insertionIndex = Count++;
            var node = new ContourDistanceNode(index, distance);
            while (insertionIndex > 0)
            {
                int parentIndex = (insertionIndex - 1) / 2;
                if (!ComesBefore(node, nodes[parentIndex]))
                    break;

                nodes[insertionIndex] = nodes[parentIndex];
                insertionIndex = parentIndex;
            }

            nodes[insertionIndex] = node;
        }

        public ContourDistanceNode Pop()
        {
            ContourDistanceNode result = nodes[0];
            ContourDistanceNode tail = nodes[--Count];
            if (Count == 0)
                return result;

            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= Count)
                    break;

                int right = left + 1;
                int child = right < Count
                            && ComesBefore(nodes[right], nodes[left])
                    ? right
                    : left;
                if (!ComesBefore(nodes[child], tail))
                    break;

                nodes[index] = nodes[child];
                index = child;
            }

            nodes[index] = tail;
            return result;
        }

        private static bool ComesBefore(
            ContourDistanceNode left,
            ContourDistanceNode right)
        {
            if (left.Distance < right.Distance)
                return true;
            if (left.Distance > right.Distance)
                return false;
            return left.Index < right.Index;
        }
    }
}
