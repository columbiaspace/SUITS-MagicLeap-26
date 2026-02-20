using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.MagicLeap;

public class VoiceIntents : MonoBehaviour
{
    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();   
    public MLVoiceIntentsConfiguration VoiceIntentsConfiguration;
    public GameObject targetObject;
    private bool shouldRotate = false;
    public float rotationSpeed = 50f;
    [Header("AI Generation")]
    [SerializeField] private bool sendVoicePromptToAi = true;
    [SerializeField] private string aiGenerateUrl = "http://10.207.22.21:11434/api/generate";
    [SerializeField] private string aiModel = "gemma3:27b";
    [SerializeField] private bool logAiResponse = true;
    [SerializeField] private bool speakAiResponse = true;
    private Coroutine aiRequestCoroutine;
    private AndroidJavaObject textToSpeech;
    private bool textToSpeechReady;

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

    // request permission for voice input at start
    private void Start()
    {
        MLPermissions.RequestPermission(MLPermission.VoiceInput, permissionCallbacks);
        InitializeTextToSpeech();

    }

    void Update()
    {
        if (shouldRotate && targetObject != null)
        {
            targetObject.transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
        }
    }
    
    // subscribe to permission events
    private void Awake()
    {
        permissionCallbacks.OnPermissionGranted += OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied += OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain += OnPermissionDenied;
    }

    // unsubscribe from permission events
    private void OnDestroy()
    {
        permissionCallbacks.OnPermissionGranted -= OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied -= OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain -= OnPermissionDenied;
        MLVoice.OnVoiceEvent -= MLVoiceOnVoiceEvent;
        DisposeTextToSpeech();
    }


    // on voice permission denied, disable script
    private void OnPermissionDenied(string permission)
    {
        Debug.LogError($"Failed to initialize voice intents due to missing or denied {MLPermission.VoiceInput} permission. Please add to manifest. Disabling script.");
        enabled = false;
    }

    // on voice permission granted, initialize voice input
    private void OnPermissionGranted(string permission)
    {
        if (permission == MLPermission.VoiceInput)
            InitializeVoiceInput();
            
    }


    // check if voice commands setting is enabled, then set up voice intents
    private void InitializeVoiceInput()
    {
        bool isVoiceEnabled = MLVoice.VoiceEnabled;

        // if voice setting is enabled, try to set up voice intents
        if (isVoiceEnabled)
        {
            Debug.Log("Voice commands setting is enabled");
            var result = MLVoice.SetupVoiceIntents(VoiceIntentsConfiguration);
            if (result.IsOk)
            {
                Debug.Log("Voice intents successfully initialized");
                MLVoice.OnVoiceEvent += MLVoiceOnVoiceEvent;
                
            }
            else
            {
                Debug.LogError("Voice could not initialize: " + result);
            }
        }

        // if voice setting is disabled, open voice settings so user can enable it
        else
        {
            Debug.Log("Voice commands setting is disabled - opening settings");
            UnityEngine.XR.MagicLeap.SettingsIntentsLauncher.LaunchSystemVoiceInputSettings();
            Application.Quit();
        }
    }

