using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.MagicLeap;

public class AIAVoskInputController : MonoBehaviour
{
    private const string ReadyButtonLabel = "Start Recording";
    private const string RecordingButtonLabel = "Stop Recording";
    private const string BusyButtonLabel = "Loading Vosk...";
    private const string DefaultVoskModelPath = "vosk-model-en-us-0.22-lgraph.zip";
    private static readonly string[] NasaMissionKeyPhrases =
    {
        "ingress",
        "egress",
        "suits",
        "s u i t s",
        "spacesuit user interface technologies for students",
        "ehp",
        "e h p",
        "extravehicular activity and human surface mobility program",
        "tss",
        "t s s",
        "telemetry stream server",
        "pr",
        "p r",
        "pressurized rover",
        "rover",
        "eva",
        "e v a",
        "extravehicular activity",
        "ev",
        "e v",
        "astronaut",
        "rssi",
        "r s s i",
        "received signal strength indicator",
        "hmd",
        "h m d",
        "hmds",
        "h m d s",
        "head mounted display",
        "head mounted displays",
        "ltv",
        "l t v",
        "lunar terrain vehicle",
        "aia",
        "a i a",
        "artificial intelligence assistant",
        "uia",
        "u i a",
        "umbilical interface assembly",
        "emu",
        "e m u",
        "extravehicular mobility unit",
        "imu",
        "i m u",
        "inertial measurement unit",
        "dcu",
        "d c u",
        "display and control unit",
        "erm",
        "e r m",
        "exit recovery mode",
        "poi",
        "p o i",
        "point of interest",
        "dust",
        "digital lunar exploration sites unreal simulation tool",
        "nav",
        "navigation",
        "a nav",
        "autonomous navigation",
        "a sits",
        "a s i t s",
        "autonomous systems indicators toggle switch",
        "r t h",
        "return to home",
        "aca",
        "a c a",
        "autonomy confidence adjustment",
        "pri",
        "primary",
        "sec",
        "secondary",
        "ril",
        "r i l",
        "reaction indicator light",
        "pops",
        "p o p s",
        "power override panel for subsystems"
    };

    private static readonly string[,] TranscriptNormalizations =
    {
        { "s u i t s", "SUITS" },
        { "suits", "SUITS" },
        { "e h p", "EHP" },
        { "ehp", "EHP" },
        { "t s s", "TSS" },
        { "tss", "TSS" },
        { "p r", "PR" },
        { "pr", "PR" },
        { "e v a", "EVA" },
        { "eva", "EVA" },
        { "e v", "EV" },
        { "ev", "EV" },
        { "r s s i", "RSSI" },
        { "rssi", "RSSI" },
        { "h m d s", "HMDs" },
        { "hmds", "HMDs" },
        { "h m d", "HMD" },
        { "hmd", "HMD" },
        { "l t v", "LTV" },
        { "ltv", "LTV" },
        { "a i a", "AIA" },
        { "aia", "AIA" },
        { "u i a", "UIA" },
        { "uia", "UIA" },
        { "e m u", "EMU" },
        { "emu", "EMU" },
        { "i m u", "IMU" },
        { "imu", "IMU" },
        { "d c u", "DCU" },
        { "dcu", "DCU" },
        { "e r m", "ERM" },
        { "erm", "ERM" },
        { "p o i", "POI" },
        { "poi", "POI" },
        { "dust", "DUST" },
        { "a nav", "ANAV" },
        { "anav", "ANAV" },
        { "a s i t s", "ASITS" },
        { "a sits", "ASITS" },
        { "asits", "ASITS" },
        { "r t h", "RTH" },
        { "rth", "RTH" },
        { "a c a", "ACA" },
        { "aca", "ACA" },
        { "pri", "PRI" },
        { "sec", "SEC" },
        { "r i l", "RIL" },
        { "ril", "RIL" },
        { "p o p s", "POPS" },
        { "pops", "POPS" }
    };

