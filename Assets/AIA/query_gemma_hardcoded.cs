using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit;

[DisallowMultipleComponent]
public class LLMInteractable : MonoBehaviour
{
    [Header("XR (Magic Leap 2 / XR Interaction Toolkit)")]
    [Tooltip("The XRGrabInteractable this script listens to.")]
    [SerializeField] private XRGrabInteractable grabInteractable;

    [Tooltip("Send to LLM when the object is activated (typically controller trigger) while grabbed/selected.")]
    [SerializeField] private bool triggerOnActivatedWhileGrabbed = true;

    [Tooltip("Optional: also send once immediately when the object is grabbed (Select Entered).")]
    [SerializeField] private bool triggerOnGrab = false;

    [Tooltip("If true, ignore additional activates while a request is in-flight.")]
    [SerializeField] private bool blockWhileRequestInFlight = true;

    [Header("Gemma Server (Remote Computer)")]
    [Tooltip("Remote machine IP or hostname, e.g. 10.207.22.21")]
    [SerializeField] private string serverHost = "10.207.22.21";

    [Tooltip("Server port. If your curl is http://10.207.22.21/api/generate with no port, this is likely 80.")]
    [SerializeField] private int serverPort = 11434;

    [Tooltip("Use HTTPS only if your server is configured for TLS.")]
    [SerializeField] private bool useHttps = false;

    [Tooltip("Must match what works in curl (e.g., gemma3:27b).")]
    [SerializeField] private string modelName = "gemma3:27b";

    [Tooltip("Seconds before the HTTP request times out (27B can be slow).")]
    [SerializeField] private int timeoutSeconds = 60;

    [Header("Prompt Input (editable in a single build)")]
    [Tooltip("TMP_InputField in the scene that controls the prompt text.")]
    [SerializeField] private TMP_InputField promptInputField;

    [Tooltip("Persist the prompt so it survives app restarts (PlayerPrefs).")]
    [SerializeField] private bool persistPrompt = true;

    [Tooltip("PlayerPrefs key used to store the prompt text.")]
    [SerializeField] private string promptPrefsKey = "GEMMA_PROMPT_TEXT";

    [Header("UI Output (optional)")]
    [Tooltip("TMP_Text to show status + model output.")]
    [SerializeField] private TMP_Text responseText;

    [Header("Debug Output (optional)")]
    [Tooltip("TMP_Text to show debug statements.")]
    [SerializeField] private TMP_Text debugText;

    [Tooltip("Append responses instead of replacing the text.")]
    [SerializeField] private bool appendResponses = false;

    [Header("Generation Settings (optional)")]
    [Range(0f, 2f)]
    [SerializeField] private float temperature = 0.7f;

    [Tooltip("Maximum tokens to generate (mapped to num_predict).")]
    [SerializeField] private int maxTokens = 256;

    // Internal state
    private bool isSelected = false;
    private bool requestInFlight = false;
    private string currentPrompt = "";

    // ---- Request/Response DTOs (match /api/generate schema) ----
    [Serializable]
    private class Options
    {
        public float temperature;
        public int num_predict;
    }

    [Serializable]
    private class GenerateRequest
    {
        public string model;
        public string prompt;
        public bool stream;
        public Options options;
    }

    // Matches your sample response (we only need 'response', but other fields are harmless)
    [Serializable]
    private class GenerateResponse
    {
        public string model;
        public string created_at;
        public string response;
        public bool done;
        public string done_reason;
    }

