using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame
{
    /// <summary>
    /// Provides a project-wide development shortcut for reloading the active scene.
    /// The listener survives scene loads so every scene gets the same behaviour
    /// without requiring a component to be added to each scene manually.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneReloadShortcut : MonoBehaviour
    {
        private static SceneReloadShortcut instance;
        private bool reloadInProgress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateListener()
        {
            if (instance != null)
                return;

            var listenerObject = new GameObject("Scene Reload Shortcut");
            instance = listenerObject.AddComponent<SceneReloadShortcut>();
            DontDestroyOnLoad(listenerObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Update()
        {
            if (!reloadInProgress && Input.GetKeyDown(KeyCode.R))
                ReloadActiveScene();
        }

        private void ReloadActiveScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
                return;

            reloadInProgress = true;
            if (activeScene.buildIndex >= 0)
            {
                SceneManager.LoadScene(activeScene.buildIndex);
                return;
            }

            if (!string.IsNullOrEmpty(activeScene.path))
            {
                SceneManager.LoadScene(activeScene.path);
                return;
            }

            reloadInProgress = false;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            reloadInProgress = false;
        }
    }
}
