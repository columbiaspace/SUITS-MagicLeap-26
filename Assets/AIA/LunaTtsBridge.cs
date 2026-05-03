using System;
using UnityEngine;

/// <summary>
/// Persistent (DontDestroyOnLoad) Android TextToSpeech wrapper. Lets any scene
/// speak through the same voice without each scene re-initializing the engine.
///
/// Place ONE instance in the bootstrap/Mission scene. VoiceIntents and any other
/// caller can fetch it via <see cref="Instance"/> and call <see cref="Speak"/>.
/// On non-Android platforms (e.g. Editor Play Mode on macOS) Speak is a no-op
/// that just logs the text, so flow logic still runs.
/// </summary>
public class LunaTtsBridge : MonoBehaviour
{
    public static LunaTtsBridge Instance { get; private set; }

    [Tooltip("Log every Speak() call so the editor can show the spoken text in the Console.")]
    public bool logSpokenText = true;

    private AndroidJavaObject _textToSpeech;
    private AndroidJavaObject _unityActivity;
    private volatile bool _ready;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeTextToSpeech();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        DisposeTextToSpeech();
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string normalized = NormalizeForSpeech(text);

        if (logSpokenText)
        {
            Debug.Log($"[LunaTts] Speak: {normalized}");
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!_ready || _textToSpeech == null)
        {
            Debug.LogWarning("[LunaTts] Speak called before TTS is ready; skipping.");
            return;
        }

        try
        {
            Action speakAction = () =>
            {
                try
                {
                    using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
                    {
                        int queueFlush = ttsClass.GetStatic<int>("QUEUE_FLUSH");
                        int errorCode = ttsClass.GetStatic<int>("ERROR");
                        int result = _textToSpeech.Call<int>(
                            "speak",
                            normalized,
                            queueFlush,
                            null,
                            $"luna-tts-{Time.frameCount}");

                        if (result == errorCode)
                        {
                            Debug.LogWarning("[LunaTts] Android TextToSpeech.speak() returned ERROR.");
                        }
                    }
                }
                catch (Exception innerEx)
                {
                    Debug.LogWarning($"[LunaTts] speak() failed on UI thread: {innerEx.Message}");
                }
            };

            if (_unityActivity != null)
            {
                _unityActivity.Call("runOnUiThread", new AndroidJavaRunnable(() => speakAction()));
            }
            else
            {
                speakAction();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LunaTts] Speak failed: {ex.Message}");
        }
#endif
    }

    private void InitializeTextToSpeech()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                _textToSpeech = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    _unityActivity,
                    new TtsInitListener(this));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LunaTts] init failed: {ex.Message}");
        }
#endif
    }

    private void OnTtsInitialized(int status)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
            {
                int success = ttsClass.GetStatic<int>("SUCCESS");
                if (status != success || _textToSpeech == null)
                {
                    Debug.LogWarning($"[LunaTts] Init status={status} (not SUCCESS).");
                    _ready = false;
                    return;
                }
            }

            using (var localeClass = new AndroidJavaClass("java.util.Locale"))
            using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
            {
                AndroidJavaObject locale = localeClass.GetStatic<AndroidJavaObject>("US");
                int langResult = _textToSpeech.Call<int>("setLanguage", locale);
                int langMissingData = ttsClass.GetStatic<int>("LANG_MISSING_DATA");
                int langNotSupported = ttsClass.GetStatic<int>("LANG_NOT_SUPPORTED");
                if (langResult == langMissingData || langResult == langNotSupported)
                {
                    Debug.LogWarning("[LunaTts] US locale missing or not supported.");
                }
            }

            _ready = true;
            Debug.Log("[LunaTts] Ready.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LunaTts] init callback error: {ex.Message}");
            _ready = false;
        }
#endif
    }

    private void DisposeTextToSpeech()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_textToSpeech == null)
        {
            return;
        }

        try
        {
            _textToSpeech.Call("stop");
            _textToSpeech.Call("shutdown");
            _textToSpeech.Dispose();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LunaTts] shutdown failed: {ex.Message}");
        }

        _textToSpeech = null;
        _unityActivity = null;
        _ready = false;
#endif
    }

    private static string NormalizeForSpeech(string text)
    {
        string normalized = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized;
    }

    private class TtsInitListener : AndroidJavaProxy
    {
        private readonly LunaTtsBridge _owner;

        public TtsInitListener(LunaTtsBridge owner)
            : base("android.speech.tts.TextToSpeech$OnInitListener")
        {
            _owner = owner;
        }

        public void onInit(int status)
        {
            _owner.OnTtsInitialized(status);
        }
    }
}