    private void Reset()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
    }

    private void Awake()
    {
        Debug.Log("[TEST] App started");
        SetDebugText("[TEST] App started");
        if (grabInteractable == null)
            grabInteractable = GetComponent<XRGrabInteractable>();

        if (grabInteractable == null)
        {
            Debug.LogError("[GrabToGemmaGenerate] Missing XRGrabInteractable. Add one to this GameObject or assign it.");
            enabled = false;
            return;
        }

        // Load persisted prompt, then sync the UI
        if (persistPrompt)
            currentPrompt = PlayerPrefs.GetString(promptPrefsKey, "");

        if (promptInputField != null)
        {
            if (!string.IsNullOrEmpty(currentPrompt))
                promptInputField.text = currentPrompt;
            else
                currentPrompt = promptInputField.text;

            promptInputField.onValueChanged.AddListener(OnPromptChanged);
        }
    }

    private void OnEnable()
    {
        grabInteractable.selectEntered.AddListener(OnSelectEntered);
        grabInteractable.selectExited.AddListener(OnSelectExited);
        grabInteractable.activated.AddListener(OnActivated);
    }

    private void OnDisable()
    {
        grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
        grabInteractable.selectExited.RemoveListener(OnSelectExited);
        grabInteractable.activated.RemoveListener(OnActivated);

        if (promptInputField != null)
            promptInputField.onValueChanged.RemoveListener(OnPromptChanged);
    }

    private void OnPromptChanged(string newValue)
    {
        currentPrompt = newValue ?? "";

        if (persistPrompt)
        {
            PlayerPrefs.SetString(promptPrefsKey, currentPrompt);
            PlayerPrefs.Save();
        }
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        isSelected = true;

        if (triggerOnGrab)
            TrySendPrompt();
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        isSelected = false;
    }

    private void OnActivated(ActivateEventArgs args)
    {
        if (!triggerOnActivatedWhileGrabbed) return;
        if (!isSelected) return; // only when currently grabbed
        TrySendPrompt();
    }

    private void TrySendPrompt()
    {
        if (blockWhileRequestInFlight && requestInFlight)
        {
            SetResponseText("[Gemma] Request already in flight...");
            return;
        }

        string prompt = (promptInputField != null) ? promptInputField.text : currentPrompt;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            SetResponseText("[Gemma] Prompt is empty. Type something into the prompt input field.");
            return;
        }

        StartCoroutine(SendPromptCoroutine(prompt.Trim()));
    }

    private string BaseUrl()
    {
        string scheme = useHttps ? "https" : "http";
        return $"{scheme}://{serverHost}:{serverPort}";
    }

    private string BuildGenerateRequestJson(string model, string prompt)
    {
        var payload = new GenerateRequest
        {
            model = model,
            prompt = prompt,
            stream = false, // important: Unity expects a single JSON response
            options = new Options
            {
                temperature = temperature,
                num_predict = maxTokens
            }
        };

        return JsonUtility.ToJson(payload);
    }

    private string ParseGenerateResponse(string rawJson)
    {
        try
        {
            var parsed = JsonUtility.FromJson<GenerateResponse>(rawJson);
            return parsed != null ? parsed.response : "";
        }
        catch
        {
            return "";
        }
    }

    private IEnumerator SendPromptCoroutine(string prompt)
    {
        Debug.Log("sending prompt");
        SetDebugText("sending prompt");
        requestInFlight = true;
        SetResponseText("[Gemma] Sending prompt...");

        string url = $"{BaseUrl()}/api/generate";
        string jsonBody = BuildGenerateRequestJson(modelName, prompt);
        Debug.Log("DEBUG URL - requestInFlight");
        Debug.Log(url);
        SetDebugText(url);
        SetDebugText(jsonBody);
        SetDebugText("next step");

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            SetDebugText("in sub group");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;
            req.SetRequestHeader("Content-Type", "application/json");

            SetDebugText("before send");

            yield return req.SendWebRequest();

            SetDebugText("after send");

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetDebugText("REQUEST FAILED");
                string serverText = req.downloadHandler != null ? req.downloadHandler.text : "";
                SetResponseText($"[Gemma] Error: {req.error}\nURL: {url}\nServer said: {serverText}");
                requestInFlight = false;
                yield break;
            }

            SetDebugText("REQUETS PASS");
            string raw = req.downloadHandler.text;

            // Parse just the "response" field (matches your sample output)
            string answer = ParseGenerateResponse(raw);

            if (string.IsNullOrEmpty(answer))
                answer = raw; // fallback: show raw JSON if parsing failed

            SetResponseText(answer);
        }

        requestInFlight = false;
    }

    private void SetResponseText(string text)
    {
        Debug.Log(text);

        if (responseText == null) return;

        if (appendResponses)
            responseText.text += (responseText.text.Length > 0 ? "\n\n" : "") + text;
        else
            responseText.text = text;
    }

    private void SetDebugText(string text)
    {
        Debug.Log(text);

        if (debugText == null) return;
        debugText.text = text;
    }
}
