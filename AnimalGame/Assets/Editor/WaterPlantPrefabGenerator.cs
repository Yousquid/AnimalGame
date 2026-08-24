using System.Collections.Generic;
using System.IO;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class WaterPlantPrefabGenerator
{
    private const string VegetationFolder =
        "Assets/Prefabs/Environment/Vegetation";
    private const string LotusSpritePath =
        "Assets/Arts/Trees/Test_Lotus.png";
    private const string LotusBackgroundSpritePath =
        "Assets/Arts/Trees/Test_Lotus_Background.png";
    private const string AustralisSpritePath =
        "Assets/Arts/Trees/Test_australis.png";
    private const string LotusPrefabPath =
        VegetationFolder + "/Test_Lotus.prefab";
    private const string AustralisPrefabPath =
        VegetationFolder + "/Test_Australis.prefab";
    private const int VegetationSortingOrder = 1101;

    static WaterPlantPrefabGenerator()
    {
        EditorApplication.delayCall += CreateMissingPrefabs;
    }

    [MenuItem("Animal Game/Level/Rebuild Water Plant Prefabs")]
    public static void RebuildPrefabs()
    {
        Sprite lotusBackground = CreateOrLoadLotusBackground(true);
        CreatePrefab(
            "Test_Lotus",
            LotusSpritePath,
            LotusPrefabPath,
            0.64f,
            lotusBackground,
            true);
        CreatePrefab(
            "Test_Australis",
            AustralisSpritePath,
            AustralisPrefabPath,
            0.45f,
            null,
            true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void CreateMissingPrefabs()
    {
        Sprite lotusBackground = CreateOrLoadLotusBackground(false);
        CreatePrefab(
            "Test_Lotus",
            LotusSpritePath,
            LotusPrefabPath,
            0.64f,
            lotusBackground,
            false);
        CreatePrefab(
            "Test_Australis",
            AustralisSpritePath,
            AustralisPrefabPath,
            0.45f,
            null,
            false);
        EnsureLotusBackgroundOnExistingPrefab(lotusBackground);
        AssetDatabase.SaveAssets();
    }

    private static void CreatePrefab(
        string objectName,
        string spritePath,
        string prefabPath,
        float editorFootprintRadiusMeters,
        Sprite backgroundSprite,
        bool overwrite)
    {
        if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            return;

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (sprite == null)
        {
            Debug.LogWarning(
                $"Cannot build {objectName}: no Sprite was imported at {spritePath}.");
            return;
        }

        var root = new GameObject(objectName);
        try
        {
            HeightMapPlacedObject placedObject =
                root.AddComponent<HeightMapPlacedObject>();
            var serializedPlacedObject = new SerializedObject(placedObject);
            serializedPlacedObject.FindProperty("map").objectReferenceValue = null;
            serializedPlacedObject.FindProperty("mapPositionMeters")
                .vector2Value = Vector2.zero;
            serializedPlacedObject.FindProperty("sampledSurfaceHeightMeters")
                .floatValue = 0f;
            serializedPlacedObject.FindProperty("footprintRadiusMeters")
                .floatValue = editorFootprintRadiusMeters;
            serializedPlacedObject.FindProperty("clampInsideMap").boolValue = true;
            serializedPlacedObject.ApplyModifiedPropertiesWithoutUndo();

            if (backgroundSprite != null)
            {
                CreateSpriteVisual(
                    root.transform,
                    "Background",
                    backgroundSprite,
                    Color.black,
                    VegetationSortingOrder - 1);
            }

            CreateSpriteVisual(
                root.transform,
                "Visual",
                sprite,
                Color.white,
                VegetationSortingOrder);

            // Deliberately do not add HeightMapObstacleFootprint, Collider2D,
            // or Rigidbody2D. Lotus and australis are decorative water plants;
            // their complete visible area remains traversable by the player.
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void CreateSpriteVisual(
        Transform parent,
        string objectName,
        Sprite sprite,
        Color colour,
        int sortingOrder)
    {
        var visual = new GameObject(objectName);
        visual.transform.SetParent(parent, false);
        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = colour;
        renderer.sortingOrder = sortingOrder;
        renderer.sharedMaterial =
            AssetDatabase.GetBuiltinExtraResource<Material>(
                "Sprites-Default.mat");
    }

    private static void EnsureLotusBackgroundOnExistingPrefab(
        Sprite backgroundSprite)
    {
        if (backgroundSprite == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(LotusPrefabPath) == null)
        {
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(LotusPrefabPath);
        try
        {
            Transform background = root.transform.Find("Background");
            SpriteRenderer renderer;
            if (background == null)
            {
                var backgroundObject = new GameObject("Background");
                backgroundObject.transform.SetParent(root.transform, false);
                backgroundObject.transform.SetSiblingIndex(0);
                renderer = backgroundObject.AddComponent<SpriteRenderer>();
            }
            else
            {
                renderer = background.GetComponent<SpriteRenderer>();
                if (renderer == null)
                    renderer = background.gameObject.AddComponent<SpriteRenderer>();
            }

            renderer.sprite = backgroundSprite;
            renderer.color = Color.black;
            renderer.sortingOrder = VegetationSortingOrder - 1;
            renderer.sharedMaterial =
                AssetDatabase.GetBuiltinExtraResource<Material>(
                    "Sprites-Default.mat");
            PrefabUtility.SaveAsPrefabAsset(root, LotusPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Sprite CreateOrLoadLotusBackground(bool regenerate)
    {
        Sprite existing =
            AssetDatabase.LoadAssetAtPath<Sprite>(LotusBackgroundSpritePath);
        if (!regenerate && existing != null)
            return existing;

        Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(LotusSpritePath);
        Sprite sourceSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LotusSpritePath);
        if (source == null || sourceSprite == null)
        {
            Debug.LogWarning(
                $"Cannot build the lotus background: no texture was imported at {LotusSpritePath}.");
            return existing;
        }

        Texture2D readable = CopyToReadableTexture(source);
        Texture2D background = null;
        try
        {
            Color32[] sourcePixels = readable.GetPixels32();
            Color32[] backgroundPixels = BuildClosedInteriorMask(
                sourcePixels,
                readable.width,
                readable.height);
            background = new Texture2D(
                readable.width,
                readable.height,
                TextureFormat.RGBA32,
                false);
            background.name = "Test Lotus Background";
            background.SetPixels32(backgroundPixels);
            background.Apply(false, false);

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return existing;

            string absoluteOutputPath = Path.Combine(
                projectRoot,
                LotusBackgroundSpritePath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(absoluteOutputPath, background.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(readable);
            if (background != null)
                Object.DestroyImmediate(background);
        }

        AssetDatabase.ImportAsset(
            LotusBackgroundSpritePath,
            ImportAssetOptions.ForceSynchronousImport
            | ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(LotusBackgroundSpritePath)
            is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = sourceSprite.pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(textureSettings);
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(LotusBackgroundSpritePath);
    }

    private static Texture2D CopyToReadableTexture(Texture2D source)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Default);
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var copy = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false);
            copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            copy.Apply(false, false);
            return copy;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private static Color32[] BuildClosedInteriorMask(
        Color32[] sourcePixels,
        int width,
        int height)
    {
        const byte outlineAlphaThreshold = 32;
        var exterior = new bool[sourcePixels.Length];
        var openPixels = new Queue<int>();

        for (int x = 0; x < width; x++)
        {
            TryAddExteriorPixel(
                x,
                0,
                width,
                height,
                sourcePixels,
                exterior,
                openPixels,
                outlineAlphaThreshold);
            TryAddExteriorPixel(
                x,
                height - 1,
                width,
                height,
                sourcePixels,
                exterior,
                openPixels,
                outlineAlphaThreshold);
        }

        for (int y = 1; y < height - 1; y++)
        {
            TryAddExteriorPixel(
                0,
                y,
                width,
                height,
                sourcePixels,
                exterior,
                openPixels,
                outlineAlphaThreshold);
            TryAddExteriorPixel(
                width - 1,
                y,
                width,
                height,
                sourcePixels,
                exterior,
                openPixels,
                outlineAlphaThreshold);
        }

        while (openPixels.Count > 0)
        {
            int index = openPixels.Dequeue();
            int x = index % width;
            int y = index / width;
            TryAddExteriorPixel(
                x - 1,
                y,
                width,
                height,
                sourcePixels,
                exterior,
                openPixels,
                outlineAlphaThreshold);
            TryAddExteriorPixel(
                x + 1,
                y,
                width,
                height,
                sourcePixels,
                exterior,
                openPixels,
                outlineAlphaThreshold);
            TryAddExteriorPixel(
                x,
                y - 1,
                width,
                height,
                sourcePixels,
                exterior,
                openPixels,
                outlineAlphaThreshold);
            TryAddExteriorPixel(
                x,
                y + 1,
                width,
                height,
                sourcePixels,
                exterior,
                openPixels,
                outlineAlphaThreshold);
        }

        var result = new Color32[sourcePixels.Length];
        for (int index = 0; index < result.Length; index++)
        {
            byte sourceAlpha = sourcePixels[index].a;
            byte outputAlpha = sourceAlpha > outlineAlphaThreshold
                ? sourceAlpha
                : exterior[index] ? (byte)0 : (byte)255;
            result[index] = new Color32(255, 255, 255, outputAlpha);
        }

        return result;
    }

    private static void TryAddExteriorPixel(
        int x,
        int y,
        int width,
        int height,
        Color32[] sourcePixels,
        bool[] exterior,
        Queue<int> openPixels,
        byte outlineAlphaThreshold)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return;

        int index = y * width + x;
        if (exterior[index] || sourcePixels[index].a > outlineAlphaThreshold)
            return;

        exterior[index] = true;
        openPixels.Enqueue(index);
    }
}
