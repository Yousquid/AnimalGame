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
        private const string InputManagerPath = "ProjectSettings/InputManager.asset";
        private const string TriggerThrottleAxisName = "Gamepad Trigger Throttle";
        private const string BalanceHorizontalAxisName = "Gamepad Balance Horizontal";
        private const string BalanceVerticalAxisName = "Gamepad Balance Vertical";
        private const string SonyBalanceHorizontalAxisName =
            "Gamepad Sony Balance Horizontal";
        private const string SonyBalanceVerticalAxisName =
            "Gamepad Sony Balance Vertical";
        private const string SonyLeftTriggerAxisName =
            "Gamepad Sony Left Trigger";
        private const string SonyRightTriggerAxisName =
            "Gamepad Sony Right Trigger";

        static RobotMapPrefabGenerator()
        {
            EditorApplication.delayCall += CreateMissingPrefabs;
            EditorApplication.delayCall += EnsureGamepadInputAxes;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
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

        [MenuItem("Animal Game/Repair Gamepad Input Axes")]
        public static void RepairGamepadInputAxes()
        {
            EnsureGamepadInputAxes();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += EnsureGamepadInputAxes;
        }

        private static void EnsureGamepadInputAxes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Object[] inputManagerAssets = AssetDatabase.LoadAllAssetsAtPath(
                InputManagerPath);
            if (inputManagerAssets == null || inputManagerAssets.Length == 0)
            {
                Debug.LogWarning("Could not load ProjectSettings/InputManager.asset.");
                return;
            }

            var inputManager = new SerializedObject(inputManagerAssets[0]);
            SerializedProperty axes = inputManager.FindProperty("m_Axes");
            if (axes == null)
            {
                Debug.LogWarning("Legacy Input Manager axes array was not found.");
                return;
            }

            bool changed = false;
            changed |= EnsureJoystickAxis(
                axes,
                TriggerThrottleAxisName,
                "Right Trigger Forward Left Trigger Reverse",
                "Left Trigger Reverse",
                2,
                false);
            changed |= EnsureJoystickAxis(
                axes,
                BalanceHorizontalAxisName,
                "Right Stick Horizontal Balance",
                "Counterbalance Left",
                3,
                false);
            changed |= EnsureJoystickAxis(
                axes,
                BalanceVerticalAxisName,
                "Right Stick Vertical Balance",
                "Counterbalance Backward",
                4,
                true);
            changed |= EnsureJoystickAxis(
                axes,
                SonyBalanceHorizontalAxisName,
                "Sony Right Stick Horizontal Balance",
                "Sony Counterbalance Left",
                2,
                false);
            changed |= EnsureJoystickAxis(
                axes,
                SonyBalanceVerticalAxisName,
                "Sony Right Stick Vertical Balance",
                "Sony Counterbalance Backward",
                5,
                true);
            changed |= EnsureJoystickAxis(
                axes,
                SonyLeftTriggerAxisName,
                "Sony Left Trigger Reverse",
                "Sony Left Trigger Released",
                3,
                false);
            changed |= EnsureJoystickAxis(
                axes,
                SonyRightTriggerAxisName,
                "Sony Right Trigger Forward",
                "Sony Right Trigger Released",
                4,
                false);

            if (!changed)
                return;

            inputManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("Repaired legacy gamepad movement and balance axes.");
        }

        private static bool EnsureJoystickAxis(
            SerializedProperty axes,
            string axisName,
            string descriptiveName,
            string descriptiveNegativeName,
            int axisIndex,
            bool invert)
        {
            for (int i = 0; i < axes.arraySize; i++)
            {
                SerializedProperty existingName = axes
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("m_Name");
                if (existingName != null && existingName.stringValue == axisName)
                    return false;
            }

            int newIndex = axes.arraySize;
            axes.InsertArrayElementAtIndex(newIndex);
            SerializedProperty axis = axes.GetArrayElementAtIndex(newIndex);
            SetString(axis, "m_Name", axisName);
            SetString(axis, "descriptiveName", descriptiveName);
            SetString(axis, "descriptiveNegativeName", descriptiveNegativeName);
            SetString(axis, "negativeButton", string.Empty);
            SetString(axis, "positiveButton", string.Empty);
            SetString(axis, "altNegativeButton", string.Empty);
            SetString(axis, "altPositiveButton", string.Empty);
            SetFloat(axis, "gravity", 0f);
            SetFloat(axis, "dead", 0.001f);
            SetFloat(axis, "sensitivity", 1f);
            SetBool(axis, "snap", false);
            SetBool(axis, "invert", invert);
            SetInt(axis, "type", 2);
            SetInt(axis, "axis", axisIndex);
            SetInt(axis, "joyNum", 0);
            return true;
        }

        private static void SetString(
            SerializedProperty parent,
            string propertyName,
            string value)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property != null)
                property.stringValue = value;
        }

        private static void SetFloat(
            SerializedProperty parent,
            string propertyName,
            float value)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property != null)
                property.floatValue = value;
        }

        private static void SetInt(
            SerializedProperty parent,
            string propertyName,
            int value)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property != null)
                property.intValue = value;
        }

        private static void SetBool(
            SerializedProperty parent,
            string propertyName,
            bool value)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property != null)
                property.boolValue = value;
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
            robot.AddComponent<RobotBalanceController>();
            robot.AddComponent<RobotHeightMotionDetector>();
            robot.AddComponent<RobotBalanceView>();
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
            cameraObject.AddComponent<RobotCameraShake>();
            PrefabUtility.SaveAsPrefabAsset(cameraObject, CameraPath);
            Object.DestroyImmediate(cameraObject);
        }
    }
}