    // handle voice events
    private void MLVoiceOnVoiceEvent(in bool wasSuccessful, in MLVoice.IntentEvent voiceEvent)
    {
        if (wasSuccessful)
        {
            switch (voiceEvent.EventID)
            {
                case 101:
                    Debug.Log("Show object");
                    targetObject.SetActive(true);
              
                    break;

                case 102:
                    Debug.Log("Hide object");
                    targetObject.SetActive(false);
             
                    break;

                case 103:
                    Debug.Log("Start rotating object");
                    shouldRotate = true;

                    break;

                case 104:
                    Debug.Log("Stop rotating object");
                    shouldRotate = false;
       
                    break;
            }

            if (sendVoicePromptToAi)
            {
                string prompt = ExtractPromptFromVoiceEvent(voiceEvent);
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    QueueAiRequest(prompt);
                }
                else
                {
                    Debug.LogWarning("Voice event succeeded but no prompt text was found to send to AI.");
                }
            }
        }
    }

    private void QueueAiRequest(string prompt)
    {
        if (aiRequestCoroutine != null)
        {
            StopCoroutine(aiRequestCoroutine);
        }

        aiRequestCoroutine = StartCoroutine(SendPromptToAi(prompt));
    }

    private IEnumerator SendPromptToAi(string prompt)
    {
        var requestBody = new AiGenerateRequest
        {
            model = aiModel,
            prompt = prompt,
            stream = false
        };

        string json = JsonUtility.ToJson(requestBody);
        using (var request = new UnityWebRequest(aiGenerateUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"AI generate request failed: {request.error}");
            }
            else
            {
                string rawResponse = request.downloadHandler.text;
                var parsedResponse = JsonUtility.FromJson<AiGenerateResponse>(rawResponse);
                string responseText = string.Empty;
                if (parsedResponse != null && !string.IsNullOrWhiteSpace(parsedResponse.response))
                {
                    responseText = parsedResponse.response.Trim();
                    if (logAiResponse)
                    {
                        Debug.Log($"AI response: {responseText}");
                    }
                }
                else if (logAiResponse)
                {
                    Debug.Log($"AI response (raw): {rawResponse}");
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
        using (var ttsClass = new AndroidJavaClass("android.speech.tts.TextToSpeech"))
        {
            int successStatus = ttsClass.GetStatic<int>("SUCCESS");
            textToSpeechReady = status == successStatus;
        }

        if (!textToSpeechReady || textToSpeech == null)
        {
            Debug.LogWarning("Text-to-speech not ready.");
            return;
        }

        using (var localeClass = new AndroidJavaClass("java.util.Locale"))
        {
            AndroidJavaObject locale = localeClass.GetStatic<AndroidJavaObject>("US");
            textToSpeech.Call<int>("setLanguage", locale);
        }
#endif
    }

    private void SpeakText(string text)
    {
        if (!speakAiResponse || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

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
                textToSpeech.Call<int>("speak", text, queueFlush, null, $"ai-response-{Time.frameCount}");
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

    // Use reflection so this works with different Magic Leap SDK voice event shapes.
    private string ExtractPromptFromVoiceEvent(MLVoice.IntentEvent voiceEvent)
    {
        object boxedVoiceEvent = voiceEvent;

        string prompt = GetStringMemberValue(boxedVoiceEvent, "Text")
                        ?? GetStringMemberValue(boxedVoiceEvent, "Prompt")
                        ?? GetStringMemberValue(boxedVoiceEvent, "Transcription")
                        ?? GetStringMemberValue(boxedVoiceEvent, "Transcript")
                        ?? GetStringMemberValue(boxedVoiceEvent, "Utterance")
                        ?? GetStringMemberValue(boxedVoiceEvent, "Phrase");

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            return prompt.Trim();
        }

        string slotsPrompt = ExtractPromptFromSlots(boxedVoiceEvent);
        if (!string.IsNullOrWhiteSpace(slotsPrompt))
        {
            return slotsPrompt;
        }

        string eventName = GetStringMemberValue(boxedVoiceEvent, "EventName");
        return string.IsNullOrWhiteSpace(eventName) ? string.Empty : eventName.Trim();
    }

    private string ExtractPromptFromSlots(object voiceEventObject)
    {
        object slots = GetMemberValue(voiceEventObject, "EventSlots")
                       ?? GetMemberValue(voiceEventObject, "Slots")
                       ?? GetMemberValue(voiceEventObject, "SlotData");

        if (slots is IEnumerable slotEnumerable)
        {
            var parts = new List<string>();
            foreach (object slot in slotEnumerable)
            {
                if (slot == null)
                {
                    continue;
                }

                string slotValue = GetStringMemberValue(slot, "Value")
                                   ?? GetStringMemberValue(slot, "Text")
                                   ?? GetStringMemberValue(slot, "Data");
                if (!string.IsNullOrWhiteSpace(slotValue))
                {
                    parts.Add(slotValue.Trim());
                }
            }

            if (parts.Count > 0)
            {
                return string.Join(" ", parts);
            }
        }

        return string.Empty;
    }

    private object GetMemberValue(object target, string memberName)
    {
        if (target == null)
        {
            return null;
        }

        Type type = target.GetType();
        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            return field.GetValue(target);
        }

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanRead)
        {
            return property.GetValue(target);
        }

        return null;
    }

    private string GetStringMemberValue(object target, string memberName)
    {
        return GetMemberValue(target, memberName) as string;
    }
}