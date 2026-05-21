using UnityEngine;
using UnityEngine.SceneManagement;

/// Singleton HUD visibility manager. Toggled by MLVoice phrases
/// "remove display" / "show display" (VoiceIntents static events).
/// Locates the scene's HUDRoot by name on each scene load; hidden state does not
/// persist across scenes. No-ops in the Starter scene.
///
/// NOTE: the open-palm gesture path (OpenPalmGestureDetector) was disabled because
/// incidental open-palm poses were toggling the HUD off without the user prompting it.
/// Visibility is now voice-only.
public class HUDVisibilityController : MonoBehaviour
{
    public static HUDVisibilityController Instance { get; private set; }

    public const string HUDRootName = "HUDRoot";
    public const string StarterSceneName = "Starter";

    private GameObject hudRoot;
    private bool isStarterScene;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        VoiceIntents.HudClearDisplayRequested += Hide;
        VoiceIntents.HudShowDisplayRequested += Show;
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindToScene(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        VoiceIntents.HudClearDisplayRequested -= Hide;
        VoiceIntents.HudShowDisplayRequested -= Show;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindToScene(scene);

    private void BindToScene(Scene scene)
    {
        isStarterScene = scene.name == StarterSceneName;
        if (isStarterScene) { hudRoot = null; return; }

        hudRoot = GameObject.Find(HUDRootName);
        if (hudRoot == null)
        {
            Debug.LogWarning($"[HUDVisibility] No '{HUDRootName}' in scene '{scene.name}'; show/hide is a no-op until next scene load.");
            return;
        }
        hudRoot.SetActive(true);
    }

    public void Show()   { if (!isStarterScene && hudRoot != null) hudRoot.SetActive(true); }
    public void Hide()   { if (!isStarterScene && hudRoot != null) hudRoot.SetActive(false); }
    public void Toggle() { if (!isStarterScene && hudRoot != null) hudRoot.SetActive(!hudRoot.activeSelf); }

    /// Override for safety-critical paths that need to force the HUD visible.
    public void ForceShow() { if (hudRoot != null) hudRoot.SetActive(true); }
}
