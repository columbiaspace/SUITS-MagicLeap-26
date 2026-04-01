using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.MagicLeap;
using Whisper;
using Whisper.Utils;

public class AIAWhisperInputController : MonoBehaviour
{
    private const string ReadyButtonLabel = "Start Recording";
    private const string RecordingButtonLabel = "Stop Recording";
    private const string BusyButtonLabel = "Transcribing...";

    [SerializeField] private VoiceIntents voiceIntents;
    [SerializeField] private Button recordButton;
    [SerializeField] private Text recordButtonText;
    [SerializeField] private Text responseTextBox;
    [SerializeField] private WhisperManager whisperManager;
    [SerializeField] private MicrophoneRecord microphoneRecord;
    [SerializeField] private string whisperModelPath = "Whisper/ggml-tiny.bin";
    [SerializeField] private bool whisperModelPathInStreamingAssets = true;
    [SerializeField] private int maxRecordingLengthSeconds = 30;
    [SerializeField] private int recordingFrequency = 16000;
    [SerializeField] private bool logTranscripts = true;

    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();
    private bool hasRecordPermission;
    private bool pendingStartAfterPermission;
    private bool isRecording;
    private bool isBusy;

    private void Awake()
    {
        permissionCallbacks.OnPermissionGranted += OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied += OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain += OnPermissionDenied;
    }

    private void Start()
    {
        TryResolveReferences();
        EnsureRuntimeComponents();
        ConfigureWhisperManager();
        ConfigureMicrophone();

        if (microphoneRecord != null)
        {
            microphoneRecord.OnRecordStop += HandleRecordingStopped;
        }

        hasRecordPermission = MLPermissions.CheckPermission(MLPermission.RecordAudio).IsOk;
        RefreshButtonVisuals();
    }

    private void OnDestroy()
    {
        permissionCallbacks.OnPermissionGranted -= OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied -= OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain -= OnPermissionDenied;

        if (microphoneRecord != null)
        {
            microphoneRecord.OnRecordStop -= HandleRecordingStopped;
        }
    }

