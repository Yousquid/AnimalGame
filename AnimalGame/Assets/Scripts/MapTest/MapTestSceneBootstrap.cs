using UnityEngine;

namespace AnimalGame.MapTest
{
    public sealed class MapTestSceneBootstrap : MonoBehaviour
    {
        private const string ControllerResourcePath = "MapTest/MapTestController";

        private void Awake()
        {
            if (FindObjectOfType<MapTestSceneController>() != null)
                return;

            GameObject controllerPrefab = Resources.Load<GameObject>(ControllerResourcePath);
            if (controllerPrefab == null)
            {
                Debug.LogError($"Missing Resources prefab: {ControllerResourcePath}");
                return;
            }

            GameObject controller = Instantiate(controllerPrefab);
            controller.name = "Map Test Controller";
        }
    }
}
