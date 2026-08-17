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
    }
}

public sealed class HeightMapSurfacePainterWindow : EditorWindow
{
    private const float MinimumBrushRadiusMeters = 0.25f;
    private const float DefaultBrushRadiusMeters = 10f;
    private const int BrushOutlineSegments = 64;

    private HeightMapLevelAsset levelAsset;
    private MapTestSceneController mapController;
    private float brushRadiusMeters = DefaultBrushRadiusMeters;
    private bool paintInScene = true;
    private bool strokeActive;
    private bool strokeErase;
    private bool strokeChanged;
    private bool hasLastStrokePosition;
    private Vector2 lastStrokeMapPosition;
    private int strokeUndoGroup = -1;
    private int lastObservedAuthoringHash;

    [MenuItem("Animal Game/Level/Terrain Surface Painter")]
    public static void Open()
    {
        var window = GetWindow<HeightMapSurfacePainterWindow>();
        window.titleContent = new GUIContent("Surface Painter");
        window.minSize = new Vector2(370f, 430f);
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
        minSize = new Vector2(370f, 430f);
        SceneView.duringSceneGui += HandleSceneGui;
        Undo.undoRedoPerformed += HandleUndoRedo;
        if (levelAsset == null)
            TryAdoptCurrentSelection();
    }

    private void OnDisable()
    {
        if (strokeActive)
            EndStroke();

        SceneView.duringSceneGui -= HandleSceneGui;
        Undo.undoRedoPerformed -= HandleUndoRedo;
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
        EditorGUILayout.LabelField(
            "Permanent Single-Surface Terrain",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Paints a binary area mask into the fixed map asset. At the end of " +
            "each stroke, the Editor permanently bakes the repeated source pattern " +
            "into one static PNG. Runtime never rebuilds or tiles this artwork.",
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
                "Select MainHeightMapLevel or a scene MapTestController, then open " +
                "this window again.",
                MessageType.Warning);
            return;
        }

        DrawSurfaceSettings();

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

        using (new EditorGUI.DisabledScope(
                   levelAsset.SurfacePatternTexture == null))
        {
            if (GUILayout.Button("Bake Current Painted Area"))
                BakeAndObserve(true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fill Entire Map"))
                    FillMask(true);
                if (GUILayout.Button("Clear Painted Area"))
                    FillMask(false);
            }
        }

