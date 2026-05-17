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
    [SerializeField, Range(0f, 1f), Tooltip(
        "Volume threshold (0–1) above which a sample is treated as speech. " +
        "The Magic Leap 2 headset mic typically peaks around 0.02–0.04 for normal " +
        "indoor speech; the original Picovoice default of 0.05 was too high and " +
        "caused Vosk to receive zero audio frames.")]
    private float voiceDetectionThreshold = 0.01f;
    [SerializeField, Tooltip(
        "Hard cap on recording length (seconds). If the user starts a recording and " +
        "the VAD never trips (e.g. mic is muted), recording is force-stopped after this many seconds " +
        "so the user sees a real error instead of a hanging UI.")]
    private float maxRecordingSeconds = 15f;
    [SerializeField] private bool logTranscripts = true;
    [SerializeField, Tooltip("Log [Vosk] audio-level samples while recording. Useful for diagnosing mic-gain issues.")]
    private bool logAudioLevels = true;

    private Coroutine maxRecordingTimeoutCoroutine;
    private Coroutine audioLevelMonitorCoroutine;
    private float peakAmplitudeThisSession;

    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();
    private bool hasRecordPermission;
    private bool pendingStartAfterPermission;
    private bool isVoskInitializing;
    private bool isVoskInitialized;
    private bool isRecording;
    // Set true once a partial transcript routes a scene-transition command this session,
    // so subsequent partials don't double-trigger before recording stops.
    private bool _routedSceneVoiceThisSession;
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
            // Prefer the persistent singleton: a freshly-loaded scene's local VoiceIntent
            // briefly coexists with the persistent one during the same frame, and
            // GameObject.Find can return the doomed duplicate before its Destroy resolves.
            voiceIntents = VoiceIntents.Instance;
            if (voiceIntents == null)
            {
                GameObject voiceIntentObject = GameObject.Find("VoiceIntent");
                if (voiceIntentObject != null)
                {
                    voiceIntents = voiceIntentObject.GetComponent<VoiceIntents>();
                }
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

        if (voiceProcessor != null)
        {
            voiceProcessor.MinimumSpeakingSampleValue = voiceDetectionThreshold;
        }
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
                keyPhrases: new List<string>(),
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
            _routedSceneVoiceThisSession = false;
            peakAmplitudeThisSession = 0f;

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
            BeginRecordingTimeouts();
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
            CancelRecordingTimeouts();
            RefreshButtonVisuals();
            return;
        }

        voiceIntents?.ShowRecordingProcessing();
        voskSpeechToText.ToggleRecording();
        isRecording = false;
        CancelRecordingTimeouts();
        RefreshButtonVisuals();
    }

    private void BeginRecordingTimeouts()
    {
        CancelRecordingTimeouts();
        if (maxRecordingSeconds > 0f)
        {
            maxRecordingTimeoutCoroutine = StartCoroutine(MaxRecordingWatchdog(maxRecordingSeconds));
        }
        if (logAudioLevels)
        {
            audioLevelMonitorCoroutine = StartCoroutine(MonitorAudioLevels());
        }
    }

    private void CancelRecordingTimeouts()
    {
        if (maxRecordingTimeoutCoroutine != null)
        {
            StopCoroutine(maxRecordingTimeoutCoroutine);
            maxRecordingTimeoutCoroutine = null;
        }
        if (audioLevelMonitorCoroutine != null)
        {
            StopCoroutine(audioLevelMonitorCoroutine);
            audioLevelMonitorCoroutine = null;
        }
    }

    private IEnumerator MaxRecordingWatchdog(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (voiceProcessor != null && voiceProcessor.IsRecording)
        {
            Debug.LogWarning($"[Vosk] Max-recording watchdog fired after {seconds:F1}s — force-stopping. " +
                             $"Peak mic amplitude this session: {peakAmplitudeThisSession:F4} (threshold {voiceDetectionThreshold:F4}).");
            StopRecording();
        }
    }

    private IEnumerator MonitorAudioLevels()
    {
        // ~4 Hz sample. Helps confirm whether the mic is hearing anything at all.
        WaitForSeconds wait = new WaitForSeconds(0.25f);
        float lastLogTime = 0f;
        while (voiceProcessor != null && voiceProcessor.IsRecording)
        {
            float level = voiceProcessor.LastFrameMaxAmplitude;
            if (level > peakAmplitudeThisSession) peakAmplitudeThisSession = level;

            if (Time.unscaledTime - lastLogTime >= 1f)
            {
                Debug.Log($"[Vosk] mic level (peak this frame) = {level:F4}, " +
                          $"session peak = {peakAmplitudeThisSession:F4}, threshold = {voiceDetectionThreshold:F4}.");
                lastLogTime = Time.unscaledTime;
            }
            yield return wait;
        }
        audioLevelMonitorCoroutine = null;
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
        CancelRecordingTimeouts();
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
                bool micWasHeard = peakAmplitudeThisSession >= voiceDetectionThreshold;
                Debug.LogWarning(
                    $"[Vosk] Empty transcription result: {rawJson}. " +
                    $"Peak mic amplitude this session = {peakAmplitudeThisSession:F4} " +
                    $"(threshold {voiceDetectionThreshold:F4}). " +
                    (micWasHeard
                        ? "Mic was audible but Vosk did not recognize any words — try speaking closer to the headset or check the model is loaded."
                        : "Mic NEVER crossed the VAD threshold — check that the headset microphone is enabled, unmuted, and that no other process (e.g. MLVoice/Luna) is holding it. " +
                          "If peak stays near 0.0000, the mic is not capturing at all."));

                string userMessage = micWasHeard
                    ? "Vosk did not recognize that audio. Try speaking a bit closer or louder."
                    : "Vosk did not hear any audio from the mic. Check that the headset mic is enabled and not muted.";

                if (voiceIntents != null)
                {
                    voiceIntents.FailActiveRecording(userMessage);
                }
                else
                {
                    UpdateStatus(userMessage);
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

                // VoiceProcessor's silence detection on ML2 doesn't reliably fire, so the
                // recording stays open and the final transcript never emits. Route scene
                // transitions off the live partial stream instead — fires the moment Vosk
                // has heard enough audio to recognize a configured command.
                if (!_routedSceneVoiceThisSession &&
                    voiceIntents.TryRouteSceneVoiceCommand(partialTranscript))
                {
                    _routedSceneVoiceThisSession = true;
                    Debug.Log($"[Vosk] Scene-voice command routed from partial transcript: '{partialTranscript}'. Stopping recording.");
                    StopRecording();
                }
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
