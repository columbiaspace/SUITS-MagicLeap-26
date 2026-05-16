using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.MagicLeap;

public class HUDVisibilityController : MonoBehaviour
{
    public static HUDVisibilityController Instance { get; private set; }

    private const string StarterSceneName = "Starter";
    private const string HudRootName = "HUDRoot";
    private const uint ClearDisplayIntentId = 114;
    private const uint ShowDisplayIntentId = 115;

    private GameObject _hudRoot;
    private OpenPalmGestureDetector _detector;
    private bool _listenersActive;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _detector = gameObject.AddComponent<OpenPalmGestureDetector>();
        _detector.OnGestureTriggered.AddListener(Toggle);

        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SetListenersActive(false);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == StarterSceneName)
        {
            _hudRoot = null;
            SetListenersActive(false);
            return;
        }

        SetListenersActive(true);
        _hudRoot = GameObject.Find(HudRootName);
        if (_hudRoot == null)
        {
            Debug.LogWarning($"[HUDVisibility] '{HudRootName}' not found in scene '{scene.name}'. Toggle is a no-op until next scene load.");
            return;
        }
        _hudRoot.SetActive(true);
    }

    public void Toggle()
    {
        if (_hudRoot != null) _hudRoot.SetActive(!_hudRoot.activeSelf);
    }

    public void ForceShow()
    {
        if (_hudRoot != null) _hudRoot.SetActive(true);
    }

    private void Hide() { if (_hudRoot != null) _hudRoot.SetActive(false); }
    private void Show() { if (_hudRoot != null) _hudRoot.SetActive(true); }

    private void SetListenersActive(bool active)
    {
        if (_listenersActive == active) return;
        _listenersActive = active;
        if (_detector != null) _detector.enabled = active;
        if (active) MLVoice.OnVoiceEvent += OnMLVoice;
        else MLVoice.OnVoiceEvent -= OnMLVoice;
    }

    private void OnMLVoice(in bool wasSuccessful, in MLVoice.IntentEvent ev)
    {
        if (!wasSuccessful) return;
        if (ev.EventID == ClearDisplayIntentId) Hide();
        else if (ev.EventID == ShowDisplayIntentId) Show();
    }
}
