using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Reads procedure step strings aloud on device (Android TTS), matching the Luna stack in <see cref="VoiceIntents"/>.
/// Add this component to scenes that drive checklist UI (egress / ingress).
/// </summary>
public sealed class ProcedureStepSpeaker : MonoBehaviour
{
    [SerializeField] private bool announceSteps = true;

    private AndroidJavaObject _tts;
    private AndroidJavaObject _unityActivity;
    private volatile bool _ttsReady;

    private Coroutine _announceCo;

    private void Start()
    {
        InitializeTextToSpeech();
    }

    private void OnDestroy()
    {
        if (_announceCo != null)
        {
            StopCoroutine(_announceCo);
            _announceCo = null;
        }

        ShutdownTts();
    }

    /// <summary>Speaks normalized step text when <see cref="announceSteps"/> is enabled.</summary>
    public void Announce(string rawText)
    {
        if (!announceSteps)
        {
            return;
        }

        string normalized = NormalizeTextForSpeech(rawText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (_announceCo != null)
        {
            StopCoroutine(_announceCo);
        }

        _announceCo = StartCoroutine(SpeakWhenReadyCoroutine(normalized));
    }

    private IEnumerator SpeakWhenReadyCoroutine(string normalizedText)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const float timeout = 8f;
        float deadline = Time.unscaledTime + timeout;
        while (!_ttsReady && Time.unscaledTime < deadline && isActiveAndEnabled)
        {
            yield return null;
        }

        if (!_ttsReady || _tts == null)
        {
            Debug.LogWarning("[Procedure-TTS] TTS not ready; skipping announcement.");
            _announceCo = null;
            yield break;
        }

        SpeakAndroid(normalizedText);
#else
        Debug.Log($"[Procedure-TTS] (editor) {normalizedText}");
#endif
        yield return null;
        _announceCo = null;
    }

    private void InitializeTextToSpeech()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                _tts = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    _unityActivity,
                    new ProcedureTtsInitListener(this));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Procedure-TTS] init failed: {e.Message}");
        }
#endif
    }

    internal void ApplyTtsInitStatus(int status)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bool ready = false;
        try
        {
            using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
            {
                int successStatus = ttsClass.GetStatic<int>("SUCCESS");
                ready = status == successStatus && _tts != null;
            }

            if (ready && _tts != null)
            {
                using (var localeClass = new AndroidJavaClass("java.util.Locale"))
                using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
                {
                    AndroidJavaObject locale = localeClass.GetStatic<AndroidJavaObject>("US");
                    int languageResult = _tts.Call<int>("setLanguage", locale);
                    int langMissingData = ttsClass.GetStatic<int>("LANG_MISSING_DATA");
                    int langNotSupported = ttsClass.GetStatic<int>("LANG_NOT_SUPPORTED");
                    if (languageResult == langMissingData || languageResult == langNotSupported)
                    {
                        Debug.LogWarning("[Procedure-TTS] US locale missing or not supported.");
                    }
                }
            }
            else if (!ready)
            {
                Debug.LogWarning($"[Procedure-TTS] not ready (status={status}).");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Procedure-TTS] OnInit callback: {e.Message}");
            ready = false;
        }

        _ttsReady = ready;
#endif
    }

    private void SpeakAndroid(string normalizedText)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
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
                        int speakResult = _tts.Call<int>(
                            "speak",
                            normalizedText,
                            queueFlush,
                            null,
                            $"procedure-step-{Time.frameCount}");

                        if (speakResult == errorCode)
                        {
                            Debug.LogWarning("[Procedure-TTS] Android TextToSpeech returned ERROR from speak().");
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Procedure-TTS] speak UI thread failed: {e.Message}");
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
        catch (Exception e)
        {
            Debug.LogWarning($"[Procedure-TTS] speak failed: {e.Message}");
        }
#endif
    }

    private void ShutdownTts()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_tts == null)
        {
            return;
        }

        try
        {
            _tts.Call("stop");
            _tts.Call("shutdown");
            _tts.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Procedure-TTS] shutdown failed: {e.Message}");
        }

        _tts = null;
        _unityActivity = null;
#endif
        _ttsReady = false;
    }

    private static string NormalizeTextForSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Trim();

        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized;
    }

    private sealed class ProcedureTtsInitListener : AndroidJavaProxy
    {
        private readonly ProcedureStepSpeaker _owner;

        public ProcedureTtsInitListener(ProcedureStepSpeaker owner)
            : base("android.speech.tts.TextToSpeech$OnInitListener")
        {
            _owner = owner;
        }

        public void onInit(int status)
        {
            _owner.ApplyTtsInitStatus(status);
        }
    }
}
