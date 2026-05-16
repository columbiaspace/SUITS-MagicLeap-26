using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR.MagicLeap;

public class VoiceIntents : MonoBehaviour
{
    public static VoiceIntents Instance { get; private set; }

    private const uint AskLunaEventId = 105;
    // MLVoice intent IDs that route directly to LTV-repair (bypasses Vosk).
    // Configured in Assets/AIA/MLVoiceIntentsConfiguration.asset.
    private const uint LtvRepairEventIdMin = 106;
    private const uint LtvRepairEventIdMax = 108;
    // MLVoice intent IDs that route to generic scene transitions via SceneVoiceCoordinator.
    // Configured in Assets/AIA/MLVoiceIntentsConfiguration.asset.
    private const uint SceneTransitionEventIdMin = 109;
    private const uint SceneTransitionEventIdMax = 113;
    // Bumped from 30s: gemma4:26b on Apple Silicon regularly takes 60-90s for first-token
    // latency on a cold model load (and 30-60s per generation while warm), so 30s caused
    // every Luna call to fail with "Luna request failed" before the orchestrator finished.
    // If the orchestrator is reachable but slow, the user now waits up to this many seconds.
    private const int AiRequestTimeoutSeconds = 180;
    private const string OllamaIpEnvironmentVariable = "LUNA_OLLAMA_IP";
    // Optional full-URL override. Takes precedence over LUNA_OLLAMA_IP and lets the
    // orchestrator live on a non-default port / path (e.g. behind a reverse proxy).
    private const string OllamaUrlEnvironmentVariable = "LUNA_OLLAMA_URL";
    private const int MaxVisibleConversationTurns = 3;
    private const string DefaultResponsePlaceholder = "Luna response will appear here.";
    private const string RecordingPlaceholder = "Recording your question...";
    private const string WaitingForResponsePlaceholder = "waiting for response...";

    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();
    public MLVoiceIntentsConfiguration VoiceIntentsConfiguration;
    public GameObject targetObject;
    private bool shouldRotate = false;
    public float rotationSpeed = 50f;

    [Header("AI Generation")]
    [SerializeField] private bool sendVoicePromptToAi = true;
    [SerializeField] private string aiGenerateUrl = "http://192.168.1.210:13853/chat";
    [SerializeField] private string aiModel = "gemma4:26b";
    [SerializeField] private bool logAiResponse = true;
    [SerializeField] private bool speakAiResponse = true;
    [SerializeField] private Text responseTextBox;
    [SerializeField] private AIAVoskInputController aiaInputController;
    [SerializeField] private ScrollRect responseScrollRect;

    [Header("Voice command routing")]
    [Tooltip("Optional LTV-repair voice coordinator. If present and the transcript matches its " +
             "trigger phrases, the prompt is consumed locally instead of being forwarded to Gemma.")]
    [SerializeField] private LtvVoiceCoordinator ltvVoiceCoordinator;

    [Tooltip("Optional generic scene-transition voice coordinator (persistent singleton). " +
             "Handles all voice transitions other than the Mission→LTV entry, which stays " +
             "on LtvVoiceCoordinator so LtvSceneBootstrapper still sees PendingVoiceTrigger.")]
    [SerializeField] private SceneVoiceCoordinator sceneVoiceCoordinator;

    [Header("Debugging")]
    [SerializeField] private bool verboseVoiceLogging = true;

    // "Hey Luna" is only a wake phrase. It starts Vosk recording; the
    // resulting transcript is sent to Gemma through SubmitPromptFromText.

    private Coroutine aiRequestCoroutine;
    private AndroidJavaObject textToSpeech;
    private AndroidJavaObject unityActivity;
    private volatile bool textToSpeechReady;
    private bool isVoiceEventSubscribed;
    private readonly List<ConversationTurn> completedConversationTurns = new List<ConversationTurn>();
    private ConversationTurn activeConversationTurn;
    private string transientStatus = DefaultResponsePlaceholder;

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

    private class ConversationTurn
    {
        public string userText;
        public string lunaText;
        public bool isLiveTranscript;
        public bool isWaitingForResponse;
    }

