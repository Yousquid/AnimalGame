using AnimalGame.Animals;
using AnimalGame.Discovery;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [InitializeOnLoad]
    public static class AnimalDiscoveryPrefabInstaller
    {
        private const string MuskratPrefabPath =
            "Assets/Prefabs/Animals/Muskrat/Muskrat_Placeholder.prefab";
        private const string BodyFillSpritePath =
            "Assets/Arts/robot_body_fill.png";
        private const string StaticShaderPath =
            "Assets/Shaders/UnknownAnimalStatic.shader";
        private const string MaterialFolder = "Assets/Materials/Animals";
        private const string StaticMaterialPath =
            MaterialFolder + "/UnknownAnimalStatic.mat";
        private const string UnknownObjectName = "Unknown Animal Static";

        static AnimalDiscoveryPrefabInstaller()
        {
            EditorApplication.delayCall += EnsureMuskratPrototype;
        }

        [MenuItem("Animal Game/Animals/Install Discovery Visual On Muskrat")]
        public static void EnsureMuskratPrototype()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                MuskratPrefabPath);
            Sprite maskSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                BodyFillSpritePath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                StaticShaderPath);
            if (prefab == null || maskSprite == null || shader == null)
                return;

            EnsureFolders();
            Material material = EnsureStaticMaterial(shader);
            if (material == null)
                return;

            HeightMapPlacedObject prefabPlacement =
                prefab.GetComponent<HeightMapPlacedObject>();
            // The asset is a reusable prototype. Scene instances own their
            // authored map coordinates as prefab overrides; the prototype
            // itself must never inherit a scene placement while its contents
            // are opened by an editor installer.
            Vector2 mapPosition = Vector2.zero;
            float surfaceHeight = 0f;
            bool prototypePlacementNeedsRepair = prefabPlacement != null
                && (prefabPlacement.MapPositionMeters.sqrMagnitude > 0.000001f
                    || Mathf.Abs(
                        prefabPlacement.SampledSurfaceHeightMeters) > 0.000001f);

            GameObject root = PrefabUtility.LoadPrefabContents(
                MuskratPrefabPath);
            bool changed = prototypePlacementNeedsRepair;
            try
            {
                DiscoverableEntity discoverable =
                    root.GetComponent<DiscoverableEntity>();
                if (discoverable == null)
                {
                    discoverable = root.AddComponent<DiscoverableEntity>();
                    discoverable.ConfigureEditorDefaults(
                        DiscoverableKind.Animal,
                        "muskrat",
                        false);
                    changed = true;
                }

                AnimalDiscoveryVisual discoveryVisual =
                    root.GetComponent<AnimalDiscoveryVisual>();
                if (discoveryVisual == null)
                {
                    discoveryVisual =
                        root.AddComponent<AnimalDiscoveryVisual>();
                    changed = true;
                }

                Transform visualRoot = root.transform.Find("Visual Root");
                if (visualRoot == null)
                    return;

                SpriteRenderer bodyFill = FindRenderer(
                    visualRoot,
                    "Body Fill");
                SpriteRenderer bodyOutline = FindRenderer(
                    visualRoot,
                    "Body Outline");
                SpriteRenderer directionIndicator = FindRenderer(
                    visualRoot,
                    "Direction Indicator");
                if (bodyFill == null
                    || bodyOutline == null
                    || directionIndicator == null)
                {
                    return;
                }

                Transform unknownTransform = visualRoot.Find(
                    UnknownObjectName);
                SpriteRenderer unknownRenderer;
                if (unknownTransform == null)
                {
                    var unknownObject = new GameObject(UnknownObjectName);
                    unknownTransform = unknownObject.transform;
                    unknownTransform.SetParent(visualRoot, false);
                    unknownTransform.localScale =
                        bodyFill.transform.localScale * 1.55f;
                    unknownRenderer =
                        unknownObject.AddComponent<SpriteRenderer>();
                    unknownRenderer.sprite = maskSprite;
                    unknownRenderer.color =
                        new Color(0.92f, 0.98f, 1f, 1f);
                    unknownRenderer.sortingLayerID =
                        bodyOutline.sortingLayerID;
                    unknownRenderer.sortingOrder = Mathf.Max(
                        bodyFill.sortingOrder,
                        Mathf.Max(
                            bodyOutline.sortingOrder,
                            directionIndicator.sortingOrder)) + 1;
                    unknownRenderer.sharedMaterial = material;
                    changed = true;
                }
                else
                {
                    unknownRenderer = unknownTransform.GetComponent<
                        SpriteRenderer>();
                    if (unknownRenderer == null)
                    {
                        unknownRenderer = unknownTransform.gameObject
                            .AddComponent<SpriteRenderer>();
                        changed = true;
                    }

                    if (unknownRenderer.sprite != maskSprite)
                    {
                        unknownRenderer.sprite = maskSprite;
                        changed = true;
                    }
                    if (unknownRenderer.sharedMaterial != material)
                    {
                        unknownRenderer.sharedMaterial = material;
                        changed = true;
                    }
                }

                var knownRenderers = new[]
                {
                    bodyFill,
                    bodyOutline,
                    directionIndicator
                };
                if (changed || !ReferencesAreInstalled(
                        discoveryVisual,
                        discoverable,
                        unknownRenderer))
                {
                    discoveryVisual.ConfigureEditorReferences(
                        discoverable,
                        knownRenderers,
                        unknownRenderer,
                        0.4f);
                    changed = true;
                }

                AnimalPlaceholderView placeholder =
                    root.GetComponent<AnimalPlaceholderView>();
                if (placeholder != null
                    && !PlaceholderReferencesVisual(
                        placeholder,
                        discoveryVisual))
                {
                    placeholder.ConfigureDiscoveryVisual(discoveryVisual);
                    changed = true;
                }

                HeightMapPlacedObject placement =
                    root.GetComponent<HeightMapPlacedObject>();
                if (placement != null)
                {
                    var serializedPlacement = new SerializedObject(placement);
                    serializedPlacement.FindProperty("mapPositionMeters")
                        .vector2Value = mapPosition;
                    serializedPlacement.FindProperty(
                            "sampledSurfaceHeightMeters")
                        .floatValue = surfaceHeight;
                    serializedPlacement.ApplyModifiedPropertiesWithoutUndo();
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, MuskratPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool ReferencesAreInstalled(
            AnimalDiscoveryVisual visual,
            DiscoverableEntity discoverable,
            SpriteRenderer unknownRenderer)
        {
            var serialized = new SerializedObject(visual);
            SerializedProperty stateProperty =
                serialized.FindProperty("discoverable");
            SerializedProperty unknownProperty =
                serialized.FindProperty("unknownRenderer");
            SerializedProperty knownProperty =
                serialized.FindProperty("knownRenderers");
            if (stateProperty.objectReferenceValue != discoverable
                || unknownProperty.objectReferenceValue != unknownRenderer
                || knownProperty.arraySize != 3)
            {
                return false;
            }

            Transform visualRoot = visual.transform.Find("Visual Root");
            if (visualRoot == null)
                return false;

            return knownProperty.GetArrayElementAtIndex(0)
                       .objectReferenceValue
                   == FindRenderer(visualRoot, "Body Fill")
                   && knownProperty.GetArrayElementAtIndex(1)
                       .objectReferenceValue
                   == FindRenderer(visualRoot, "Body Outline")
                   && knownProperty.GetArrayElementAtIndex(2)
                       .objectReferenceValue
                   == FindRenderer(visualRoot, "Direction Indicator");
        }

        private static bool PlaceholderReferencesVisual(
            AnimalPlaceholderView placeholder,
            AnimalDiscoveryVisual visual)
        {
            var serialized = new SerializedObject(placeholder);
            return serialized.FindProperty("discoveryVisual")
                .objectReferenceValue == visual;
        }

        private static SpriteRenderer FindRenderer(
            Transform visualRoot,
            string childName)
        {
            Transform child = visualRoot.Find(childName);
            return child != null
                ? child.GetComponent<SpriteRenderer>()
                : null;
        }

        private static Material EnsureStaticMaterial(Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                StaticMaterialPath);
            if (material != null)
                return material;

            material = new Material(shader)
            {
                name = "Unknown Animal Static"
            };
            material.SetColor(
                "_DarkColor",
                new Color(0.015f, 0.025f, 0.035f, 1f));
            material.SetColor(
                "_LightColor",
                new Color(0.78f, 0.94f, 1f, 1f));
            material.SetFloat("_NoiseCells", 20f);
            material.SetFloat("_NoiseFps", 18f);
            material.SetFloat("_EdgeDistortion", 0.12f);
            AssetDatabase.CreateAsset(material, StaticMaterialPath);
            return material;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "Animals");
        }
    }

    [CustomEditor(typeof(DiscoverableEntity))]
    public sealed class DiscoverableEntityEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (!EditorApplication.isPlaying)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Debug", EditorStyles.boldLabel);
            DiscoverableEntity discoverable =
                (DiscoverableEntity)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Discover"))
                    discoverable.SetDiscovered(true);
                if (GUILayout.Button("Reset Unknown"))
                    discoverable.ResetToUnknown();
            }
        }
    }
}