        if (levelAsset.SurfacePatternTexture == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a Source Pattern before painting or baking.",
                MessageType.Error);
        }
        else if (mapController == null)
        {
            EditorGUILayout.HelpBox(
                "The selected level is not present in the open scene. Baking still " +
                "works, but Scene painting needs its MapTestController.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Scene controls\n" +
            "• Left-drag: paint the test terrain\n" +
            "• Shift + left-drag: erase\n" +
            "• Alt + mouse: normal Scene camera navigation\n" +
            "• Ctrl+Z / Ctrl+Y: undo or redo and automatically rebake",
            MessageType.None);

        if (GUI.changed)
            SceneView.RepaintAll();
    }

    private void DrawSurfaceSettings()
    {
        var serializedLevel = new SerializedObject(levelAsset);
        SerializedProperty pattern = serializedLevel.FindProperty(
            "surfacePatternTexture");
        SerializedProperty tint = serializedLevel.FindProperty("surfaceTint");
        SerializedProperty opacity = serializedLevel.FindProperty("surfaceOpacity");
        SerializedProperty tileSize = serializedLevel.FindProperty(
            "surfaceTileSizeMeters");
        SerializedProperty bakeResolution = serializedLevel.FindProperty(
            "surfaceBakeResolution");
        SerializedProperty maskResolution = serializedLevel.FindProperty(
            "surfaceMaskResolution");
        SerializedProperty revealEdge = serializedLevel.FindProperty(
            "surfaceRevealEdgePixels");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Baked Surface Settings", EditorStyles.boldLabel);
        serializedLevel.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(pattern, new GUIContent("Source Pattern"));
        EditorGUILayout.PropertyField(tint, new GUIContent("Tint"));
        EditorGUILayout.PropertyField(opacity, new GUIContent("Opacity"));
        EditorGUILayout.PropertyField(tileSize, new GUIContent("Tile Size (metres)"));
        EditorGUILayout.PropertyField(
            bakeResolution,
            new GUIContent("Baked Texture Resolution"));
        EditorGUILayout.PropertyField(
            maskResolution,
            new GUIContent("Paint Mask Resolution"));
        EditorGUILayout.PropertyField(
            revealEdge,
            new GUIContent("UI Reveal Edge (pixels)"));
        if (EditorGUI.EndChangeCheck())
        {
            serializedLevel.ApplyModifiedProperties();
            EditorUtility.SetDirty(levelAsset);
        }
        else
        {
            serializedLevel.ApplyModifiedProperties();
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Baked Runtime Texture",
                levelAsset.BakedSurfaceVisual,
                typeof(Texture2D),
                false);
        }
    }

    private void HandleSceneGui(SceneView sceneView)
    {
        if (!paintInScene
            || levelAsset == null
            || mapController == null
            || levelAsset.SurfacePatternTexture == null
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

        Handles.color = erase
            ? new Color(1f, 0.28f, 0.2f, 0.95f)
            : new Color(1f, 0.84f, 0.22f, 0.95f);
        Handles.DrawAAPolyLine(3f, points);
    }

    private void BeginStroke(Vector2 mapPosition, bool erase)
    {
        strokeActive = true;
        strokeErase = erase;
        strokeChanged = false;
        hasLastStrokePosition = false;
        Undo.IncrementCurrentGroup();
        strokeUndoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(
            erase ? "Erase Terrain Surface" : "Paint Terrain Surface");
        Undo.RegisterCompleteObjectUndo(
            levelAsset,
            erase ? "Erase Terrain Surface" : "Paint Terrain Surface");
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
        if (!levelAsset.PaintSurfaceMask(
                mapPosition,
                brushRadiusMeters,
                strokeErase))
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

    private void FillMask(bool painted)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        string operationName = painted
            ? "Fill Terrain Surface"
            : "Clear Terrain Surface";
        Undo.SetCurrentGroupName(operationName);
        Undo.RegisterCompleteObjectUndo(levelAsset, operationName);
        if (levelAsset.FillSurfaceMask(painted))
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
            Object.FindObjectsOfType<MapTestSceneController>(true);
        if (openMaps.Length > 0)
            SetLevelAsset(openMaps[0].LevelAsset);
    }

    private void SetLevelAsset(HeightMapLevelAsset asset)
    {
        if (levelAsset == asset && mapController != null)
            return;

        levelAsset = asset;
        ResolveSceneMap();
        lastObservedAuthoringHash = levelAsset != null
            ? levelAsset.CalculateSurfaceAuthoringHash()
            : 0;
        Repaint();
        SceneView.RepaintAll();
    }

    private void ResolveSceneMap()
    {
        mapController = null;
        if (levelAsset == null)
            return;

        MapTestSceneController[] openMaps =
            Object.FindObjectsOfType<MapTestSceneController>(true);
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

internal static class HeightMapSurfaceBaker
{
    private const string BakeShaderName =
        "Hidden/AnimalGame/Bake Single Terrain Surface";

    public static bool Bake(HeightMapLevelAsset level, bool registerUndo)
    {
        if (level == null || level.SurfacePatternTexture == null)
        {
            Debug.LogError(
                "Terrain surface baking requires a fixed map asset and Source Pattern.");
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

        byte[] maskData = level.GetOrCreateSurfaceMask();
        int maskResolution = level.SurfaceMaskResolution;
        int bakeResolution = level.SurfaceBakeResolution;
        Texture2D maskTexture = null;
        Texture2D readableOutput = null;
        Material bakeMaterial = null;
        RenderTexture renderTarget = null;
        RenderTexture previousTarget = RenderTexture.active;

        try
        {
            maskTexture = new Texture2D(
                maskResolution,
                maskResolution,
                TextureFormat.R8,
                false,
                true)
            {
                name = "Temporary Terrain Surface Mask",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            maskTexture.LoadRawTextureData(maskData);
            maskTexture.Apply(false, true);

            bakeMaterial = new Material(bakeShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            bakeMaterial.SetTexture("_PatternTex", level.SurfacePatternTexture);
            bakeMaterial.SetTexture("_MaskTex", maskTexture);
            bakeMaterial.SetColor("_Tint", level.SurfaceTint);
            bakeMaterial.SetFloat("_Opacity", level.SurfaceOpacity);
            bakeMaterial.SetVector(
                "_MapSizeMeters",
                new Vector4(
                    level.MapSizeMeters.x,
                    level.MapSizeMeters.y,
                    0f,
                    0f));
            bakeMaterial.SetFloat(
                "_TileSizeMeters",
                level.SurfaceTileSizeMeters);

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
                $"Baked permanent terrain surface: {outputPath}",
                level);
            return true;
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (renderTarget != null)
                RenderTexture.ReleaseTemporary(renderTarget);
            if (bakeMaterial != null)
                Object.DestroyImmediate(bakeMaterial);
            if (maskTexture != null)
                Object.DestroyImmediate(maskTexture);
            if (readableOutput != null)
                Object.DestroyImmediate(readableOutput);
        }
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
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = Mathf.Clamp(
            Mathf.NextPowerOfTwo(resolution),
            256,
            8192);
        importer.SaveAndReimport();
    }
}
