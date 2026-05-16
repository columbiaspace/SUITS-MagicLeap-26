using UnityEngine;
using UnityEngine.SceneManagement;

public static class HUDVisibilityBootstrap
{
    private const string StarterSceneName = "Starter";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= MaybeInstantiate;
        SceneManager.sceneLoaded += MaybeInstantiate;
        MaybeInstantiate(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void MaybeInstantiate(Scene scene, LoadSceneMode mode)
    {
        if (HUDVisibilityController.Instance != null) return;
        if (scene.name == StarterSceneName) return;
        new GameObject(nameof(HUDVisibilityController)).AddComponent<HUDVisibilityController>();
    }
}
