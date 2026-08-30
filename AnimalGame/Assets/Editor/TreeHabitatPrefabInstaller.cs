using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [InitializeOnLoad]
    public static class TreeHabitatPrefabInstaller
    {
        private const string HealthyTreePrefabPath =
            "Assets/Prefabs/Environment/Vegetation/Test_Tree.prefab";
        private const string DeadTreePrefabPath =
            "Assets/Prefabs/Environment/Vegetation/Test_Dead_Tree.prefab";

        static TreeHabitatPrefabInstaller()
        {
            EditorApplication.delayCall += () => EnsureTreeHabitats();
        }

        [MenuItem("Animal Game/Vegetation/Install Tree Habitats")]
        public static void ReinstallTreeHabitats()
        {
            int changedCount = EnsureTreeHabitats();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                changedCount > 0
                    ? $"Installed or updated habitat data on {changedCount} tree prefab(s)."
                    : "Both tree prefabs already have the correct habitat data.");
        }

        private static int EnsureTreeHabitats()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return 0;

            int changedCount = 0;
            if (EnsureTreeHabitat(
                    HealthyTreePrefabPath,
                    TreeHealthState.Healthy))
            {
                changedCount++;
            }

            if (EnsureTreeHabitat(
                    DeadTreePrefabPath,
                    TreeHealthState.Dead))
            {
                changedCount++;
            }

            if (changedCount > 0)
                AssetDatabase.SaveAssets();
            return changedCount;
        }

        private static bool EnsureTreeHabitat(
            string prefabPath,
            TreeHealthState health)
        {
            GameObject prefabAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                return false;

            HeightMapPlacedObject assetPlacedObject =
                prefabAsset.GetComponent<HeightMapPlacedObject>();
            Vector2 authoredMapPosition = assetPlacedObject != null
                ? assetPlacedObject.MapPositionMeters
                : Vector2.zero;
            float authoredSurfaceHeight = assetPlacedObject != null
                ? assetPlacedObject.SampledSurfaceHeightMeters
                : 0f;

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                TreeHabitat habitat = root.GetComponent<TreeHabitat>();
                bool changed = habitat == null;
                if (habitat == null)
                    habitat = root.AddComponent<TreeHabitat>();

                Transform trunkCenter = root.transform.Find("Solid Core");
                if (trunkCenter == null)
                    trunkCenter = root.transform;

                HeightMapObstacleFootprint footprint =
                    trunkCenter.GetComponent<HeightMapObstacleFootprint>();
                if (footprint == null)
                {
                    footprint = root.GetComponentInChildren<
                        HeightMapObstacleFootprint>(true);
                }

                float perchRadius = footprint != null
                    ? footprint.RadiusMeters
                    : 0.6f;
                changed |= habitat.ConfigureEditorDefaults(
                    health,
                    trunkCenter,
                    perchRadius);

                HeightMapPlacedObject placedObject =
                    root.GetComponent<HeightMapPlacedObject>();
                RestoreAuthoredMapMetadata(
                    placedObject,
                    authoredMapPosition,
                    authoredSurfaceHeight);

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RestoreAuthoredMapMetadata(
            HeightMapPlacedObject placedObject,
            Vector2 mapPosition,
            float sampledSurfaceHeight)
        {
            if (placedObject == null)
                return;

            var serialized = new SerializedObject(placedObject);
            serialized.FindProperty("mapPositionMeters").vector2Value =
                mapPosition;
            serialized.FindProperty("sampledSurfaceHeightMeters").floatValue =
                sampledSurfaceHeight;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
