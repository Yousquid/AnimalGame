using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [InitializeOnLoad]
    public static class AnimalPrefabGenerator
    {
        private const string AnimalDataFolder = "Assets/Data/Animals";
        private const string AnimalPrefabFolder = "Assets/Prefabs/Animals";
        private const string MuskratPrefabFolder =
            AnimalPrefabFolder + "/Muskrat";
        private const string MuskratConfigPath =
            AnimalDataFolder + "/MuskratConfig.asset";
        private const string MuskratPrefabPath =
            MuskratPrefabFolder + "/Muskrat_Placeholder.prefab";

        private const string BushPrefabPath =
            "Assets/Prefabs/Environment/Vegetation/Test_Bush.prefab";
        private const string LotusPrefabPath =
            "Assets/Prefabs/Environment/Vegetation/Test_Lotus.prefab";
        private const string AustralisPrefabPath =
            "Assets/Prefabs/Environment/Vegetation/Test_Australis.prefab";

        private const string BodySpritePath =
            "Assets/Arts/robot_body_new.png";
        private const string BodyFillSpritePath =
            "Assets/Arts/robot_body_fill.png";
        private const string IndicatorSpritePath =
            "Assets/Arts/indicator_new.png";

        private const float MuskratVisibleBodyDiameter = 0.3f;
        private const float BodyArtworkVisibleDiameterPixels = 72.5f;
        private const float BodyFillVisibleDiameterPixels = 82.5f;
        private const float IndicatorScale = 0.66f;
        private const int AnimalSortingOrder = 1150;

        static AnimalPrefabGenerator()
        {
            EditorApplication.delayCall += EnsurePrototypeAssets;
        }

        [MenuItem("Animal Game/Animals/Rebuild Muskrat Prototype")]
        public static void RebuildMuskratPrototype()
        {
            EnsureFolders();
            AnimalSpeciesConfig config = EnsureMuskratConfig();
            EnsureFoodSource(BushPrefabPath, AnimalFoodType.Bush);
            EnsureFoodSource(LotusPrefabPath, AnimalFoodType.Lotus);
            EnsureFoodSource(AustralisPrefabPath, AnimalFoodType.Australis);
            CreateMuskratPrefab(config, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Rebuilt the muskrat prototype, configuration, and plant food markers.");
        }

        private static void EnsurePrototypeAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EnsureFolders();
            AnimalSpeciesConfig config = EnsureMuskratConfig();
            EnsureFoodSource(BushPrefabPath, AnimalFoodType.Bush);
            EnsureFoodSource(LotusPrefabPath, AnimalFoodType.Lotus);
            EnsureFoodSource(AustralisPrefabPath, AnimalFoodType.Australis);
            CreateMuskratPrefab(config, false);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Data");
            EnsureFolder("Assets/Data", "Animals");
            EnsureFolder("Assets/Prefabs", "Animals");
            EnsureFolder(AnimalPrefabFolder, "Muskrat");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static AnimalSpeciesConfig EnsureMuskratConfig()
        {
            AnimalSpeciesConfig config =
                AssetDatabase.LoadAssetAtPath<AnimalSpeciesConfig>(
                    MuskratConfigPath);
            if (config != null)
                return config;

            config = ScriptableObject.CreateInstance<AnimalSpeciesConfig>();
            config.name = "Muskrat Config";
            AssetDatabase.CreateAsset(config, MuskratConfigPath);
            return config;
        }

        private static void EnsureFoodSource(
            string prefabPath,
            AnimalFoodType foodType)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                return;

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                AnimalFoodSource source = root.GetComponent<AnimalFoodSource>();
                if (source != null)
                    return;

                source = root.AddComponent<AnimalFoodSource>();
                source.ConfigureEditorDefaults(foodType, 1f, 0.05f);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateMuskratPrefab(
            AnimalSpeciesConfig config,
            bool overwrite)
        {
            if (!overwrite
                && AssetDatabase.LoadAssetAtPath<GameObject>(MuskratPrefabPath)
                != null)
            {
                return;
            }

            Sprite bodySprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                BodySpritePath);
            Sprite bodyFillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                BodyFillSpritePath);
            Sprite indicatorSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                IndicatorSpritePath);
            if (bodySprite == null
                || bodyFillSprite == null
                || indicatorSprite == null)
            {
                Debug.LogWarning(
                    "Cannot create the muskrat placeholder because one or more robot placeholder sprites are missing.");
                return;
            }

            var root = new GameObject("Muskrat Placeholder");
            try
            {
                HeightMapPlacedObject placedObject =
                    root.AddComponent<HeightMapPlacedObject>();
                ConfigurePlacedObject(placedObject, config);

                root.AddComponent<AnimalMotor>();
                root.AddComponent<AnimalPerception>();
                AnimalPlaceholderView view =
                    root.AddComponent<AnimalPlaceholderView>();
                MuskratBehaviour behaviour =
                    root.AddComponent<MuskratBehaviour>();
                AnimalAgent agent = root.AddComponent<AnimalAgent>();

                var visualRootObject = new GameObject("Visual Root");
                visualRootObject.transform.SetParent(root.transform, false);

                SpriteRenderer fillRenderer = CreateRenderer(
                    visualRootObject.transform,
                    "Body Fill",
                    bodyFillSprite,
                    Color.black,
                    AnimalSortingOrder,
                    CalculateArtworkScale(
                        bodyFillSprite,
                        BodyFillVisibleDiameterPixels,
                        MuskratVisibleBodyDiameter));
                SpriteRenderer outlineRenderer = CreateRenderer(
                    visualRootObject.transform,
                    "Body Outline",
                    bodySprite,
                    new Color(0.92f, 0.98f, 1f, 1f),
                    AnimalSortingOrder + 1,
                    CalculateArtworkScale(
                        bodySprite,
                        BodyArtworkVisibleDiameterPixels,
                        MuskratVisibleBodyDiameter));
                SpriteRenderer indicatorRenderer = CreateRenderer(
                    visualRootObject.transform,
                    "Direction Indicator",
                    indicatorSprite,
                    new Color(0.92f, 0.98f, 1f, 1f),
                    AnimalSortingOrder + 2,
                    IndicatorScale);

                view.ConfigureEditorReferences(
                    visualRootObject.transform,
                    fillRenderer,
                    outlineRenderer,
                    indicatorRenderer);
                agent.ConfigureEditorDefaults(config, behaviour, view);

                // The prototype is an actor, not a map obstacle. Do not add a
                // HeightMapObstacleFootprint, Collider2D, or Rigidbody2D.
                PrefabUtility.SaveAsPrefabAsset(root, MuskratPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ConfigurePlacedObject(
            HeightMapPlacedObject placedObject,
            AnimalSpeciesConfig config)
        {
            var serialized = new SerializedObject(placedObject);
            serialized.FindProperty("map").objectReferenceValue = null;
            serialized.FindProperty("mapPositionMeters").vector2Value =
                Vector2.zero;
            serialized.FindProperty("sampledSurfaceHeightMeters").floatValue =
                0f;
            serialized.FindProperty("footprintRadiusMeters").floatValue =
                config != null ? config.BodyRadiusMeters : 0.16f;
            serialized.FindProperty("clampInsideMap").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SpriteRenderer CreateRenderer(
            Transform parent,
            string objectName,
            Sprite sprite,
            Color colour,
            int sortingOrder,
            float localScale)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(parent, false);
            child.transform.localScale = Vector3.one * localScale;
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = colour;
            renderer.sortingOrder = sortingOrder;
            renderer.sharedMaterial =
                AssetDatabase.GetBuiltinExtraResource<Material>(
                    "Sprites-Default.mat");
            return renderer;
        }

        private static float CalculateArtworkScale(
            Sprite sprite,
            float visibleDiameterPixels,
            float targetDiameter)
        {
            float artworkDiameter = visibleDiameterPixels
                                    / Mathf.Max(1f, sprite.pixelsPerUnit);
            return targetDiameter / Mathf.Max(0.0001f, artworkDiameter);
        }
    }
}
