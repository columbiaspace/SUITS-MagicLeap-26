using System;
using System.Collections;
using System.Collections.Generic;
using LtvDiagnostics;
using TssApi;
using UnityEngine;

public class LtvInstructionService : MonoBehaviour
{
    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;

    [Header("Polling")]
    [SerializeField] private float verificationPollSeconds = 1.0f;
    [SerializeField] private float verificationTimeoutSeconds = 10.0f;

    [Header("Retry")]
    [SerializeField] private int maxRetries = 3;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;

    // Events for frontend / UI consumers
    public event Action<Dictionary<string, object>> InstructionUpdated;
    public event Action<LtvError, int> StepChanged;
    public event Action<LtvError> ErrorChanged;
    public event Action<LtvError> ResolutionFailed;
    public event Action<LtvError> MaxRetriesExceeded;
    public event Action AllErrorsResolved;

    // Public read-only state
    public LtvError CurrentError => currentError;
    public int CurrentStepIndex => currentStepIndex;
    public bool IsDiagnosisActive => diagnosisActive;
    public bool IsVerifying => verifying;
    public int RemainingErrors => errorHeap.Count;
    public int RetryCount => retryCount;

    private MaxHeap<LtvError> errorHeap = new MaxHeap<LtvError>();
    private LtvError currentError;
    private int currentStepIndex;
    private bool diagnosisActive;
    private bool verifying;
    private int retryCount;
    private Coroutine verificationCoroutine;

