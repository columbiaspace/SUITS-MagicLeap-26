using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR.MagicLeap;

public class VoiceIntents : MonoBehaviour
{
    private const uint AskLunaEventId = 105;
    private const int AiRequestTimeoutSeconds = 30;
    private const string OllamaIpEnvironmentVariable = "LUNA_OLLAMA_IP";

    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();
    public MLVoiceIntentsConfiguration VoiceIntentsConfiguration;
    public GameObject targetObject;
    private bool shouldRotate = false;
    public float rotationSpeed = 50f;

    [Header("AI Generation")]
    [SerializeField] private bool sendVoicePromptToAi = true;
    [SerializeField] private string aiGenerateUrl = "http://10.206.85.24:13853/chat";
    [SerializeField] private string aiModel = "gemma4:26b";
    [SerializeField] private bool logAiResponse = true;
    [SerializeField] private bool speakAiResponse = true;
    [SerializeField] private Text responseTextBox;
    [SerializeField] private AIAVoskInputController aiaInputController;

    [Header("Debugging")]
    [SerializeField] private bool verboseVoiceLogging = true;

    // "Hey Luna" is only a wake phrase. It starts Vosk recording; the
    // resulting transcript is sent to Gemma through SubmitPromptFromText.

    private Coroutine aiRequestCoroutine;
    private AndroidJavaObject textToSpeech;
    private AndroidJavaObject unityActivity;
    private volatile bool textToSpeechReady;
    private bool isVoiceEventSubscribed;

    [Serializable]
    private class AiChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class AiChatRequest
    {
        public AiChatMessage[] messages;
        public bool stream;
    }

    [Serializable]
    private class AiChatResponseMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class AiChatResponse
    {
        public AiChatResponseMessage message;
        public string prompt;
        public string response;
        public bool stream;
    }

    private void Start()
    {
        ApplyOllamaIpOverrideFromEnvironment();
        TryResolveResponseTextBox();
        TryResolveAiaInputController();
        MLPermissions.RequestPermission(MLPermission.VoiceInput, permissionCallbacks);
        InitializeTextToSpeech();
    }

    private void ApplyOllamaIpOverrideFromEnvironment()
    {
        string ollamaIp = Environment.GetEnvironmentVariable(OllamaIpEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(ollamaIp))
        {
            return;
        }

        aiGenerateUrl = $"http://{ollamaIp.Trim()}:13853/chat";
    }

