using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.RobotMap
{
    public static class RobotMapBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateDemo()
        {
            if (SceneManager.GetActiveScene().name != "SampleScene")
                return;

            if (Object.FindObjectOfType<RobotMapDemo>() != null)
                return;

            var root = new GameObject("Robot Map Demo");
            root.AddComponent<RobotMapDemo>();
        }
    }
}