    private void Start()
    {
        ApplyOllamaIpOverrideFromEnvironment();
        TryResolveResponseTextBox();
        TryResolveResponseScrollRect();
        TryResolveAiaInputController();
        MLPermissions.RequestPermission(MLPermission.VoiceInput, permissionCallbacks);

        // Prefer LunaTtsBridge (persistent, scene-spanning) when one exists in the scene.
        // Only initialize the inline Android TTS if no bridge is wired up — this avoids
        // two TextToSpeech engines competing for the audio service on ML2.
        if (FindObjectOfType<LunaTtsBridge>() == null)
        {
            InitializeTextToSpeech();
        }
        else
        {
            Debug.Log("[Luna] LunaTtsBridge present; routing TTS through it instead of inline engine.");
        }

        if (responseTextBox != null && string.IsNullOrWhiteSpace(responseTextBox.text))
        {
            UpdateResponseTextBox(DefaultResponsePlaceholder);
        }
    }

    private void ApplyOllamaIpOverrideFromEnvironment()
    {
        // LUNA_OLLAMA_URL wins outright — lets the user point at any host:port/path
        // (e.g. behind a reverse proxy). LUNA_OLLAMA_IP is the simpler legacy override:
        // it only swaps the host portion of the existing URL, preserving port and path.
        string urlOverride = Environment.GetEnvironmentVariable(OllamaUrlEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(urlOverride))
        {
            string trimmed = urlOverride.Trim();
            if (!string.Equals(trimmed, aiGenerateUrl, StringComparison.Ordinal))
            {
                Debug.Log($"[Luna] URL override from {OllamaUrlEnvironmentVariable}: '{aiGenerateUrl}' -> '{trimmed}'.");
                aiGenerateUrl = trimmed;
            }
        }
        else
        {
            string ollamaIp = Environment.GetEnvironmentVariable(OllamaIpEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(ollamaIp))
            {
                string newUrl = $"http://{ollamaIp.Trim()}:13853/chat";
                if (!string.Equals(newUrl, aiGenerateUrl, StringComparison.Ordinal))
                {
                    Debug.Log($"[Luna] IP override from {OllamaIpEnvironmentVariable}: '{aiGenerateUrl}' -> '{newUrl}'.");
                    aiGenerateUrl = newUrl;
                }
            }
        }

        Debug.Log($"[Luna] AI endpoint = {aiGenerateUrl} (timeout {AiRequestTimeoutSeconds}s, model '{aiModel}').");
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
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        permissionCallbacks.OnPermissionGranted += OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied += OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain += OnPermissionDenied;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

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
                if (voiceEvent.EventID >= LtvRepairEventIdMin && voiceEvent.EventID <= LtvRepairEventIdMax)
                {
                    Debug.Log($"[Luna] LTV-repair MLVoice intent fired (id={voiceEvent.EventID}, name='{voiceEvent.EventName}')");
                    string spokenPhrase = string.IsNullOrWhiteSpace(voiceEvent.EventName)
                        ? "ltv repair"
                        : voiceEvent.EventName;
                    if (!TryRouteSceneVoiceCommand(spokenPhrase))
                    {
                        Debug.LogWarning("[Luna] LTV-repair MLVoice intent matched but coordinator did not accept it.");
                    }
                }
                else if (voiceEvent.EventID >= SceneTransitionEventIdMin && voiceEvent.EventID <= SceneTransitionEventIdMax)
                {
                    Debug.Log($"[Luna] Scene-transition MLVoice intent fired (id={voiceEvent.EventID}, name='{voiceEvent.EventName}')");
                    string spokenPhrase = string.IsNullOrWhiteSpace(voiceEvent.EventName)
                        ? string.Empty
                        : voiceEvent.EventName;
                    if (!TryRouteSceneVoiceCommand(spokenPhrase))
                    {
                        Debug.LogWarning($"[Luna] Scene-transition MLVoice intent '{voiceEvent.EventName}' did not match any configured transition in the current scene.");
                    }
                }
                else
                {
                    Debug.Log($"Unhandled voice intent event id: {voiceEvent.EventID}");
                }
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

    private void TryResolveResponseScrollRect()
    {
        if (responseScrollRect != null)
        {
            return;
        }

        GameObject responsePanelObject = GameObject.Find("AIAResponsePanel");
        if (responsePanelObject == null)
        {
            return;
        }

        responseScrollRect = responsePanelObject.GetComponent<ScrollRect>();
    }

    private void TryResolveAiaInputController()
    {
        if (aiaInputController != null)
        {
            return;
        }

        aiaInputController = FindObjectOfType<AIAVoskInputController>();
    }

    /// <summary>
    /// Public so AIAVoskInputController can fire scene-transition routing on partial
    /// Vosk transcripts (without waiting for the recording to stop and a final result
    /// to emit). Tries the generic <see cref="SceneVoiceCoordinator"/> first (any
    /// scene-to-scene transition), then falls back to <see cref="LtvVoiceCoordinator"/>
    /// (kept for the Mission→LTV entry because LtvSceneBootstrapper consumes its
    /// PendingVoiceTrigger flag). Returns true when a transition fired.
    /// </summary>
    public bool TryRouteSceneVoiceCommand(string trimmedPrompt)
    {
        if (sceneVoiceCoordinator == null)
        {
            sceneVoiceCoordinator = SceneVoiceCoordinator.Instance != null
                ? SceneVoiceCoordinator.Instance
                : FindObjectOfType<SceneVoiceCoordinator>();
        }

        if (sceneVoiceCoordinator != null && sceneVoiceCoordinator.TryHandleVoiceCommand(trimmedPrompt))
        {
            return true;
        }

        if (ltvVoiceCoordinator == null)
        {
            ltvVoiceCoordinator = FindObjectOfType<LtvVoiceCoordinator>();
        }

        return ltvVoiceCoordinator != null && ltvVoiceCoordinator.TryHandleVoiceCommand(trimmedPrompt);
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
        ScrollResponseTextToBottom();
    }

    public void SetResponseStatus(string text)
    {
        transientStatus = string.IsNullOrWhiteSpace(text) ? DefaultResponsePlaceholder : text.Trim();

        if (activeConversationTurn == null && completedConversationTurns.Count == 0)
        {
            UpdateResponseTextBox(transientStatus);
        }
        else
        {
            RenderConversation();
        }
    }

    public void BeginRecordingTranscript()
    {
        PrepareActiveConversationTurn();
        activeConversationTurn.userText = RecordingPlaceholder;
        activeConversationTurn.isLiveTranscript = true;
        activeConversationTurn.isWaitingForResponse = false;
        activeConversationTurn.lunaText = string.Empty;
        transientStatus = DefaultResponsePlaceholder;
        RenderConversation();
    }

    public void UpdateRecordingTranscript(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return;
        }

        PrepareActiveConversationTurn();
        activeConversationTurn.userText = transcript.Trim();
        activeConversationTurn.isLiveTranscript = true;
        activeConversationTurn.isWaitingForResponse = false;
        RenderConversation();
    }