    public void ToggleRecording()
    {
        if (isBusy)
        {
            return;
        }

        if (!EnsureMicrophonePermission())
        {
            return;
        }

        if (isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void TryResolveReferences()
    {
        if (voiceIntents == null)
        {
            GameObject voiceIntentObject = GameObject.Find("VoiceIntent");
            if (voiceIntentObject != null)
            {
                voiceIntents = voiceIntentObject.GetComponent<VoiceIntents>();
            }
        }

        if (recordButton == null)
        {
            GameObject recordButtonObject = GameObject.Find("AIARecordButton");
            if (recordButtonObject != null)
            {
                recordButton = recordButtonObject.GetComponent<Button>();
            }
        }

        if (recordButtonText == null)
        {
            GameObject recordButtonTextObject = GameObject.Find("AIARecordButtonText");
            if (recordButtonTextObject != null)
            {
                recordButtonText = recordButtonTextObject.GetComponent<Text>();
            }
        }

        if (responseTextBox == null)
        {
            GameObject responseTextObject = GameObject.Find("AIAResponseText");
            if (responseTextObject != null)
            {
                responseTextBox = responseTextObject.GetComponent<Text>();
            }
        }
    }

    private void EnsureRuntimeComponents()
    {
        whisperManager ??= gameObject.GetComponent<WhisperManager>();
        if (whisperManager == null)
        {
            whisperManager = gameObject.AddComponent<WhisperManager>();
        }

        microphoneRecord ??= gameObject.GetComponent<MicrophoneRecord>();
        if (microphoneRecord == null)
        {
            microphoneRecord = gameObject.AddComponent<MicrophoneRecord>();
        }
    }

    private void ConfigureWhisperManager()
    {
        if (whisperManager == null)
        {
            return;
        }

        if (!whisperManager.IsLoaded && !whisperManager.IsLoading)
        {
            whisperManager.ModelPath = whisperModelPath;
            whisperManager.IsModelPathInStreamingAssets = whisperModelPathInStreamingAssets;
        }

        whisperManager.language = "en";
        whisperManager.translateToEnglish = false;
        whisperManager.noContext = true;
    }

    private void ConfigureMicrophone()
    {
        if (microphoneRecord == null)
        {
            return;
        }

        microphoneRecord.maxLengthSec = maxRecordingLengthSeconds;
        microphoneRecord.loop = false;
        microphoneRecord.frequency = recordingFrequency;
        microphoneRecord.echo = false;
        microphoneRecord.useVad = false;
        microphoneRecord.vadStop = false;
    }

    private bool EnsureMicrophonePermission()
    {
        if (hasRecordPermission || MLPermissions.CheckPermission(MLPermission.RecordAudio).IsOk)
        {
            hasRecordPermission = true;
            return true;
        }

        pendingStartAfterPermission = true;
        UpdateStatus("Microphone permission required for Whisper recording.");
        MLPermissions.RequestPermission(MLPermission.RecordAudio, permissionCallbacks);
        return false;
    }

    private void OnPermissionGranted(string permission)
    {
        if (permission != MLPermission.RecordAudio)
        {
            return;
        }

        hasRecordPermission = true;

        if (!pendingStartAfterPermission)
        {
            return;
        }

        pendingStartAfterPermission = false;
        StartRecording();
    }

    private void OnPermissionDenied(string permission)
    {
        if (permission != MLPermission.RecordAudio)
        {
            return;
        }

        pendingStartAfterPermission = false;
        UpdateStatus("Microphone permission denied.");
        RefreshButtonVisuals();
    }

    private void StartRecording()
    {
        if (microphoneRecord == null)
        {
            UpdateStatus("Whisper microphone recorder is missing.");
            return;
        }

        try
        {
            microphoneRecord.StartRecord();
            isRecording = true;
            UpdateStatus("Recording your question... Tap again to stop.");
            RefreshButtonVisuals();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Whisper] Failed to start microphone recording: {exception}");
            UpdateStatus("Failed to start microphone recording.");
            isRecording = false;
            RefreshButtonVisuals();
        }
    }

    private void StopRecording()
    {
        if (microphoneRecord == null || !microphoneRecord.IsRecording)
        {
            isRecording = false;
            RefreshButtonVisuals();
            return;
        }

        isRecording = false;
        isBusy = true;
        UpdateStatus("Processing your recording...");
        RefreshButtonVisuals();
        microphoneRecord.StopRecord();
    }

    private async void HandleRecordingStopped(AudioChunk recordedAudio)
    {
        try
        {
            if (recordedAudio.Data == null || recordedAudio.Data.Length == 0)
            {
                UpdateStatus("No audio was captured.");
                return;
            }

            bool whisperReady = await EnsureWhisperModelReady();
            if (!whisperReady)
            {
                return;
            }

            UpdateStatus("Transcribing your question...");
            WhisperResult result = await whisperManager.GetTextAsync(
                recordedAudio.Data,
                recordedAudio.Frequency,
                recordedAudio.Channels);

            string transcript = result?.Result?.Trim();
            if (string.IsNullOrWhiteSpace(transcript))
            {
                Debug.LogWarning("[Whisper] Transcription completed but transcript was empty.");
                UpdateStatus("Whisper could not transcribe that recording.");
                return;
            }

            if (logTranscripts)
            {
                Debug.Log($"[Whisper] Transcript: {transcript}");
            }

            if (voiceIntents == null)
            {
                UpdateStatus(transcript);
                Debug.LogWarning("[Whisper] VoiceIntents reference is missing, so transcript was not forwarded to Luna.");
                return;
            }

            voiceIntents.SubmitPromptFromText(transcript);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Whisper] Failed to transcribe recorded audio: {exception}");
            UpdateStatus("Whisper transcription failed.");
        }
        finally
        {
            isBusy = false;
            RefreshButtonVisuals();
        }
    }

    private async Task<bool> EnsureWhisperModelReady()
    {
        if (whisperManager == null)
        {
            UpdateStatus("Whisper manager is missing.");
            return false;
        }

        if (!whisperManager.IsLoaded)
        {
            UpdateStatus("Loading Whisper model...");
            await whisperManager.InitModel();
        }

        if (whisperManager.IsLoaded)
        {
            return true;
        }

        UpdateStatus("Whisper failed to load. Check the model file.");
        return false;
    }

    private void RefreshButtonVisuals()
    {
        if (recordButton != null)
        {
            recordButton.interactable = !isBusy;
        }

        if (recordButtonText == null)
        {
            return;
        }

        if (isBusy)
        {
            recordButtonText.text = BusyButtonLabel;
        }
        else if (isRecording)
        {
            recordButtonText.text = RecordingButtonLabel;
        }
        else
        {
            recordButtonText.text = ReadyButtonLabel;
        }
    }

    private void UpdateStatus(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (voiceIntents != null)
        {
            voiceIntents.SetResponseStatus(text);
            return;
        }

        if (responseTextBox != null)
        {
            responseTextBox.text = text;
            return;
        }

        Debug.LogWarning($"[Whisper] Could not display status because no AIA text box was found. Status: {text}");
    }
}
