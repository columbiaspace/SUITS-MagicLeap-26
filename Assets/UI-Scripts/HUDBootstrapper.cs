using UnityEngine;
using UnityEngine.SceneManagement;

/// Ensures a HUDVisibilityController singleton exists on first non-Starter scene
/// load. Idempotent across scene transitions; the controller's own DontDestroyOnLoad
/// keeps it alive once created.
internal static class HUDBootstrapper
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        EnsureFor(SceneManager.GetActiveScene());
        SceneManager.sceneLoaded += (scene, mode) => EnsureFor(scene);
    }

    private static void EnsureFor(Scene scene)
    {
        if (scene.name == HUDVisibilityController.StarterSceneName) return;
        if (HUDVisibilityController.Instance != null) return;
        var go = new GameObject("HUDVisibilityController");
        go.AddComponent<HUDVisibilityController>();
    }
}
