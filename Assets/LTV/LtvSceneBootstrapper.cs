using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives in the LTV scene. On Start, if <see cref="LtvVoiceCoordinator.PendingVoiceTrigger"/>
/// is set (i.e. we got here via the "ltv repair" voice command from Mission), runs the
/// bookend flow:
///   1. Green flash overlay.
///   2. StartDiagnosisFromTss; DiagnosisStarted(count) fires.
///      - count > 0: speak "Initializing LTV-Repair. Found X issues..." and let the user work.
///      - count == 0: speak "No LTV issues found, returning to Mission" and auto-return.
///   3. When AllErrorsResolved fires (and we had real work to do), green flash again,
///      speak "LTV-repair finished", auto-return to Mission.
/// </summary>
public class LtvSceneBootstrapper : MonoBehaviour
{
    [Header("Scene wiring")]
    [Tooltip("LTV repair queue / TSS bridge. Defaults to the first one found in the scene.")]
    public LtvInstructionService instructionService;

    [Tooltip("Fullscreen green flash overlay. Defaults to the first one found in the scene.")]
    public LtvFlashOverlay flashOverlay;

    [Header("Return")]
    [Tooltip("Scene to load after the repair flow ends. Must be in Build Settings.")]
    public string returnSceneName = "Mission";

    [Tooltip("Seconds to wait after the end-flash + TTS before loading the return scene.")]
    public float returnDelaySeconds = 3f;

    [Header("Behavior")]
    [Tooltip("If true, runs the flow even when the voice trigger flag isn't set " +
             "(useful for editor testing).")]
    public bool runWithoutVoiceTrigger = false;

    [Tooltip("Log every transition through the bootstrap state machine.")]
    public bool enableDebugLogs = true;

    private bool _subscribed;
    private bool _hadInitialErrors;
    private bool _returnScheduled;

    private void Start()
    {
        bool triggered = LtvVoiceCoordinator.PendingVoiceTrigger;
        LtvVoiceCoordinator.ClearPendingTrigger();

        if (!triggered && !runWithoutVoiceTrigger)
        {
            if (enableDebugLogs)
            {
                Debug.Log("[LtvBoot] No voice trigger; standing by. (Set runWithoutVoiceTrigger to test in editor.)", this);
            }
            return;
        }

        ResolveSceneRefs();

        if (instructionService == null)
        {
            Debug.LogError("[LtvBoot] LtvInstructionService not found in scene; cannot run repair flow.", this);
            return;
        }

        SubscribeServiceEvents();

        if (flashOverlay != null)
        {
            flashOverlay.Flash();
        }

        // StartDiagnosisFromTss is synchronous w.r.t. firing DiagnosisStarted.
        // Calling here means by the time we return, DiagnosisStarted has already
        // fired and OnDiagnosisStarted has handled the count-based branching.
        instructionService.StartDiagnosisFromTss();
    }

    private void OnDestroy()
    {
        UnsubscribeServiceEvents();
    }

    private void ResolveSceneRefs()
    {
        if (instructionService == null)
        {
            instructionService = FindObjectOfType<LtvInstructionService>();
        }

        if (flashOverlay == null)
        {
            flashOverlay = FindObjectOfType<LtvFlashOverlay>();
        }
    }

    private void SubscribeServiceEvents()
    {
        if (_subscribed || instructionService == null) return;
        instructionService.DiagnosisStarted += OnDiagnosisStarted;
        instructionService.AllErrorsResolved += OnAllErrorsResolved;
        _subscribed = true;
    }

    private void UnsubscribeServiceEvents()
    {
        if (!_subscribed || instructionService == null) return;
        instructionService.DiagnosisStarted -= OnDiagnosisStarted;
        instructionService.AllErrorsResolved -= OnAllErrorsResolved;
        _subscribed = false;
    }

    private void OnDiagnosisStarted(int errorCount)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[LtvBoot] DiagnosisStarted count={errorCount}", this);
        }

        if (errorCount <= 0)
        {
            _hadInitialErrors = false;
            Speak("No LTV issues found, returning to Mission.");
            ScheduleReturnToMission();
            return;
        }

        _hadInitialErrors = true;
        string issueWord = errorCount == 1 ? "issue" : "issues";
        Speak($"Initializing LTV repair. Found {errorCount} {issueWord}. Please follow the instructions at the top of the screen.");
    }

    private void OnAllErrorsResolved()
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[LtvBoot] AllErrorsResolved (hadInitialErrors={_hadInitialErrors})", this);
        }

        if (!_hadInitialErrors)
        {
            // The "0 initial issues" path already scheduled the return; don't double-fire.
            return;
        }

        if (flashOverlay != null)
        {
            flashOverlay.Flash();
        }

        Speak("LTV repair finished.");
        ScheduleReturnToMission();
    }

    private void ScheduleReturnToMission()
    {
        if (_returnScheduled) return;
        _returnScheduled = true;
        StartCoroutine(ReturnToMissionAfterDelay());
    }

    private IEnumerator ReturnToMissionAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, returnDelaySeconds));

        if (enableDebugLogs)
        {
            Debug.Log($"[LtvBoot] Loading return scene '{returnSceneName}'.", this);
        }

        if (string.IsNullOrWhiteSpace(returnSceneName))
        {
            Debug.LogWarning("[LtvBoot] returnSceneName is empty; staying in LTV scene.", this);
            yield break;
        }

        SceneManager.LoadScene(returnSceneName);
    }

    private void Speak(string text)
    {
        if (LunaTtsBridge.Instance != null)
        {
            LunaTtsBridge.Instance.Speak(text);
        }
        else if (enableDebugLogs)
        {
            Debug.LogWarning($"[LtvBoot] LunaTtsBridge.Instance is null; would have spoken: '{text}'", this);
        }
    }
}
