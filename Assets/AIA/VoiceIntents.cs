using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.MagicLeap;

public class VoiceIntents : MonoBehaviour
{
    private const uint AskLunaEventId = 105;
    private const string AskLunaSlotName = "query";
    private const int AiRequestTimeoutSeconds = 60;
    /// <summary>Optional override for CI/editor. On device, use the Inspector URL.</summary>
    private const string GenerateUrlEnvironmentVariable = "LUNA_GENERATE_URL";

    private const string DefaultLunaPrompt =
        "Respond with your name as Luna Assistant and a description of your base model.";

    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();
    public MLVoiceIntentsConfiguration VoiceIntentsConfiguration;
    public GameObject targetObject;
    private bool shouldRotate = false;
    public float rotationSpeed = 50f;

    [Header("AI via Vercel proxy")]
    [Tooltip("HTTPS URL of your deployed proxy (Ollama /api/generate), e.g. https://your-app.vercel.app/api/generate — set once; Ollama IP lives only in Vercel (OLLAMA_BASE_URL).")]
    [SerializeField] private string lunaGenerateUrl = "";

    [SerializeField] private bool sendVoicePromptToAi = true;
    [SerializeField] private string aiModel = "gemma3:27b";
    [SerializeField] private bool logAiResponse = true;
    [SerializeField] private bool speakAiResponse = true;

    [Header("Debugging")]
    [SerializeField] private bool verboseVoiceLogging = true;

    private Coroutine aiRequestCoroutine;
    private AndroidJavaObject textToSpeech;
    private volatile bool textToSpeechReady;
    private bool isVoiceEventSubscribed;

    [Serializable]
    private class AiGenerateRequest
    {
        public string model;
        public string prompt;
        public bool stream;
    }

    [Serializable]
    private class AiGenerateResponse
    {
        public string response;
    }

    private void Start()
    {
        ApplyGenerateUrlFromEnvironment();
        MLPermissions.RequestPermission(MLPermission.VoiceInput, permissionCallbacks);
        InitializeTextToSpeech();
    }

    private void ApplyGenerateUrlFromEnvironment()
    {
        string fromEnv = Environment.GetEnvironmentVariable(GenerateUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(fromEnv))
            return;
        lunaGenerateUrl = fromEnv.Trim();
    }

    private string ResolveGenerateUrl()
    {
        if (!string.IsNullOrWhiteSpace(lunaGenerateUrl))
            return lunaGenerateUrl.Trim();
        string fromEnv = Environment.GetEnvironmentVariable(GenerateUrlEnvironmentVariable);
        return string.IsNullOrWhiteSpace(fromEnv) ? "" : fromEnv.Trim();
    }

    void Update()
    {
        if (shouldRotate && targetObject != null)
            targetObject.transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
    }

    private void Awake()
    {
        permissionCallbacks.OnPermissionGranted += OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied += OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain += OnPermissionDenied;
    }

    private void OnDestroy()
    {
        permissionCallbacks.OnPermissionGranted -= OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied -= OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain -= OnPermissionDenied;
        if (isVoiceEventSubscribed)
        {
            MLVoice.OnVoiceEvent -= MLVoiceOnVoiceEvent;
            isVoiceEventSubscribed = false;
        }

        DisposeTextToSpeech();
    }

    private void OnPermissionDenied(string permission)
    {
        Debug.LogError($"Failed to initialize voice intents due to missing or denied " +
                       $"{MLPermission.VoiceInput} permission. Please add to manifest. Disabling script.");
        enabled = false;
    }

    private void OnPermissionGranted(string permission)
    {
        if (permission == MLPermission.VoiceInput)
            InitializeVoiceInput();
    }

