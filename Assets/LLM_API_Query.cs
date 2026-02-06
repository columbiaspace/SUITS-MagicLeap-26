using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;

public class LLMInteractable : MonoBehaviour
{
    [Header("LLM Settings")]
    [SerializeField] private string apiKey = "API_KEY"; // hardcoded for POC
    [TextArea]
    [SerializeField] private string prompt = "What is your name?";

    [Header("UI")]
    [SerializeField] private TMP_Text responseText;

    private XRBaseInteractable interactable;

    // Gemini API endpoint
    // private const string baseUrl =
    //     "https://generativelanguage.googleapis.com/v1beta/models/gemini-pro:generateContent";
    private const string baseUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent";

    #region Gemini DTOs
    [System.Serializable]
    private class GeminiRequest
    {
        public Content[] contents;
    }

    [System.Serializable]
    private class Content
    {
        public Part[] parts;
    }

    [System.Serializable]
    private class Part
    {
        public string text;
    }

    [System.Serializable]
    private class GeminiResponse
    {
        public Candidate[] candidates;
    }

    [System.Serializable]
    private class Candidate
    {
        public Content content;
    }
    #endregion

    private void Start()
    {
        Debug.Log("[LLM] Start() called");
        // Debug.Log("[LLM] Testing log output from editor");
        StartCoroutine(TestNetwork());
        // Debug.Log("[LLM] After StartCoRoutine");
        // StartCoroutine(SendPromptToGemini(prompt));
    }

    private void Awake()
    {
        // Grab the interactable on the SAME GameObject (your cube)
        interactable = GetComponent<XRBaseInteractable>();
        if (interactable == null)
        {
            Debug.LogError("LLMInteractable: No XRBaseInteractable found on this GameObject. " +
                           "Add XRGrabInteractable or XRSimpleInteractable.");
        }
    }

    private void OnEnable()
    {
        if (interactable != null)
            interactable.selectEntered.AddListener(OnSelected);
    }

    private void OnDisable()
    {
        if (interactable != null)
            interactable.selectEntered.RemoveListener(OnSelected);
    }

    private void OnSelected(SelectEnterEventArgs args)
    {
        StartCoroutine(SendPromptToGemini(prompt));
    }

    private IEnumerator SendPromptToGemini(string userMessage)
    {
        string urlTEST = $"{baseUrl}?key={apiKey}";
        Debug.Log("Full LLM URL: " + urlTEST);

        if (responseText != null)
            responseText.text = "Sending prompt to LLM...";

        var requestData = new GeminiRequest
        {
            contents = new[]
            {
                new Content
                {
                    parts = new[]
                    {
                        new Part { text = userMessage }
                    }
                }
            }
        };

        string jsonBody = JsonUtility.ToJson(requestData);
        Debug.Log("Gemini Request JSON: " + jsonBody);

        string url = $"{baseUrl}?key={apiKey}";

        Debug.Log("[LLM] Full URL: " + url);
        Debug.Log("[LLM] Request JSON: " + jsonBody);

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            Debug.Log("[LLM] Result: " + www.result);
            Debug.Log("[LLM] Error: " + www.error);
            Debug.Log("[LLM] Response: " + www.downloadHandler.text);

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("LLM request failed: " + www.error);
                if (responseText != null)
                    responseText.text = "Error: " + www.error;
            }
            else
            {
                string responseJson = www.downloadHandler.text;
                Debug.Log("Gemini Response: " + responseJson);

                try
                {
                    GeminiResponse geminiResponse = JsonUtility.FromJson<GeminiResponse>(responseJson);
                    string textResponse = ExtractTextFromGeminiResponse(geminiResponse);

                    if (responseText != null)
                        responseText.text = textResponse;
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Failed to parse Gemini response: " + e);
                    if (responseText != null)
                        responseText.text = "Failed to parse response.";
                }
            }
        }
    }

    private string ExtractTextFromGeminiResponse(GeminiResponse response)
    {
        if (response == null || response.candidates == null || response.candidates.Length == 0)
            return "No response from LLM.";

        var candidate = response.candidates[0];
        if (candidate.content == null || candidate.content.parts == null || candidate.content.parts.Length == 0)
            return "Empty content from LLM.";

        return candidate.content.parts[0].text;
    }

    private IEnumerator TestNetwork()
    {
        Debug.Log("[LLM] Starting network test...");
        using (UnityWebRequest www = UnityWebRequest.Get("https://www.google.com"))
        {
            yield return www.SendWebRequest();
            Debug.Log("[LLM] Network test result: " + www.result);
            Debug.Log("[LLM] Network test error: " + www.error);
            Debug.Log("[LLM] Network test response length: " + (www.downloadHandler?.text?.Length ?? 0));
        }
    }
}
