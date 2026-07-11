using AnimalGame.RobotMap;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [InitializeOnLoad]
    public static class RobotMapPrefabGenerator
    {
        private const string RobotFolder = "Assets/Prefabs/Resources/Robot";
        private const string CameraFolder = "Assets/Prefabs/Resources/Camera";
        private const string RobotPath = RobotFolder + "/RobotMarker.prefab";
        private const string CameraPath = CameraFolder + "/RobotCamera.prefab";

        static RobotMapPrefabGenerator()
        {
            EditorApplication.delayCall += CreateMissingPrefabs;
        }

        [MenuItem("Animal Game/Rebuild Robot Map Prefabs")]
        public static void RebuildPrefabs()
        {
            EnsureFolders();
            CreateRobotPrefab();
            CreateCameraPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Robot marker and camera prefabs rebuilt.");
        }

        private static void CreateMissingPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(CameraPath) == null)
            {
                RebuildPrefabs();
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Prefabs");
            EnsureFolder("Assets/Prefabs", "Resources");
            EnsureFolder("Assets/Prefabs/Resources", "Robot");
            EnsureFolder("Assets/Prefabs/Resources", "Camera");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static void CreateRobotPrefab()
        {
            var robot = new GameObject("Robot Marker");
            robot.AddComponent<RobotMover>();
            robot.AddComponent<RobotMarkerView>();
            PrefabUtility.SaveAsPrefabAsset(robot, RobotPath);
            Object.DestroyImmediate(robot);
        }

        private static void CreateCameraPrefab()
        {
            var cameraObject = new GameObject("Robot Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 14f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.008f, 0.011f, 0.014f);
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;

            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<RobotCameraFollow>();
            PrefabUtility.SaveAsPrefabAsset(cameraObject, CameraPath);
            Object.DestroyImmediate(cameraObject);
        }
    }
}
