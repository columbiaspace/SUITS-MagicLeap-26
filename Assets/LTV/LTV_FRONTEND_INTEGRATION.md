# LTV Frontend Integration Guide

How to connect UI buttons, voice commands, and display panels to `LtvErrorQueueService`.

## Setup

1. Attach `LtvErrorQueueService` to a GameObject in your scene (or the same one that has `TssUnityApiService`).
2. It auto-finds `TssUnityApiService` on Awake. No manual wiring needed unless you have multiple instances.
3. Inspector settings:
   - **Verification Poll Seconds**: How often to check TSS after astronaut finishes all steps (default 1s)
   - **Verification Timeout Seconds**: How long to wait before declaring resolution failed (default 10s)
   - **Max Retries**: How many times to re-show instructions before skipping an error (default 3)

## Quick Start — Minimal Script

```csharp
using LtvDiagnostics;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class LtvDiagnosticsUI : MonoBehaviour
{
    [SerializeField] private LtvErrorQueueService queueService;
    [SerializeField] private Text instructionText;
    [SerializeField] private Text errorInfoText;
    [SerializeField] private Text progressText;
    [SerializeField] private Button nextStepButton;
    [SerializeField] private Button startButton;
    [SerializeField] private GameObject retryWarningPanel;

    void OnEnable()
    {
        queueService.StepChanged += OnStepChanged;
        queueService.ErrorChanged += OnErrorChanged;
        queueService.ResolutionFailed += OnResolutionFailed;
        queueService.MaxRetriesExceeded += OnMaxRetriesExceeded;
        queueService.AllErrorsResolved += OnAllResolved;

        nextStepButton.onClick.AddListener(OnNextStepPressed);
        startButton.onClick.AddListener(OnStartPressed);
    }

    void OnDisable()
    {
        queueService.StepChanged -= OnStepChanged;
        queueService.ErrorChanged -= OnErrorChanged;
        queueService.ResolutionFailed -= OnResolutionFailed;
        queueService.MaxRetriesExceeded -= OnMaxRetriesExceeded;
        queueService.AllErrorsResolved -= OnAllResolved;

        nextStepButton.onClick.RemoveListener(OnNextStepPressed);
        startButton.onClick.RemoveListener(OnStartPressed);
    }

    // --- Button Handlers ---

    void OnStartPressed()
    {
        queueService.StartDiagnosisFromTss();
    }

    void OnNextStepPressed()
    {
        queueService.AdvanceStep();
    }

    // --- Event Handlers ---

    void OnErrorChanged(LtvError error)
    {
        retryWarningPanel.SetActive(false);
        errorInfoText.text = $"Error {error.Code}: {error.Description}\n" +
                             $"Priority: {error.Priority} | Remaining: {queueService.RemainingErrors}";
    }

    void OnStepChanged(LtvError error, int stepIndex)
    {
        string instruction = error.Procedures[stepIndex];
        instructionText.text = instruction;
        progressText.text = $"Step {stepIndex + 1} of {error.Procedures.Count}";

        // Disable button while verifying
        nextStepButton.interactable = !queueService.IsVerifying;
    }

    void OnResolutionFailed(LtvError error)
    {
        retryWarningPanel.SetActive(true);
        instructionText.text = $"Error {error.Code} was NOT resolved.\n" +
                               $"Retry {queueService.RetryCount}/{3}. " +
                               "Review and repeat all steps.";
    }

    void OnMaxRetriesExceeded(LtvError error)
    {
        instructionText.text = $"Error {error.Code} could not be resolved after max retries. " +
                               "Moving to next error.";
    }

    void OnAllResolved()
    {
        instructionText.text = "All LTV errors resolved.";
        errorInfoText.text = "";
        progressText.text = "";
        nextStepButton.interactable = false;
    }
}
```

## Voice Command Integration

For voice-triggered step advancement (e.g., Magic Leap voice intents or AIA):

