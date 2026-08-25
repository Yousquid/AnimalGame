using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [InitializeOnLoad]
    public static class VegetationContactFadePrefabInstaller
    {
        private const string VegetationPrefabFolder =
            "Assets/Prefabs/Environment/Vegetation";

        static VegetationContactFadePrefabInstaller()
        {
            EditorApplication.delayCall += () => EnsureAllVegetationPrefabs();
        }

        [MenuItem("Animal Game/Vegetation/Install Contact Fade On All Prefabs")]
        public static void ReinstallContactFade()
        {
            int changedCount = EnsureAllVegetationPrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                changedCount > 0
                    ? $"Installed vegetation contact fade on {changedCount} prefab(s)."
                    : "Every vegetation prefab already has contact fade installed.");
        }

        private static int EnsureAllVegetationPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return 0;

            int changedCount = 0;
            string[] prefabGuids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { VegetationPrefabFolder });
            for (int index = 0; index < prefabGuids.Length; index++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(
                    prefabGuids[index]);
                GameObject prefabAsset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                HeightMapPlacedObject assetPlacedObject = prefabAsset != null
                    ? prefabAsset.GetComponent<HeightMapPlacedObject>()
                    : null;
                Vector2 authoredMapPosition = assetPlacedObject != null
                    ? assetPlacedObject.MapPositionMeters
                    : Vector2.zero;
                float authoredSurfaceHeight = assetPlacedObject != null
                    ? assetPlacedObject.SampledSurfaceHeightMeters
                    : 0f;
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    HeightMapPlacedObject placedObject =
                        root.GetComponent<HeightMapPlacedObject>();
                    if (placedObject == null
                        || root.GetComponent<VegetationContactFade>() != null)
                    {
                        continue;
                    }

                    VegetationContactFade fade =
                        root.AddComponent<VegetationContactFade>();
                    fade.ConfigureEditorDefaults(
                        3f,
                        0.5f,
                        0.9f,
                        0.05f,
                        0.75f);
                    RestoreAuthoredMapMetadata(
                        placedObject,
                        authoredMapPosition,
                        authoredSurfaceHeight);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    changedCount++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (changedCount > 0)
                AssetDatabase.SaveAssets();
            return changedCount;
        }

        private static void RestoreAuthoredMapMetadata(
            HeightMapPlacedObject placedObject,
            Vector2 mapPosition,
            float sampledSurfaceHeight)
        {
            var serialized = new SerializedObject(placedObject);
            serialized.FindProperty("mapPositionMeters").vector2Value =
                mapPosition;
            serialized.FindProperty("sampledSurfaceHeightMeters").floatValue =
                sampledSurfaceHeight;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
