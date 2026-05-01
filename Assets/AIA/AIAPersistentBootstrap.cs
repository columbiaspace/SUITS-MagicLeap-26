using UnityEngine;
using UnityEngine.SceneManagement;

public static class AIAPersistentBootstrap
{
    private const string AiaRootResourcePath = "AIARoot";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneBootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!AIASceneCatalog.IsAiaEnabledScene(scene.name))
        {
            if (AIAPersistentRoot.HasInstance)
            {
                AIAPersistentRoot.Instance.RefreshForScene(scene);
            }

            return;
        }

        if (!AIAPersistentRoot.HasInstance)
        {
            GameObject aiaRootPrefab = Resources.Load<GameObject>(AiaRootResourcePath);
            if (aiaRootPrefab == null)
            {
                Debug.LogError("[AIA] Could not load AIARoot prefab from Resources.");
                return;
            }

            Object.Instantiate(aiaRootPrefab);
        }

        if (AIAPersistentRoot.HasInstance)
        {
            AIAPersistentRoot.Instance.RefreshForScene(scene);
        }
    }
}