    [SerializeField] private VoiceIntents voiceIntents;
    [SerializeField] private Button recordButton;
    [SerializeField] private Text recordButtonText;
    [SerializeField] private Text responseTextBox;
    [SerializeField] private VoskSpeechToText voskSpeechToText;
    [SerializeField] private VoiceProcessor voiceProcessor;
    [SerializeField] private string voskModelPath = DefaultVoskModelPath;
    [SerializeField] private int maxAlternatives = 1;
    [SerializeField] private float initializationTimeoutSeconds = 120f;
    [SerializeField] private float silenceStopSeconds = 2f;
    [SerializeField] private bool logTranscripts = true;

    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();
    private bool hasRecordPermission;
    private bool pendingStartAfterPermission;
    private bool isVoskInitializing;
    private bool isVoskInitialized;
    private bool isRecording;
    private Coroutine initializationTimeoutCoroutine;

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
        ConfigureVosk();

        hasRecordPermission = MLPermissions.CheckPermission(MLPermission.RecordAudio).IsOk;
        RefreshButtonVisuals();
    }

    private void OnDestroy()
    {
        StopInitializationTimeout();

        permissionCallbacks.OnPermissionGranted -= OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied -= OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain -= OnPermissionDenied;

        if (voskSpeechToText != null)
        {
            voskSpeechToText.OnStatusUpdated -= HandleVoskStatusUpdated;
            voskSpeechToText.OnTranscriptionResult -= HandleTranscriptionResult;
            voskSpeechToText.OnPartialTranscriptionResult -= HandlePartialTranscriptionResult;
        }

        if (voiceProcessor != null)
        {
            voiceProcessor.OnRecordingStop -= HandleVoiceRecordingStop;
        }

        if (voiceProcessor != null && voiceProcessor.IsRecording)
        {
            voiceProcessor.StopRecording();
        }
    }

    public void ToggleRecording()
    {
        if (!EnsureMicrophonePermission())
        {
            return;
        }

        if (isVoskInitializing)
        {
            UpdateStatus("Vosk is still initializing...");
            return;
        }

        if (!isVoskInitialized)
        {
            InitializeVosk(startRecordingWhenReady: true);
            return;
        }

        if (voiceProcessor != null && voiceProcessor.IsRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    public void StartRecordingFromVoiceIntent()
    {
        if (!EnsureMicrophonePermission())
        {
            return;
        }

        if (isVoskInitializing)
        {
            UpdateStatus("Vosk is still initializing...");
            return;
        }

        if (voiceProcessor != null && voiceProcessor.IsRecording)
        {
            UpdateStatus("Already recording your question. Tap Stop Recording when finished.");
            RefreshButtonVisuals();
            return;
        }

        if (!isVoskInitialized)
        {
            InitializeVosk(startRecordingWhenReady: true);
            return;
        }

        StartRecording();
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
        voiceProcessor ??= gameObject.GetComponent<VoiceProcessor>();
        if (voiceProcessor == null)
        {
            voiceProcessor = gameObject.AddComponent<VoiceProcessor>();
        }

        voskSpeechToText ??= gameObject.GetComponent<VoskSpeechToText>();
        if (voskSpeechToText == null)
        {
            voskSpeechToText = gameObject.AddComponent<VoskSpeechToText>();
        }
    }

    private void ConfigureVosk()
    {
        if (voskSpeechToText == null)
        {
            return;
        }

        voskSpeechToText.AutoStart = false;
        voskSpeechToText.ModelPath = GetSafeVoskModelPath();
        voskSpeechToText.MaxAlternatives = maxAlternatives;
        voskSpeechToText.EmitResultsOnlyOnStop = true;
        voskSpeechToText.AutoStopRecordingOnSilence = true;
        voskSpeechToText.SilenceTimeoutSeconds = silenceStopSeconds;
        voskSpeechToText.VoiceProcessor = voiceProcessor;
        voskSpeechToText.OnStatusUpdated -= HandleVoskStatusUpdated;
        voskSpeechToText.OnTranscriptionResult -= HandleTranscriptionResult;
        voskSpeechToText.OnPartialTranscriptionResult -= HandlePartialTranscriptionResult;
        voskSpeechToText.OnStatusUpdated += HandleVoskStatusUpdated;
        voskSpeechToText.OnTranscriptionResult += HandleTranscriptionResult;
        voskSpeechToText.OnPartialTranscriptionResult += HandlePartialTranscriptionResult;

        if (voiceProcessor != null)
        {
            voiceProcessor.OnRecordingStop -= HandleVoiceRecordingStop;
            voiceProcessor.OnRecordingStop += HandleVoiceRecordingStop;
        }
    }

    private bool EnsureMicrophonePermission()
    {
        if (hasRecordPermission || MLPermissions.CheckPermission(MLPermission.RecordAudio).IsOk)
        {
            hasRecordPermission = true;
            return true;
        }

        pendingStartAfterPermission = true;
        UpdateStatus("Microphone permission required for Vosk recording.");
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
        ToggleRecording();
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

    private void InitializeVosk(bool startRecordingWhenReady)
    {
        if (voskSpeechToText == null)
        {
            UpdateStatus("Vosk speech recognizer is missing.");
            return;
        }

        try
        {
            isVoskInitializing = true;
            UpdateStatus("Loading Vosk model...");
            RefreshButtonVisuals();

            string safeModelPath = GetSafeVoskModelPath();
            Debug.Log($"[Vosk] AIA initializing model path: {safeModelPath}");
            StartInitializationTimeout();

            voskSpeechToText.StartVoskStt(
                keyPhrases: new List<string>(NasaMissionKeyPhrases),
                modelPath: safeModelPath,
                startMicrophone: startRecordingWhenReady,
                maxAlternatives: maxAlternatives);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Vosk] Failed to initialize: {exception}");
            isVoskInitializing = false;
            StopInitializationTimeout();
            UpdateStatus("Vosk initialization failed.");
            RefreshButtonVisuals();
        }
    }

    private void StartRecording()
    {
        if (voskSpeechToText == null)
        {
            UpdateStatus("Vosk speech recognizer is missing.");
            return;
        }

        try
        {
            if (voiceIntents != null)
            {
                voiceIntents.BeginRecordingTranscript();
            }
            else
            {
                UpdateStatus("Recording your question... Pause for 2 seconds or tap Stop.");
            }

            voskSpeechToText.ToggleRecording();
            isRecording = voiceProcessor != null && voiceProcessor.IsRecording;
            RefreshButtonVisuals();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Vosk] Failed to start recording: {exception}");
            UpdateStatus("Failed to start Vosk recording.");
            isRecording = false;
            RefreshButtonVisuals();
        }
    }

    private void StopRecording()
    {
        if (voskSpeechToText == null || voiceProcessor == null || !voiceProcessor.IsRecording)
        {
            isRecording = false;
            RefreshButtonVisuals();
            return;
        }

        voiceIntents?.ShowRecordingProcessing();
        voskSpeechToText.ToggleRecording();
        isRecording = false;
        RefreshButtonVisuals();
    }

    private void HandleVoskStatusUpdated(string status)
    {
        if (string.Equals(status, "Initialized", StringComparison.OrdinalIgnoreCase))
        {
            isVoskInitializing = false;
            isVoskInitialized = true;
            isRecording = voiceProcessor != null && voiceProcessor.IsRecording;
            if (isRecording)
            {
                if (voiceIntents != null)
                {
                    voiceIntents.BeginRecordingTranscript();
                }
                else
                {
                    UpdateStatus("Recording your question... Pause for 2 seconds or tap Stop.");
                }
            }
            StopInitializationTimeout();
        }
        else if (!string.IsNullOrWhiteSpace(status))
        {
            UpdateStatus(status);
        }

        RefreshButtonVisuals();
    }

    private void HandleVoiceRecordingStop()
    {
        isRecording = false;
        RefreshButtonVisuals();
    }

    private void HandleTranscriptionResult(string rawJson)
    {
        try
        {
            var result = new RecognitionResult(rawJson);
            if (result.Partial || result.Phrases == null || result.Phrases.Length == 0)
            {
                return;
            }

            string transcript = result.Phrases[0]?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(transcript))
            {
                Debug.LogWarning($"[Vosk] Empty transcription result: {rawJson}");
                if (voiceIntents != null)
                {
                    voiceIntents.FailActiveRecording("Vosk could not transcribe that recording.");
                }
                else
                {
                    UpdateStatus("Vosk could not transcribe that recording.");
                }
                return;
            }

            transcript = NormalizeDomainTranscript(transcript);

            if (logTranscripts)
            {
                Debug.Log($"[Vosk] Transcript: {transcript}");
            }

            if (voiceIntents == null)
            {
                UpdateStatus(transcript);
                Debug.LogWarning("[Vosk] VoiceIntents reference is missing, so transcript was not forwarded to Luna.");
                return;
            }

            voiceIntents.SubmitPromptFromText(transcript);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Vosk] Failed to parse transcription result '{rawJson}': {exception}");
            UpdateStatus("Vosk transcription failed.");
        }
    }

    private void HandlePartialTranscriptionResult(string rawJson)
    {
        if (voiceProcessor == null || !voiceProcessor.IsRecording)
        {
            return;
        }

        try
        {
            var result = new RecognitionResult(rawJson);
            if (!result.Partial || result.Phrases == null || result.Phrases.Length == 0)
            {
                return;
            }

            string partialTranscript = result.Phrases[0]?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(partialTranscript))
            {
                return;
            }

            partialTranscript = NormalizeDomainTranscript(partialTranscript);

            if (voiceIntents != null)
            {
                voiceIntents.UpdateRecordingTranscript(partialTranscript);
            }
            else
            {
                UpdateStatus(partialTranscript);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Vosk] Failed to parse partial transcription result '{rawJson}': {exception}");
        }
    }

    private static string NormalizeDomainTranscript(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return transcript;
        }

        string normalizedTranscript = Regex.Replace(transcript.Trim(), @"\s+", " ");
        for (int i = 0; i < TranscriptNormalizations.GetLength(0); i++)
        {
            string spokenForm = TranscriptNormalizations[i, 0];
            string normalizedForm = TranscriptNormalizations[i, 1];
            string pattern = $@"\b{Regex.Escape(spokenForm)}\b";
            normalizedTranscript = Regex.Replace(
                normalizedTranscript,
                pattern,
                normalizedForm,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return normalizedTranscript;
    }

    private void RefreshButtonVisuals()
    {
        if (recordButton != null)
        {
            recordButton.interactable = !isVoskInitializing;
        }

        if (recordButtonText == null)
        {
            return;
        }

        if (isVoskInitializing)
        {
            recordButtonText.text = BusyButtonLabel;
        }
        else if (isRecording || (voiceProcessor != null && voiceProcessor.IsRecording))
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

        Debug.LogWarning($"[Vosk] Could not display status because no AIA text box was found. Status: {text}");
    }

    private string GetSafeVoskModelPath()
    {
        if (string.IsNullOrWhiteSpace(voskModelPath))
        {
            return DefaultVoskModelPath;
        }

        return voskModelPath.Trim();
    }

    private void StartInitializationTimeout()
    {
        StopInitializationTimeout();
        if (initializationTimeoutSeconds <= 0f)
        {
            return;
        }

        initializationTimeoutCoroutine = StartCoroutine(InitializationTimeout());
    }

    private void StopInitializationTimeout()
    {
        if (initializationTimeoutCoroutine == null)
        {
            return;
        }

        StopCoroutine(initializationTimeoutCoroutine);
        initializationTimeoutCoroutine = null;
    }

    private IEnumerator InitializationTimeout()
    {
        yield return new WaitForSeconds(initializationTimeoutSeconds);

        if (!isVoskInitializing || isVoskInitialized)
        {
            yield break;
        }

        Debug.LogError("[Vosk] Initialization timed out before the recognizer reported Initialized.");
        isVoskInitializing = false;
        UpdateStatus("Vosk initialization timed out. Check model path and native library.");
        RefreshButtonVisuals();
    }
}
