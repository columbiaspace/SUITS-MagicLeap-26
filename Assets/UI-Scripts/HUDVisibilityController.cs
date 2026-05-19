using UnityEngine;
using UnityEngine.SceneManagement;

/// Singleton HUD visibility manager. Toggled by the open-palm gesture and by
/// MLVoice phrases "clear display" / "show display" (VoiceIntents static events).
/// Locates the scene's HUDRoot by name on each scene load; hidden state does not
/// persist across scenes. No-ops in the Starter scene.
public class HUDVisibilityController : MonoBehaviour
{
    public static HUDVisibilityController Instance { get; private set; }

    public const string HUDRootName = "HUDRoot";
    public const string StarterSceneName = "Starter";

    private GameObject hudRoot;
    private bool isStarterScene;
    private OpenPalmGestureDetector gestureDetector;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        gestureDetector = GetComponent<OpenPalmGestureDetector>();
        if (gestureDetector == null) gestureDetector = gameObject.AddComponent<OpenPalmGestureDetector>();
        gestureDetector.OnGestureTriggered.AddListener(Toggle);

        VoiceIntents.HudClearDisplayRequested += Hide;
        VoiceIntents.HudShowDisplayRequested += Show;
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindToScene(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (gestureDetector != null) gestureDetector.OnGestureTriggered.RemoveListener(Toggle);
        VoiceIntents.HudClearDisplayRequested -= Hide;
        VoiceIntents.HudShowDisplayRequested -= Show;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindToScene(scene);

    private void BindToScene(Scene scene)
    {
        isStarterScene = scene.name == StarterSceneName;
        if (gestureDetector != null) gestureDetector.enabled = !isStarterScene;
        if (isStarterScene) { hudRoot = null; return; }

        hudRoot = GameObject.Find(HUDRootName);
        if (hudRoot == null)
        {
            Debug.LogWarning($"[HUDVisibility] No '{HUDRootName}' in scene '{scene.name}'; toggle is a no-op until next scene load.");
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
