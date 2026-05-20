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
    private static readonly string[] SendRecordingCommands =
    {
        "send recording",
        "sent recording",
        "said recording",
        "sed recording",
        "sod recording",
        "set recording",
        "sand recording",
        "send record",
        "sent record",
        "said record",
        "send a recording",
        "send the recording",
        "send my recording",
        "submit recording",
        "submit record",
        "end recording"
    };
    private static readonly string[] PurgeRecordingCommands =
    {
        "purge recording",
        "purge the recording",
        "clear recording",
        "cancel recording",
        "discard recording",
        "delete recording",
        "flush recording",
        "erase recording",
        "purge record",
        "perch recording",
        "urge recording",
        "clear record",
        "cancel record",
        "discard record",
        "delete record",
        "flush record"
    };
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
    [SerializeField, Tooltip(
        "Decompress the Vosk zip and load the model as soon as the scene starts, " +
        "before the user ever presses Record. This eliminates the multi-hundred-ms " +
        "main-thread stall that otherwise causes a black-frame / flicker on the ML2 " +
        "the first time a recording is started. Has no effect in the Editor / on " +
        "non-Android builds because libvosk is Android-only.")]
    private bool preloadVoskOnStart = true;
    [SerializeField] private float silenceStopSeconds = 1.75f;
    [SerializeField, Range(0f, 1f), Tooltip(
        "Volume threshold (0–1) above which a sample is treated as speech. " +
        "The Magic Leap 2 headset mic typically peaks around 0.02–0.04 for normal " +
        "indoor speech; the original Picovoice default of 0.05 was too high and " +
        "caused Vosk to receive zero audio frames.")]
    private float voiceDetectionThreshold = 0.009f;
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
    private bool discardNextTranscriptionResult;
    private Coroutine initializationTimeoutCoroutine;
    // Set true while we are preloading Vosk in the background. Causes
    // HandleVoskStatusUpdated to swallow progress text ("Loading Model
    // from: ...", "Decompressing model...") so we don't overwrite the
    // AIA panel's idle copy. Failure statuses are NOT suppressed.
    private bool suppressVoskStatusUpdates;
    // Pending start triggered while a preload is still in flight.
    private Coroutine pendingStartAfterPreloadCoroutine;

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

        // Kick off the Vosk model decompress + load now, while the AIA
        // panel is still showing its idle UI. The decompressed model
        // lives under persistentDataPath and is reused across launches,
        // so on warm starts this is just the (off-main-thread) Model
        // mmap. Without preloading, the first record-press blocks the
        // main thread long enough to blank the ML2 compositor.
        TryPreloadVosk();

        RefreshButtonVisuals();
    }

    private void OnDestroy()
    {
        StopInitializationTimeout();

        if (pendingStartAfterPreloadCoroutine != null)
        {
            StopCoroutine(pendingStartAfterPreloadCoroutine);
            pendingStartAfterPreloadCoroutine = null;
        }

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

    /// <summary>
    /// Wired to the on-screen "Start/Stop Recording" button.
    /// - Idle  → start a new recording session.
    /// - Active → stop the recording. The final transcript that Vosk emits on stop
    ///   flows through HandleTranscriptionResult → VoiceIntents.SubmitPromptFromText,
    ///   which POSTs the transcript to the AIA /chat endpoint and renders the response
    ///   in the AIA panel. So clicking Stop also submits the in-progress query.
    /// </summary>
    public void ToggleRecording()
    {
        if (voiceIntents != null && !voiceIntents.IsLunaActive)
        {
            Debug.Log("[Luna] Recording button ignored because Luna is deactivated.");
            return;
        }

        if (!EnsureMicrophonePermission())
        {
            return;
        }

        // Already recording — stop and submit. Same as before.
        if (voiceProcessor != null && voiceProcessor.IsRecording)
        {
            StopRecording();
            return;
        }

        if (isVoskInitializing)
        {
            // A preload is in flight. Queue the start so the user doesn't
            // have to press the button a second time once init completes.
            UpdateStatus("Vosk is still loading — recording will start in a moment...");
            QueueStartAfterPreload();
            return;
        }

        if (!isVoskInitialized)
        {
            InitializeVosk(startRecordingWhenReady: true);
            return;
        }

        StartRecording();
    }

    /// <summary>
    /// "Hey Luna" wake-phrase entry point. Always starts a fresh session — if a
    /// recording is already active, it is stopped first so the user gets a new
    /// transcript window rather than appending to an old one.
    /// </summary>
    public void StartRecordingFromVoiceIntent()
    {
        if (voiceIntents != null && !voiceIntents.IsLunaActive)
        {
            Debug.Log("[Luna] Hey Luna ignored because Luna is deactivated.");
            return;
        }

        if (!EnsureMicrophonePermission())
        {
            return;
        }

        if (voiceProcessor != null && voiceProcessor.IsRecording)
        {
            StopRecording();
            StartCoroutine(RestartRecordingNextFrame());
            return;
        }

        if (isVoskInitializing)
        {
            // Preload still running — wait for it instead of bouncing
            // the wake phrase. Matches the previous Codex preload fix.
            QueueStartAfterPreload();
            return;
        }

        if (!isVoskInitialized)
        {
            InitializeVosk(startRecordingWhenReady: true);
            return;
        }

        StartRecording();
    }

    private IEnumerator RestartRecordingNextFrame()
    {
        yield return null;
        if (isVoskInitialized)
        {
            StartRecording();
        }
    }

    private void QueueStartAfterPreload()
    {
        if (pendingStartAfterPreloadCoroutine != null)
        {
            // Already waiting — don't stack multiple coroutines.
            return;
        }
        pendingStartAfterPreloadCoroutine = StartCoroutine(StartRecordingWhenInitialized());
    }

    private IEnumerator StartRecordingWhenInitialized()
    {
        // Wait for preload to finish (or fail). isVoskInitializing flips
        // false in HandleVoskStatusUpdated when we receive "Initialized"
        // or any failure status.
        while (isVoskInitializing)
        {
            yield return null;
        }

        pendingStartAfterPreloadCoroutine = null;

        if (!isVoskInitialized)
        {
            // Preload failed — nothing more to do, the status text already
            // shows the failure.
            yield break;
        }

        if (voiceIntents != null && !voiceIntents.IsLunaActive)
        {
            yield break;
        }

        if (voiceProcessor != null && voiceProcessor.IsRecording)
        {
            yield break;
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

        // We may have preloaded Vosk before the mic permission was
        // granted, in which case Microphone.devices was empty and
        // VoiceProcessor's device list is stale. Refresh it now so the
        // first StartRecording picks up the real headset mic.
        if (voiceProcessor != null)
        {
            voiceProcessor.UpdateDevices();
        }

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

    private void TryPreloadVosk()
    {
        if (!preloadVoskOnStart)
        {
            return;
        }

        if (isVoskInitialized || isVoskInitializing)
        {
            return;
        }

        if (voskSpeechToText == null)
        {
            return;
        }

        // libvosk is Android-only — see VoskSpeechToText.IsVoskNativeAvailable.
        // Calling StartVoskStt on the Mac Editor would still bail out
        // safely via the gate inside VoskSpeechToText, but we avoid even
        // logging a warning on author machines.
        if (!VoskSpeechToText.IsVoskNativeAvailable)
        {
            return;
        }

        InitializeVosk(startRecordingWhenReady: false, showStatus: false);
    }

    private void InitializeVosk(bool startRecordingWhenReady, bool showStatus = true)
    {
        if (voskSpeechToText == null)
        {
            if (showStatus)
            {
                UpdateStatus("Vosk speech recognizer is missing.");
            }
            return;
        }

        try
        {
            isVoskInitializing = true;
            // Only suppress when caller asked AND we're not also starting
            // the mic — once recording is starting, the user needs to see
            // progress text.
            suppressVoskStatusUpdates = !showStatus && !startRecordingWhenReady;
            if (showStatus)
            {
                UpdateStatus("Loading Vosk model...");
            }
            RefreshButtonVisuals();

            string safeModelPath = GetSafeVoskModelPath();
            Debug.Log($"[Vosk] AIA initializing model path: {safeModelPath} (preload={!startRecordingWhenReady})");
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
            suppressVoskStatusUpdates = false;
            StopInitializationTimeout();
            if (showStatus)
            {
                UpdateStatus("Vosk initialization failed.");
            }
            RefreshButtonVisuals();
        }
    }

    private void StartRecording()
    {
        if (voiceIntents != null && !voiceIntents.IsLunaActive)
        {
            Debug.Log("[Luna] Vosk recording start ignored because Luna is deactivated.");
            return;
        }

        if (voskSpeechToText == null)
        {
            UpdateStatus("Vosk speech recognizer is missing.");
            return;
        }

        try
        {
            _routedSceneVoiceThisSession = false;
            discardNextTranscriptionResult = false;
            peakAmplitudeThisSession = 0f;

            if (voiceIntents != null)
            {
                voiceIntents.BeginRecordingTranscript();
            }
            else
            {
                UpdateStatus("Recording your question... Pause briefly or tap Stop.");
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

    public void CancelRecordingWithoutSubmit(string statusMessage = null)
    {
        if (voskSpeechToText == null || voiceProcessor == null || !voiceProcessor.IsRecording)
        {
            isRecording = false;
            CancelRecordingTimeouts();
            RefreshButtonVisuals();
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                voiceIntents?.FailActiveRecording(statusMessage);
            }
            return;
        }

        discardNextTranscriptionResult = true;
        voskSpeechToText.ToggleRecording();
        isRecording = false;
        CancelRecordingTimeouts();
        RefreshButtonVisuals();

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            voiceIntents?.FailActiveRecording(statusMessage);
        }
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
            suppressVoskStatusUpdates = false;
            isRecording = voiceProcessor != null && voiceProcessor.IsRecording;
            if (isRecording)
            {
                if (voiceIntents != null)
                {
                    voiceIntents.BeginRecordingTranscript();
                }
                else
                {
                    UpdateStatus("Recording your question... Pause briefly or tap Stop.");
                }
            }
            StopInitializationTimeout();
        }
        else if (IsVoskFailureStatus(status))
        {
            // Failures should always surface, even if we asked to
            // suppress the chatter during a preload.
            isVoskInitializing = false;
            suppressVoskStatusUpdates = false;
            StopInitializationTimeout();
            UpdateStatus(status);
        }
        else if (!string.IsNullOrWhiteSpace(status) && !suppressVoskStatusUpdates)
        {
            UpdateStatus(status);
        }

        RefreshButtonVisuals();
    }

    private static bool IsVoskFailureStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
               status.IndexOf("could not", StringComparison.OrdinalIgnoreCase) >= 0 ||
               status.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
               status.IndexOf("not available", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void HandleVoiceRecordingStop()
    {
        isRecording = false;
        CancelRecordingTimeouts();
        RefreshButtonVisuals();
    }

    private void HandleTranscriptionResult(string rawJson)
    {
        if (discardNextTranscriptionResult)
        {
            discardNextTranscriptionResult = false;
            Debug.Log("[Vosk] Discarding final transcript because the recording was purged or Luna was deactivated.");
            return;
        }

        if (voiceIntents != null && !voiceIntents.IsLunaActive)
        {
            Debug.Log("[Luna] Ignoring Vosk transcript because Luna is deactivated.");
            return;
        }

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
            if (ContainsCommandAlias(partialTranscript, PurgeRecordingCommands))
            {
                Debug.Log($"[Vosk] Purge-recording command detected in partial transcript: '{partialTranscript}'. Discarding recording.");
                CancelRecordingWithoutSubmit("Recording purged.");
                return;
            }

            if (ContainsCommandAlias(partialTranscript, SendRecordingCommands))
            {
                Debug.Log($"[Vosk] Send-recording command detected in partial transcript: '{partialTranscript}'. Stopping recording.");
                StopRecording();
                return;
            }

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

    private static bool ContainsCommandAlias(string transcript, string[] aliases)
    {
        if (string.IsNullOrWhiteSpace(transcript) || aliases == null)
        {
            return false;
        }

        for (int i = 0; i < aliases.Length; i++)
        {
            if (transcript.IndexOf(aliases[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
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