    private void InitializeVoiceInput()
    {
        if (VoiceIntentsConfiguration == null)
        {
            Debug.LogError("Voice intents configuration is not assigned. Disabling script.");
            enabled = false;
            return;
        }

        bool isVoiceEnabled = MLVoice.VoiceEnabled;

        if (isVoiceEnabled)
        {
            Debug.Log("Voice commands setting is enabled");
            var result = MLVoice.SetupVoiceIntents(VoiceIntentsConfiguration);
            if (result.IsOk)
            {
                Debug.Log("Voice intents successfully initialized");
                if (isVoiceEventSubscribed)
                    MLVoice.OnVoiceEvent -= MLVoiceOnVoiceEvent;
                MLVoice.OnVoiceEvent += MLVoiceOnVoiceEvent;
                isVoiceEventSubscribed = true;
            }
            else
            {
                Debug.LogError("Voice could not initialize: " + result);
            }
        }
        else
        {
            Debug.LogWarning("Voice commands setting is disabled - opening settings. " +
                             "Please enable voice input and relaunch the app.");
            UnityEngine.XR.MagicLeap.SettingsIntentsLauncher.LaunchSystemVoiceInputSettings();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    activity.Call<bool>("moveTaskToBack", true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"moveTaskToBack failed, falling back to Application.Quit: {e.Message}");
                Application.Quit();
            }
#else
            Application.Quit();
#endif
        }
    }

    private void MLVoiceOnVoiceEvent(in bool wasSuccessful, in MLVoice.IntentEvent voiceEvent)
    {
        if (verboseVoiceLogging)
            LogVoiceEvent(voiceEvent, wasSuccessful);

        if (!wasSuccessful)
        {
            Debug.LogWarning("Voice event was not successful. Ignoring intent callback.");
            return;
        }

        switch (voiceEvent.EventID)
        {
            case 101:
                Debug.Log("Show object");
                if (targetObject != null)
                    targetObject.SetActive(true);
                else
                    Debug.LogWarning("Show command received, but targetObject is not assigned.");
                break;

            case 102:
                Debug.Log("Hide object");
                if (targetObject != null)
                    targetObject.SetActive(false);
                else
                    Debug.LogWarning("Hide command received, but targetObject is not assigned.");
                break;

            case 103:
                Debug.Log("Start rotating object");
                shouldRotate = true;
                break;

            case 104:
                Debug.Log("Stop rotating object");
                shouldRotate = false;
                break;

            case AskLunaEventId:
                Debug.Log("Ask Luna intent detected");
                TrySendAskLunaPromptToAi(voiceEvent);
                break;

            default:
                Debug.Log($"Unhandled voice intent event id: {voiceEvent.EventID}");
                break;
        }
    }

    private void TrySendAskLunaPromptToAi(MLVoice.IntentEvent voiceEvent)
    {
        if (!sendVoicePromptToAi)
        {
            Debug.LogWarning("Ask Luna intent detected but AI forwarding is disabled.");
            return;
        }

        string url = ResolveGenerateUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            Debug.LogError(
                "[Luna] lunaGenerateUrl is empty. Set it in the VoiceIntents Inspector to your Vercel URL " +
                "(https://…/api/generate), or set env LUNA_GENERATE_URL for local builds.");
            return;
        }

        string prompt = ExtractSlotPrompt(voiceEvent, AskLunaSlotName);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            Debug.Log("[Luna] No slot text captured. Using default prompt.");
            prompt = DefaultLunaPrompt;
        }

        if (logAiResponse)
            Debug.Log($"[Luna->proxy] Prompt: {prompt}");

        QueueAiRequest(prompt);
    }

    private string ExtractSlotPrompt(MLVoice.IntentEvent voiceEvent, string slotName)
    {
        if (voiceEvent.EventSlotsUsed == null || voiceEvent.EventSlotsUsed.Count == 0)
        {
            if (verboseVoiceLogging)
                Debug.Log("[VoiceDebug] No slots in this event.");
            return string.Empty;
        }

        foreach (var slot in voiceEvent.EventSlotsUsed)
        {
            if (verboseVoiceLogging)
                Debug.Log($"[VoiceDebug] Checking slot: name='{slot.SlotName}' value='{slot.SlotValue}'");

            if (!string.Equals(slot.SlotName, slotName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(slot.SlotValue))
            {
                Debug.LogWarning($"[VoiceDebug] Slot '{slotName}' found but value is empty.");
                return string.Empty;
            }

            return slot.SlotValue.Trim();
        }

        if (verboseVoiceLogging)
            Debug.Log($"[VoiceDebug] Slot '{slotName}' not found in event slots.");
        return string.Empty;
    }

    private void LogVoiceEvent(MLVoice.IntentEvent voiceEvent, bool wasSuccessful)
    {
        string slotSummary = "none";
        if (voiceEvent.EventSlotsUsed != null && voiceEvent.EventSlotsUsed.Count > 0)
        {
            var slotParts = new List<string>();
            foreach (var slot in voiceEvent.EventSlotsUsed)
                slotParts.Add($"{slot.SlotName}='{slot.SlotValue}'");
            slotSummary = string.Join(", ", slotParts);
        }

        Debug.Log(
            $"[VoiceDebug] success={wasSuccessful} " +
            $"state={voiceEvent.State} " +
            $"noIntentReason={voiceEvent.NoIntentReason} " +
            $"id={voiceEvent.EventID} " +
            $"name='{voiceEvent.EventName}' " +
            $"slots=[{slotSummary}]");
    }

    private void QueueAiRequest(string prompt)
    {
        if (aiRequestCoroutine != null)
        {
            Debug.Log("[Gemma] Cancelling previous in-flight AI request.");
            StopCoroutine(aiRequestCoroutine);
        }

        aiRequestCoroutine = StartCoroutine(SendPromptToAi(prompt));
    }

    private IEnumerator SendPromptToAi(string prompt)
    {
        string generateUrl = ResolveGenerateUrl();
        if (string.IsNullOrWhiteSpace(generateUrl))
        {
            Debug.LogError("[Gemma] Generate URL is not configured.");
                 aiRequestCoroutine = null;
                 yield break;
        }

        var requestBody = new AiGenerateRequest
        {
            model = aiModel,
            prompt = prompt,
            stream = false
        };

        string json = JsonUtility.ToJson(requestBody);
        if (logAiResponse)
            Debug.Log($"[Gemma] Sending request to {generateUrl} model='{aiModel}' prompt='{prompt}'");

        using (var request = new UnityWebRequest(generateUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = AiRequestTimeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[Gemma] Request failed (HTTP {request.responseCode}): {request.error}. " +
                    $"Body: {request.downloadHandler?.text}");
            }
            else
            {
                string rawResponse = request.downloadHandler.text;
                if (logAiResponse)
                    Debug.Log($"[Gemma] Raw response ({rawResponse?.Length ?? 0} chars): {rawResponse}");

                var parsedResponse = JsonUtility.FromJson<AiGenerateResponse>(rawResponse);
                string responseText = string.Empty;

                if (parsedResponse != null && !string.IsNullOrWhiteSpace(parsedResponse.response))
                {
                    responseText = parsedResponse.response.Trim();
                    if (logAiResponse)
                        Debug.Log($"[Gemma] Parsed response: {responseText}");
                }
                else
                {
                    Debug.LogWarning("[Gemma] Response parsed but 'response' field was null or empty.");
                }

                SpeakText(responseText);
            }
        }

        aiRequestCoroutine = null;
    }

    private void InitializeTextToSpeech()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                textToSpeech = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    activity,
                    new TextToSpeechInitListener(this));
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"TTS init failed: {exception.Message}");
        }