    private void Awake()
    {
        if (tssApi == null)
        {
            tssApi = GetComponent<TssUnityApiService>();
        }

        if (tssApi == null)
        {
            tssApi = TssUnityApiService.Instance;
        }

        if (tssApi == null)
        {
            tssApi = FindObjectOfType<TssUnityApiService>();
        }

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[LTV] Service ready. TssApi linked={tssApi != null}",
                this
            );
        }
    }

    /// <summary>
    /// Fetches error_procedures from TSS, builds priority queue, and starts
    /// walking through errors highest-priority-first.
    /// </summary>
    public void StartDiagnosisFromTss()
    {
        if (tssApi == null)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning("[LTV] Cannot start diagnosis: TssUnityApiService is null.", this);
            }

            return;
        }

        StopVerification();
        errorHeap.Clear();
        currentError = null;
        currentStepIndex = 0;
        retryCount = 0;
        diagnosisActive = true;

        List<Dictionary<string, object>> errorProcedures = tssApi.GetLtvErrorProcedures();

        if (errorProcedures == null || errorProcedures.Count == 0)
        {
            if (enableDebugLogs)
            {
                Debug.Log("[LTV] No error_procedures returned from TSS.", this);
            }

            diagnosisActive = false;
            AllErrorsResolved?.Invoke();
            EmitSnapshot();
            return;
        }

        for (int i = 0; i < errorProcedures.Count; i++)
        {
            Dictionary<string, object> raw = errorProcedures[i];
            if (raw == null)
            {
                continue;
            }

            LtvError error = ParseError(raw);
            if (error == null || !error.NeedsResolved)
            {
                continue;
            }

            if (error.Procedures.Count == 0)
            {
                if (enableDebugLogs)
                {
                    Debug.Log(
                        $"[LTV] Skipping error {error.Code} ({error.Description}): no procedures defined.",
                        this
                    );
                }

                continue;
            }

            errorHeap.Insert(error);

            if (enableDebugLogs)
            {
                Debug.Log(
                    $"[LTV] Queued error {error.Code} ({error.Description}) " +
                    $"priority={error.Priority} criticality={error.Criticality} subsystem={error.SubsystemId} " +
                    $"steps={error.Procedures.Count}",
                    this
                );
            }
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[LTV] Diagnosis started. {errorHeap.Count} errors queued.", this);
        }

        PopNextError();
    }

    /// <summary>
    /// Advance to the next step in the current error's procedure.
    /// After the last step, initiates TSS verification.
    /// </summary>
    public void AdvanceStep()
    {
        if (currentError == null || verifying)
        {
            return;
        }

        int nextIndex = currentStepIndex + 1;

        if (nextIndex >= currentError.Procedures.Count)
        {
            if (enableDebugLogs)
            {
                Debug.Log(
                    $"[LTV] All steps completed for error {currentError.Code}. Verifying with TSS...",
                    this
                );
            }

            StartVerification();
            return;
        }

        currentStepIndex = nextIndex;

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[LTV] Step {currentStepIndex + 1}/{currentError.Procedures.Count} " +
                $"for error {currentError.Code}: {currentError.Procedures[currentStepIndex]}",
                this
            );
        }

        StepChanged?.Invoke(currentError, currentStepIndex);
        EmitSnapshot();
    }

    /// <summary>
    /// Alias for AdvanceStep. Kept for LtvInstructionDebugPanel compatibility.
    /// Returns true if the step was advanced.
    /// </summary>
    public bool MarkCurrentStepDone()
    {
        if (currentError == null || verifying)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning("[LTV] MarkCurrentStepDone ignored: no active step or verifying.", this);
            }

            return false;
        }

        AdvanceStep();
        return true;
    }

    /// <summary>
    /// Returns the current instruction state as a dictionary.
    /// Compatible with LtvInstructionDebugPanel.
    /// </summary>
    public Dictionary<string, object> GetCurrentInstruction()
    {
        return BuildSnapshot();
    }

    /// <summary>
    /// Returns the current state as a dictionary. Same as GetCurrentInstruction.
    /// Compatible with LtvErrorQueueService consumers.
    /// </summary>
    public Dictionary<string, object> GetCurrentSnapshot()
    {
        return BuildSnapshot();
    }

    /// <summary>
    /// Forces a snapshot emission. For debug panel compatibility.
    /// </summary>
    public void RefreshNow()
    {
        EmitSnapshot();
    }

    /// <summary>
    /// Stops all diagnosis activity and resets state.
    /// </summary>
    public void StopDiagnosis()
    {
        StopVerification();
        errorHeap.Clear();
        currentError = null;
        currentStepIndex = 0;
        retryCount = 0;
        diagnosisActive = false;

        if (enableDebugLogs)
        {
            Debug.Log("[LTV] Diagnosis stopped.", this);
        }

        EmitSnapshot();
    }

    private void PopNextError()
    {
        if (errorHeap.Count == 0)
        {
            currentError = null;
            currentStepIndex = 0;
            diagnosisActive = false;

            if (enableDebugLogs)
            {
                Debug.Log("[LTV] All errors resolved.", this);
            }

            AllErrorsResolved?.Invoke();
            EmitSnapshot();
            return;
        }

        currentError = errorHeap.ExtractMax();
        currentStepIndex = 0;
        retryCount = 0;

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[LTV] Now working on error {currentError.Code} ({currentError.Description}) " +
                $"priority={currentError.Priority}, steps={currentError.Procedures.Count}, " +
                $"remaining={errorHeap.Count}",
                this
            );
        }

        ErrorChanged?.Invoke(currentError);
        StepChanged?.Invoke(currentError, currentStepIndex);
        EmitSnapshot();
    }

    private void StartVerification()
    {
        if (verificationCoroutine != null)
        {
            StopCoroutine(verificationCoroutine);
            verificationCoroutine = null;
        }

        verifying = true;
        EmitSnapshot();
        verificationCoroutine = StartCoroutine(VerifyResolution());
    }

    private void StopVerification()
    {
        if (verificationCoroutine != null)
        {
            StopCoroutine(verificationCoroutine);
            verificationCoroutine = null;
        }

        verifying = false;
    }

    private IEnumerator VerifyResolution()
    {
        LtvError errorToVerify = currentError;
        WaitForSeconds wait = new WaitForSeconds(verificationPollSeconds);

        // Keep polling until the error is resolved or diagnosis is stopped.
        // No fixed timeout — the astronaut turns off the error on the server,
        // and we detect it whenever TSS reports the flag as false.
        while (diagnosisActive)
        {
            if (tssApi == null || errorToVerify == null)
            {
                break;
            }

            bool stillActive = IsErrorStillActive(errorToVerify);

            if (enableDebugLogs)
            {
                string errorKey = MapCodeToErrorKey(errorToVerify.Code);
                Debug.Log(
                    $"[LTV] Verification poll: error={errorToVerify.Code} key={errorKey} stillActive={stillActive}",
                    this
                );
            }

            if (!stillActive)
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[LTV] Error {errorToVerify.Code} verified resolved by TSS.", this);
                }

                verifying = false;
                verificationCoroutine = null;
                PopNextError();
                yield break;
            }

            yield return wait;
        }

        verifying = false;
        verificationCoroutine = null;
    }

    private void HandleResolutionFailed()
    {
        if (currentError == null)
        {
            return;
        }

        retryCount++;

        if (retryCount > maxRetries)
        {
            if (enableDebugLogs)
            {
                Debug.LogError(
                    $"[LTV] Error {currentError.Code} exceeded max retries ({maxRetries}). " +
                    "Skipping to next error.",
                    this
                );
            }

            MaxRetriesExceeded?.Invoke(currentError);
            PopNextError();
            return;
        }

        if (enableDebugLogs)
        {
            Debug.LogWarning(
                $"[LTV] Error {currentError.Code} NOT resolved after completing all steps. " +
                $"Retry {retryCount}/{maxRetries}. Re-showing all instructions.",
                this
            );
        }

        currentStepIndex = 0;
        ResolutionFailed?.Invoke(currentError);
        StepChanged?.Invoke(currentError, currentStepIndex);
        EmitSnapshot();
    }

    private bool IsErrorStillActive(LtvError error)
    {
        if (tssApi == null || error == null)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning("[LTV] Cannot verify error: TSS or error is null. Assuming still active.", this);
            }

            return true;
        }

        string errorKey = MapCodeToErrorKey(error.Code);
        if (string.IsNullOrEmpty(errorKey))
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning($"[LTV] No TSS key mapping for code {error.Code}. Assuming still active.", this);
            }

            return true;
        }

        Dictionary<string, object> errors = tssApi.GetLtvErrors();
        if (errors == null || errors.Count == 0)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning(
                    $"[LTV] TSS returned null/empty errors dict. Checking error_procedures fallback.",
                    this
                );
            }

            // Fallback: check error_procedures for needs_resolved flag
            return IsErrorInProceduresList(error);
        }

        if (!errors.TryGetValue(errorKey, out object value))
        {
            if (enableDebugLogs)
            {
                string availableKeys = string.Join(", ", errors.Keys);
                Debug.LogWarning(
                    $"[LTV] TSS errors missing key '{errorKey}'. Available keys: [{availableKeys}]. " +
                    "Falling back to error_procedures check.",
                    this
                );
            }

            // Fallback: check error_procedures for needs_resolved flag
            return IsErrorInProceduresList(error);
        }

        bool result = ToBool(value);
        if (enableDebugLogs)
        {
            Debug.Log($"[LTV] Error flag '{errorKey}' = {value} (parsed as {result})", this);
        }

        return result;
    }

    /// <summary>
    /// Fallback verification: re-fetches error_procedures from TSS and checks
    /// if this error's needs_resolved is still true.
    /// </summary>
    private bool IsErrorInProceduresList(LtvError error)
    {
        if (tssApi == null || error == null)
        {
            return true;
        }

        List<Dictionary<string, object>> procedures = tssApi.GetLtvErrorProcedures();
        for (int i = 0; i < procedures.Count; i++)
        {
            Dictionary<string, object> proc = procedures[i];
            string code = proc.ContainsKey("code") ? Convert.ToString(proc["code"]) : string.Empty;
            if (code == error.Code)
            {
                bool needsResolved = proc.ContainsKey("needs_resolved") && ToBool(proc["needs_resolved"]);
                if (enableDebugLogs)
                {
                    Debug.Log($"[LTV] Fallback check: error {code} needs_resolved={needsResolved}", this);
                }

                return needsResolved;
            }
        }

        // Error not found in procedures list at all — treat as resolved
        if (enableDebugLogs)
        {
            Debug.Log($"[LTV] Fallback check: error {error.Code} not found in procedures list. Treating as resolved.", this);
        }

        return false;
    }

    private Dictionary<string, object> BuildSnapshot()
    {
        if (currentError == null)
        {
            return new Dictionary<string, object>
            {
                { "has_active_error", false },
                { "active", false },
                { "error_key", string.Empty },
                { "error_code", string.Empty },
                { "error_description", string.Empty },
                { "procedure_id", string.Empty },
                { "procedure_complete", true },
                { "priority", 0 },
                { "next_step_id", string.Empty },
                { "next_step_has_criteria", false },
                { "next_instruction", diagnosisActive ? "Verifying..." : "No active LTV errors." },
                { "hint", string.Empty },
                { "voice_short", string.Empty },
                { "current_step_index", -1 },
                { "total_steps", 0 },
                { "remaining_errors", errorHeap.Count },
                { "verifying", verifying },
                { "retry_count", retryCount }
            };
        }

        string instruction = string.Empty;
        if (currentStepIndex >= 0 && currentStepIndex < currentError.Procedures.Count)
        {
            instruction = currentError.Procedures[currentStepIndex];
        }

        string errorKey = MapCodeToErrorKey(currentError.Code);
        bool isLastStep = currentStepIndex >= currentError.Procedures.Count - 1;

        return new Dictionary<string, object>
        {
            { "has_active_error", true },
            { "active", true },
            { "error_key", errorKey },
            { "error_code", currentError.Code },
            { "error_description", currentError.Description },
            { "procedure_id", errorKey },
            { "procedure_complete", false },
            { "priority", currentError.Priority },
            { "criticality", currentError.Criticality },
            { "subsystem_id", currentError.SubsystemId },
            { "next_step_id", $"step-{currentStepIndex + 1}" },
            { "next_step_has_criteria", isLastStep },
            { "next_instruction", verifying ? $"Verifying error {currentError.Code} resolved with TSS..." : instruction },
            { "hint", string.Empty },
            { "voice_short", instruction },
            { "current_step_index", currentStepIndex },
            { "current_instruction", instruction },
            { "total_steps", currentError.Procedures.Count },
            { "remaining_errors", errorHeap.Count },
            { "verifying", verifying },
            { "retry_count", retryCount }
        };
    }

    private void EmitSnapshot()
    {
        Dictionary<string, object> snapshot = BuildSnapshot();
        InstructionUpdated?.Invoke(snapshot);
    }

    private static LtvError ParseError(Dictionary<string, object> raw)
    {
        if (raw == null)
        {
            return null;
        }

        string code = raw.ContainsKey("code") ? Convert.ToString(raw["code"]) : "0000";
        string description = raw.ContainsKey("description") ? Convert.ToString(raw["description"]) : string.Empty;

        bool needsResolved = false;
        if (raw.ContainsKey("needs_resolved"))
        {
            needsResolved = ToBool(raw["needs_resolved"]);
        }

        List<string> procedures = new List<string>();
        if (raw.ContainsKey("procedures") && raw["procedures"] is List<object> rawList)
        {
            for (int i = 0; i < rawList.Count; i++)
            {
                procedures.Add(Convert.ToString(rawList[i]));
            }
        }

        return new LtvError(code, description, needsResolved, procedures);
    }

    private static string MapCodeToErrorKey(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        switch (code)
        {
            case "0000": return "recovery_mode";
            case "4155": return "power_distribution";
            case "4761": return "dust_sensor";
            case "2129": return "fuse";
            case "2130": return "nav_system";
            case "2131": return "lidar_system";
            case "2132": return "comms";
            case "3700": return "electronic_heater";
            case "2900": return "power_subsystem_bus";
            default: return string.Empty;
        }
    }

    private static bool ToBool(object value)
    {
        if (value == null)
        {
            return false;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (value is string strValue)
        {
            if (bool.TryParse(strValue, out bool parsed))
            {
                return parsed;
            }

            if (double.TryParse(strValue, out double parsedNumber))
            {
                return Math.Abs(parsedNumber) > double.Epsilon;
            }
        }

        if (value is IConvertible convertible)
        {
            try
            {
                return Math.Abs(convertible.ToDouble(null)) > double.Epsilon;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