```csharp
// In your voice intent handler (e.g., VoiceIntents.cs):

// "Next step" / "Continue" / "Mark done"
void OnVoiceNextStep()
{
    if (queueService.IsDiagnosisActive && !queueService.IsVerifying)
    {
        queueService.AdvanceStep();
    }
}

// "Start diagnosis" / "Begin repairs"
void OnVoiceStartDiagnosis()
{
    if (!queueService.IsDiagnosisActive)
    {
        queueService.StartDiagnosisFromTss();
    }
}

// "Stop diagnosis" / "Cancel repairs"
void OnVoiceStopDiagnosis()
{
    queueService.StopDiagnosis();
}

// "What's the current step?" / "Repeat instruction"
string OnVoiceGetCurrentInstruction()
{
    Dictionary<string, object> snap = queueService.GetCurrentSnapshot();
    return snap["current_instruction"].ToString();
}

// "How many errors left?"
string OnVoiceGetStatus()
{
    if (!queueService.IsDiagnosisActive)
    {
        return "No active diagnosis.";
    }

    LtvError current = queueService.CurrentError;
    return $"Working on error {current.Code}, {current.Description}. " +
           $"Step {queueService.CurrentStepIndex + 1} of {current.Procedures.Count}. " +
           $"{queueService.RemainingErrors} errors remaining.";
}
```

## Events Reference

| Event | Signature | When It Fires | What To Do |
|-------|-----------|---------------|------------|
| `ErrorChanged` | `Action<LtvError>` | New error popped from heap | Update error header/title, reset UI |
| `StepChanged` | `Action<LtvError, int>` | Step index changed (advance or reset to 0) | Show instruction text at `error.Procedures[stepIndex]` |
| `ResolutionFailed` | `Action<LtvError>` | All steps done but TSS says error still active | Show warning, instructions restart from step 0 |
| `MaxRetriesExceeded` | `Action<LtvError>` | Failed resolution more than `maxRetries` times | Show skip message, next error auto-pops |
| `AllErrorsResolved` | `Action` | Heap empty, last error resolved | Show success, disable next-step button |

## Properties You Can Poll

Use these for UI state checks (e.g., button interactability):

```csharp
queueService.IsDiagnosisActive   // is a session running?
queueService.IsVerifying          // waiting for TSS verification?
queueService.CurrentError         // the LtvError being worked on (null if idle)
queueService.CurrentStepIndex     // 0-based index into Procedures list
queueService.RemainingErrors      // how many errors left in the heap
queueService.RetryCount           // how many retries for current error
```

## GetCurrentSnapshot() Dictionary

Alternative to events — poll this for a full state dictionary:

```csharp
Dictionary<string, object> snap = queueService.GetCurrentSnapshot();
```

| Key | Type | Description |
|-----|------|-------------|
| `active` | bool | Is diagnosis running with an active error? |
| `error_code` | string | 4-digit NASA error code (e.g., "4155") |
| `error_description` | string | Human-readable description |
| `priority` | int | Computed priority (criticality * 10 + subsystem) |
| `criticality` | int | First digit of error code (0-4) |
| `subsystem_id` | int | Second digit of error code (0-9) |
| `current_step_index` | int | 0-based step index (-1 if idle) |
| `current_instruction` | string | The procedure step text to display |
| `total_steps` | int | Total steps for this error |
| `remaining_errors` | int | Errors still in the heap |
| `verifying` | bool | Waiting for TSS to confirm resolution |
| `retry_count` | int | Number of failed verification attempts |

## Flow Diagram

```
User presses "Start Diagnosis"
          |
          v
  StartDiagnosisFromTss()
          |
  Fetch error_procedures from TSS
  Parse each, compute priority, insert into max-heap
          |
          v
  PopNextError() --> ErrorChanged event
          |
          v
  Show step 0 --> StepChanged event
          |
  User presses "Next Step" (button or voice)
          |
          v
  AdvanceStep()
          |
    +---- Is this the last step? ----+
    |                                |
    No                              Yes
    |                                |
    v                                v
  Show next step              StartVerification()
  StepChanged event           Poll TSS every 1s for up to 10s
                                     |
                              +------+------+
                              |             |
                          Resolved      NOT Resolved
                              |             |
                              v             v
                        PopNextError()  ResolutionFailed event
                        (or AllErrorsResolved  Reset to step 0
                         if heap empty)    Re-show all instructions
                                           |
                                    (after maxRetries exceeded)
                                           |
                                           v
                                    MaxRetriesExceeded event
                                    Skip to next error
```

## Important Notes

1. **Do NOT call `AdvanceStep()` while `IsVerifying` is true** — the call is silently ignored, but your UI should disable the button to prevent confusion.
2. **`ResolutionFailed` automatically resets to step 0** — the StepChanged event fires right after with stepIndex=0, so your UI updates automatically.
3. **Errors with empty procedures are skipped** — if TSS sends an error with `"procedures": []`, it won't enter the heap.
4. **Errors with `needs_resolved: false` are skipped** — only actionable errors are queued.
5. **The service does NOT store procedures locally** — everything comes from the live TSS `error_procedures` response at diagnosis start time.