#endif
    }

    private void OnTextToSpeechInitialized(int status)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bool ready = false;
        try
        {
            using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
            {
                int successStatus = ttsClass.GetStatic<int>("SUCCESS");
                ready = status == successStatus;
            }

            if (ready && textToSpeech != null)
            {
                using (var localeClass = new AndroidJavaClass("java.util.Locale"))
                {
                    AndroidJavaObject locale = localeClass.GetStatic<AndroidJavaObject>("US");
                    textToSpeech.Call<int>("setLanguage", locale);
                }

                Debug.Log("Text-to-speech initialized successfully.");
            }
            else
            {
                Debug.LogWarning($"Text-to-speech not ready (status={status}).");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"TTS initialization callback error: {e.Message}");
            ready = false;
        }

        textToSpeechReady = ready;
#endif
    }

    private void SpeakText(string text)
    {
        if (!speakAiResponse || string.IsNullOrWhiteSpace(text))
            return;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!textToSpeechReady || textToSpeech == null)
        {
            Debug.LogWarning("Skipping spoken response because TTS is not ready yet.");
            return;
        }

        try
        {
            using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
            {
                int queueFlush = ttsClass.GetStatic<int>("QUEUE_FLUSH");
                textToSpeech.Call<int>("speak", text, queueFlush, null,
                    $"ai-response-{Time.frameCount}");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Failed to speak AI response: {exception.Message}");
        }
#endif
    }

    private void DisposeTextToSpeech()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (textToSpeech == null)
            return;

        try
        {
            textToSpeech.Call("stop");
            textToSpeech.Call("shutdown");
            textToSpeech.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"TTS shutdown failed: {exception.Message}");
        }

        textToSpeech = null;
        textToSpeechReady = false;
#endif
    }

    private class TextToSpeechInitListener : AndroidJavaProxy
    {
        private readonly VoiceIntents owner;

        public TextToSpeechInitListener(VoiceIntents ownerInstance)
            : base("android.speech.tts.TextToSpeech$OnInitListener")
        {
            owner = ownerInstance;
        }

        public void onInit(int status)
        {
            owner.OnTextToSpeechInitialized(status);
        }
    }
}
