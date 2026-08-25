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
        private const string StaticShaderPath =
            "Assets/Shaders/UnknownAnimalStatic.shader";
        private const string MaterialFolder = "Assets/Materials/Animals";
        private const string StaticMaterialPath =
            MaterialFolder + "/UnknownAnimalStatic.mat";
        private const string StaticMaskTexturePath =
            MaterialFolder + "/UnknownAnimalStaticMask.asset";
        private const string UnknownObjectName = "Unknown Animal Static";
        private const float UnknownFieldScaleFromBodyFill = 2.4f;

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
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                StaticShaderPath);
            if (prefab == null || shader == null)
                return;

            EnsureFolders();
            Sprite maskSprite = EnsureFullRectMaskSprite();
            Material material = EnsureStaticMaterial(shader);
            if (maskSprite == null || material == null)
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
                    unknownRenderer =
                        unknownObject.AddComponent<SpriteRenderer>();
                    unknownRenderer.sprite = maskSprite;
                    unknownRenderer.color = Color.white;
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
                    if (unknownRenderer.color != Color.white)
                    {
                        unknownRenderer.color = Color.white;
                        changed = true;
                    }
                }

                Vector3 desiredUnknownScale = Vector3.one
                    * bodyFill.transform.localScale.x
                    * UnknownFieldScaleFromBodyFill;
                if ((unknownTransform.localScale - desiredUnknownScale)
                    .sqrMagnitude > 0.000001f)
                {
                    unknownTransform.localScale = desiredUnknownScale;
                    changed = true;
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
            bool created = material == null;
            if (created)
            {
                material = new Material(shader)
                {
                    name = "Unknown Animal Static"
                };
            }

            bool changed = false;
            changed |= SetShaderIfDifferent(material, shader);
            changed |= SetColorIfDifferent(
                material,
                "_DarkColor",
                new Color(0.005f, 0.005f, 0.005f, 1f));
            changed |= SetColorIfDifferent(
                material,
                "_LightColor",
                Color.white);
            changed |= SetFloatIfDifferent(material, "_NoiseCells", 12f);
            changed |= SetFloatIfDifferent(material, "_NoiseFps", 11f);
            changed |= SetFloatIfDifferent(material, "_ScrollRate", 0.22f);
            changed |= SetFloatIfDifferent(material, "_FieldCoverage", 0.72f);
            changed |= SetFloatIfDifferent(material, "_FieldRadius", 0.46f);
            changed |= SetFloatIfDifferent(material, "_FieldDrift", 0.16f);
            changed |= SetFloatIfDifferent(material, "_BlockFill", 0.88f);
            changed |= SetFloatIfDifferent(material, "_ClusterContrast", 0.8f);

            if (created)
                AssetDatabase.CreateAsset(material, StaticMaterialPath);
            else if (changed)
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            return material;
        }

        private static Sprite EnsureFullRectMaskSprite()
        {
            Object[] existingAssets = AssetDatabase.LoadAllAssetsAtPath(
                StaticMaskTexturePath);
            for (int i = 0; i < existingAssets.Length; i++)
            {
                if (existingAssets[i] is Sprite existingSprite)
                    return existingSprite;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                StaticMaskTexturePath);
            if (texture == null)
            {
                texture = new Texture2D(
                    8,
                    8,
                    TextureFormat.RGBA32,
                    false)
                {
                    name = "Unknown Animal Static Mask Texture",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                var pixels = new Color[8 * 8];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = Color.white;
                texture.SetPixels(pixels);
                texture.Apply(false, false);
                AssetDatabase.CreateAsset(texture, StaticMaskTexturePath);
            }

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width,
                0u,
                SpriteMeshType.FullRect);
            sprite.name = "Unknown Animal Static Full Rect";
            AssetDatabase.AddObjectToAsset(sprite, texture);
            EditorUtility.SetDirty(texture);
            EditorUtility.SetDirty(sprite);
            AssetDatabase.SaveAssetIfDirty(texture);
            AssetDatabase.SaveAssetIfDirty(sprite);
            return sprite;
        }

        private static bool SetShaderIfDifferent(
            Material material,
            Shader shader)
        {
            if (material.shader == shader)
                return false;

            material.shader = shader;
            return true;
        }

        private static bool SetColorIfDifferent(
            Material material,
            string propertyName,
            Color value)
        {
            if (material.HasProperty(propertyName)
                && material.GetColor(propertyName) == value)
            {
                return false;
            }

            material.SetColor(propertyName, value);
            return true;
        }

        private static bool SetFloatIfDifferent(
            Material material,
            string propertyName,
            float value)
        {
            if (material.HasProperty(propertyName)
                && Mathf.Approximately(
                    material.GetFloat(propertyName),
                    value))
            {
                return false;
            }

            material.SetFloat(propertyName, value);
            return true;
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