    void Update()
    {
        if (shouldRotate && targetObject != null)
        {
            targetObject.transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
        }
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
                {
                    MLVoice.OnVoiceEvent -= MLVoiceOnVoiceEvent;
                }
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
        {
            LogVoiceEvent(voiceEvent, wasSuccessful);
        }

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
                {
                    targetObject.SetActive(true);
                }
                else
                {
                    Debug.LogWarning("Show command received, but targetObject is not assigned.");
                }
                break;

            case 102:
                Debug.Log("Hide object");
                if (targetObject != null)
                {
                    targetObject.SetActive(false);
                }
                else
                {
                    Debug.LogWarning("Hide command received, but targetObject is not assigned.");
                }
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
                Debug.Log("Hey Luna wake phrase detected");
                StartAiaRecordingFromWakePhrase();
                break;

            default:
                Debug.Log($"Unhandled voice intent event id: {voiceEvent.EventID}");
                break;
        }
    }

    private void StartAiaRecordingFromWakePhrase()
    {
        TryResolveAiaInputController();
        if (aiaInputController == null)
        {
            Debug.LogWarning("[Luna] Hey Luna detected, but AIA input controller was not found.");
            UpdateResponseTextBox("Luna voice recording is unavailable.");
            return;
        }

        aiaInputController.StartRecordingFromVoiceIntent();
    }

    private void TryResolveResponseTextBox()
    {
        if (responseTextBox != null)
        {
            return;
        }

        GameObject responseTextObject = GameObject.Find("AIAResponseText");
        if (responseTextObject == null)
        {
            return;
        }

        responseTextBox = responseTextObject.GetComponent<Text>();
    }

    private void TryResolveAiaInputController()
    {
        if (aiaInputController != null)
        {
            return;
        }

        aiaInputController = FindObjectOfType<AIAVoskInputController>();
    }

    private void UpdateResponseTextBox(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        TryResolveResponseTextBox();
        if (responseTextBox == null)
        {
            Debug.LogWarning("[Luna] Could not find AIAResponseText to update.");
            return;
        }

        responseTextBox.text = text;
    }

    public void SetResponseStatus(string text)
    {
        UpdateResponseTextBox(text);
    }

    public void SubmitPromptFromText(string prompt)
    {
        if (!sendVoicePromptToAi)
        {
            Debug.LogWarning("Text prompt received but AI forwarding is disabled.");
            UpdateResponseTextBox("Luna AI forwarding is disabled.");
            return;
        }

        string trimmedPrompt = prompt?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedPrompt))
        {
            Debug.LogWarning("[Luna] Ignoring empty text prompt.");
            UpdateResponseTextBox("Luna could not hear a question.");
            return;
        }

        if (logAiResponse)
        {
            Debug.Log($"[Vosk->Gemma] Prompt: {trimmedPrompt}");
        }

        UpdateResponseTextBox("Luna heard your question.");
        QueueAiRequest(trimmedPrompt);
    }

    private void LogVoiceEvent(MLVoice.IntentEvent voiceEvent, bool wasSuccessful)
    {
        string slotSummary = "none";
        if (voiceEvent.EventSlotsUsed != null && voiceEvent.EventSlotsUsed.Count > 0)
        {
            var slotParts = new List<string>();
            foreach (var slot in voiceEvent.EventSlotsUsed)
            {
                slotParts.Add($"{slot.SlotName}='{slot.SlotValue}'");
            }
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
        var requestBody = new AiChatRequest
        {
            messages = new[]
            {
                new AiChatMessage
                {
                    role = "user",
                    content = prompt
                }
            },
            stream = false
        };

        string json = JsonUtility.ToJson(requestBody);
        if (logAiResponse)
        {
            Debug.Log($"[Gemma] Sending request to {aiGenerateUrl} " +
                      $"messages=1 prompt='{prompt}'");
        }
        UpdateResponseTextBox("Sending request to Luna...");

        using (var request = new UnityWebRequest(aiGenerateUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = AiRequestTimeoutSeconds;

            UpdateResponseTextBox("Waiting for Luna response...");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[Gemma] Request failed (HTTP {request.responseCode}): {request.error}. " +
                    $"Body: {request.downloadHandler?.text}");
                UpdateResponseTextBox("Luna request failed.");
            }
            else
            {
                string rawResponse = request.downloadHandler.text;
                if (logAiResponse)
                {
                    Debug.Log($"[Gemma] Raw response ({rawResponse?.Length ?? 0} chars): {rawResponse}");
                }

                string responseText = ExtractAssistantResponseText(rawResponse);

                if (string.IsNullOrWhiteSpace(responseText))
                {
                    Debug.LogWarning("[Gemma] Response parsed but no assistant text field was found.");
                    UpdateResponseTextBox("Luna returned an empty response.");
                }
                else
                {
                    UpdateResponseTextBox(responseText);
                }

                SpeakText(responseText);
            }
        }

        aiRequestCoroutine = null;
    }

    private string ExtractAssistantResponseText(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return string.Empty;
        }

        try
        {
            JSONNode root = JSONNode.Parse(rawResponse);

            string responseText = GetJsonString(root["message"]?["content"]);
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                LogParsedResponse("message.content", responseText);
                return responseText;
            }

            responseText = GetJsonString(root["response"]);
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                LogParsedResponse("response", responseText);
                return responseText;
            }

            responseText = GetJsonString(root["content"]);
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                LogParsedResponse("content", responseText);
                return responseText;
            }

            JSONNode choices = root["choices"];
            if (choices != null && choices.Count > 0)
            {
                responseText = GetJsonString(choices[0]?["message"]?["content"]);
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    LogParsedResponse("choices[0].message.content", responseText);
                    return responseText;
                }

                responseText = GetJsonString(choices[0]?["text"]);
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    LogParsedResponse("choices[0].text", responseText);
                    return responseText;
                }
            }

            JSONNode messages = root["messages"];
            if (messages != null && messages.Count > 0)
            {
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    string role = GetJsonString(messages[i]?["role"]);
                    string content = GetJsonString(messages[i]?["content"]);
                    if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(content))
                    {
                        LogParsedResponse($"messages[{i}].content", content);
                        return content;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Gemma] Failed to parse JSON response: {exception.Message}");
        }

        string trimmedResponse = rawResponse.Trim();
        if (!trimmedResponse.StartsWith("{", StringComparison.Ordinal) &&
            !trimmedResponse.StartsWith("[", StringComparison.Ordinal))
        {
            LogParsedResponse("raw-text", trimmedResponse);
            return trimmedResponse;
        }

        return string.Empty;
    }

    private static string GetJsonString(JSONNode node)
    {
        if (node == null || node.IsNull)
        {
            return string.Empty;
        }

        return node.Value?.Trim() ?? string.Empty;
    }

    private void LogParsedResponse(string sourcePath, string responseText)
    {
        if (!logAiResponse)
        {
            return;
        }

        Debug.Log($"[Gemma] Parsed response from {sourcePath}: {responseText}");
    }

    private void InitializeTextToSpeech()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                textToSpeech = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    unityActivity,
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
                using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
                {
                    AndroidJavaObject locale = localeClass.GetStatic<AndroidJavaObject>("US");
                    int languageResult = textToSpeech.Call<int>("setLanguage", locale);
                    int langMissingData = ttsClass.GetStatic<int>("LANG_MISSING_DATA");
                    int langNotSupported = ttsClass.GetStatic<int>("LANG_NOT_SUPPORTED");

                    Debug.Log($"Text-to-speech language result={languageResult}.");
                    if (languageResult == langMissingData || languageResult == langNotSupported)
                    {
                        Debug.LogWarning("Text-to-speech US locale is missing or not supported on device.");
                    }
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
        if (!speakAiResponse)
        {
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        string normalizedText = NormalizeTextForSpeech(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            Debug.LogWarning("Skipping spoken response because normalized TTS text is empty.");
            return;
        }

        if (!textToSpeechReady || textToSpeech == null)
        {
            Debug.LogWarning("Skipping spoken response because TTS is not ready yet.");
            return;
        }

        try
        {
            Debug.Log($"[TTS] Speaking {normalizedText.Length} characters: '{TruncateForLog(normalizedText, 160)}'");

            Action speakAction = () =>
            {
                try
                {
                    using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
                    {
                        int queueFlush = ttsClass.GetStatic<int>("QUEUE_FLUSH");
                        int errorCode = ttsClass.GetStatic<int>("ERROR");
                        int speakResult = textToSpeech.Call<int>(
                            "speak",
                            normalizedText,
                            queueFlush,
                            null,
                            $"ai-response-{Time.frameCount}");

                        Debug.Log($"[TTS] speak() result={speakResult}");
                        if (speakResult == errorCode)
                        {
                            Debug.LogWarning("[TTS] Android TextToSpeech returned ERROR from speak().");
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Failed to speak AI response on UI thread: {exception.Message}");
                }
            };

            if (unityActivity != null)
            {
                unityActivity.Call("runOnUiThread", new AndroidJavaRunnable(() => speakAction()));
            }
            else
            {
                Debug.LogWarning("[TTS] Unity activity reference was null. Speaking off UI thread.");
                speakAction();
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
        {
            return;
        }

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
        unityActivity = null;
        textToSpeechReady = false;
#endif
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

    private static string TruncateForLog(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength) + "...";
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