    public void FailActiveRecording(string message)
    {
        activeConversationTurn = null;
        SetResponseStatus(message);
    }

    public void ShowRecordingProcessing()
    {
        if (activeConversationTurn == null)
        {
            return;
        }

        RenderConversation();
    }

    public void SubmitPromptFromText(string prompt)
    {
        if (!sendVoicePromptToAi)
        {
            Debug.LogWarning("Text prompt received but AI forwarding is disabled.");
            CompleteActiveConversation("Luna AI forwarding is disabled.");
            return;
        }

        string trimmedPrompt = prompt?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedPrompt))
        {
            Debug.LogWarning("[Luna] Ignoring empty text prompt.");
            FailActiveRecording("Luna could not hear a question.");
            return;
        }

        if (TryRouteSceneVoiceCommand(trimmedPrompt))
        {
            // A scene-transition coordinator consumed the prompt and queued a LoadScene.
            // We intentionally do NOT forward this transcript to Gemma.
            CompleteActiveConversation("Loading next scene.");
            return;
        }

        if (logAiResponse)
        {
            Debug.Log($"[Vosk->Gemma] Prompt: {trimmedPrompt}");
        }

        PrepareActiveConversationTurn();
        activeConversationTurn.userText = trimmedPrompt;
        activeConversationTurn.isLiveTranscript = false;
        activeConversationTurn.isWaitingForResponse = true;
        activeConversationTurn.lunaText = WaitingForResponsePlaceholder;
        transientStatus = DefaultResponsePlaceholder;
        RenderConversation();
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
        using (var request = new UnityWebRequest(aiGenerateUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = AiRequestTimeoutSeconds;

            SetActiveConversationWaitingState();
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                bool wasTimeout = request.result == UnityWebRequest.Result.ConnectionError
                                  && (string.IsNullOrEmpty(request.error)
                                      || request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                                      || request.error.IndexOf("aborted", StringComparison.OrdinalIgnoreCase) >= 0);
                Debug.LogError(
                    $"[Gemma] Request failed (HTTP {request.responseCode}, result={request.result}): {request.error}. " +
                    $"URL: {aiGenerateUrl}. Body: {request.downloadHandler?.text}");

                string userMessage;
                if (wasTimeout)
                {
                    userMessage = $"Luna timed out after {AiRequestTimeoutSeconds}s. The model is slow or unreachable at {aiGenerateUrl}.";
                }
                else if (request.responseCode == 0)
                {
                    userMessage = $"Luna unreachable at {aiGenerateUrl} — check the orchestrator is running and on the same network.";
                }
                else
                {
                    userMessage = $"Luna request failed (HTTP {request.responseCode}).";
                }
                CompleteActiveConversation(userMessage);
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
                    CompleteActiveConversation("Luna returned an empty response.");
                }
                else
                {
                    CompleteActiveConversation(responseText);
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

        // Prefer the persistent bridge — it survives scene transitions and is the only TTS
        // we initialize when present. Inline path below only runs as a fallback.
        if (LunaTtsBridge.Instance != null)
        {
            LunaTtsBridge.Instance.Speak(text);
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

    private void PrepareActiveConversationTurn()
    {
        if (activeConversationTurn != null)
        {
            return;
        }

        while (completedConversationTurns.Count >= MaxVisibleConversationTurns)
        {
            completedConversationTurns.RemoveAt(0);
        }

        if (completedConversationTurns.Count == MaxVisibleConversationTurns - 1)
        {
            // Leave room for the in-progress turn.
        }
        else if (completedConversationTurns.Count > MaxVisibleConversationTurns - 1)
        {
            completedConversationTurns.RemoveRange(0, completedConversationTurns.Count - (MaxVisibleConversationTurns - 1));
        }

        activeConversationTurn = new ConversationTurn();
    }

    private void SetActiveConversationWaitingState()
    {
        if (activeConversationTurn == null)
        {
            return;
        }

        activeConversationTurn.isLiveTranscript = false;
        activeConversationTurn.isWaitingForResponse = true;
        activeConversationTurn.lunaText = WaitingForResponsePlaceholder;
        RenderConversation();
    }

    private void CompleteActiveConversation(string lunaText)
    {
        PrepareActiveConversationTurn();
        activeConversationTurn.isLiveTranscript = false;
        activeConversationTurn.isWaitingForResponse = false;
        activeConversationTurn.lunaText = string.IsNullOrWhiteSpace(lunaText) ? "Luna returned an empty response." : lunaText.Trim();

        completedConversationTurns.Add(activeConversationTurn);
        while (completedConversationTurns.Count > MaxVisibleConversationTurns)
        {
            completedConversationTurns.RemoveAt(0);
        }

        activeConversationTurn = null;
        transientStatus = DefaultResponsePlaceholder;
        RenderConversation();
    }

    private void RenderConversation()
    {
        var turnsToRender = new List<ConversationTurn>();
        if (completedConversationTurns.Count > 0)
        {
            turnsToRender.AddRange(completedConversationTurns.Skip(Math.Max(0, completedConversationTurns.Count - MaxVisibleConversationTurns)));
        }

        if (activeConversationTurn != null)
        {
            while (turnsToRender.Count >= MaxVisibleConversationTurns)
            {
                turnsToRender.RemoveAt(0);
            }

            turnsToRender.Add(activeConversationTurn);
        }

        if (turnsToRender.Count == 0)
        {
            UpdateResponseTextBox(string.IsNullOrWhiteSpace(transientStatus) ? DefaultResponsePlaceholder : transientStatus);
            return;
        }

        var builder = new StringBuilder();
        for (int i = 0; i < turnsToRender.Count; i++)
        {
            ConversationTurn turn = turnsToRender[i];
            string userText = string.IsNullOrWhiteSpace(turn.userText) ? RecordingPlaceholder : turn.userText.Trim();

            builder.Append("You: ");
            builder.Append(userText);

            if (!turn.isLiveTranscript)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append("Luna: ");
                builder.Append(turn.isWaitingForResponse
                    ? WaitingForResponsePlaceholder
                    : (string.IsNullOrWhiteSpace(turn.lunaText) ? "Luna returned an empty response." : turn.lunaText.Trim()));
            }

            if (i < turnsToRender.Count - 1)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine();
            }
        }

        UpdateResponseTextBox(builder.ToString());
    }

    private void ScrollResponseTextToBottom()
    {
        TryResolveResponseScrollRect();
        if (responseScrollRect == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        responseScrollRect.verticalNormalizedPosition = 0f;
        if (responseScrollRect.content != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(responseScrollRect.content);
        }
        Canvas.ForceUpdateCanvases();
        responseScrollRect.verticalNormalizedPosition = 0f;
    }
}
