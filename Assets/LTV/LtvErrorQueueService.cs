using System;
using System.Collections;
using System.Collections.Generic;
using LtvDiagnostics;
using TssApi;
using UnityEngine;

public class LtvErrorQueueService : MonoBehaviour
{
    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;

    [Header("Polling")]
    [SerializeField] private float verificationPollSeconds = 1.0f;
    [SerializeField] private float verificationTimeoutSeconds = 10.0f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;

    public event Action<LtvError, int> StepChanged;
    public event Action<LtvError> ErrorChanged;
    public event Action<LtvError> ResolutionFailed;
    public event Action AllErrorsResolved;

    private MaxHeap<LtvError> errorHeap = new MaxHeap<LtvError>();
    private LtvError currentError;
    private int currentStepIndex;
    private bool diagnosisActive;
    private bool verifying;
    private int retryCount;

    private Coroutine verificationCoroutine;

    public LtvError CurrentError => currentError;
    public int CurrentStepIndex => currentStepIndex;
    public bool IsDiagnosisActive => diagnosisActive;
    public bool IsVerifying => verifying;
    public int RemainingErrors => errorHeap.Count;
    public int RetryCount => retryCount;

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
    }

    public Dictionary<string, object> GetCurrentSnapshot()
    {
        if (currentError == null)
        {
            return new Dictionary<string, object>
            {
                { "active", false },
                { "error_code", string.Empty },
                { "error_description", string.Empty },
                { "current_step_index", -1 },
                { "current_instruction", "No active diagnostics." },
                { "total_steps", 0 },
                { "remaining_errors", 0 },
                { "verifying", false },
                { "resolution_failed", false },
                { "retry_count", 0 }
            };
        }

        string instruction = string.Empty;
        if (currentStepIndex >= 0 && currentStepIndex < currentError.Procedures.Count)
        {
            instruction = currentError.Procedures[currentStepIndex];
        }

        return new Dictionary<string, object>
        {
            { "active", true },
            { "error_code", currentError.Code },
            { "error_description", currentError.Description },
            { "priority", currentError.Priority },
            { "criticality", currentError.Criticality },
            { "subsystem_id", currentError.SubsystemId },
            { "current_step_index", currentStepIndex },
            { "current_instruction", instruction },
            { "total_steps", currentError.Procedures.Count },
            { "remaining_errors", errorHeap.Count },
            { "verifying", verifying },
            { "resolution_failed", false },
            { "retry_count", retryCount }
        };
    }

    public void StartDiagnosis(List<Dictionary<string, object>> errorProceduresRaw)
    {
        StopVerification();
        errorHeap.Clear();
        currentError = null;
        currentStepIndex = 0;
        retryCount = 0;
        diagnosisActive = true;

        if (errorProceduresRaw == null)
        {
            if (enableDebugLogs)
            {
                Debug.Log("[LTV-Queue] StartDiagnosis called with null data.");
            }

            diagnosisActive = false;
            AllErrorsResolved?.Invoke();
            return;
        }

        for (int i = 0; i < errorProceduresRaw.Count; i++)
        {
            Dictionary<string, object> raw = errorProceduresRaw[i];
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
                    Debug.Log($"[LTV-Queue] Skipping error {error.Code} ({error.Description}): no procedures defined.");
                }

                continue;
            }

            errorHeap.Insert(error);

            if (enableDebugLogs)
            {
                Debug.Log(
                    $"[LTV-Queue] Queued error {error.Code} ({error.Description}) " +
                    $"priority={error.Priority} criticality={error.Criticality} subsystem={error.SubsystemId} " +
                    $"steps={error.Procedures.Count}"
                );
            }
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[LTV-Queue] Diagnosis started. {errorHeap.Count} errors queued.");
        }

        PopNextError();
    }

    public void StartDiagnosisFromTss()
    {
        if (tssApi == null)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning("[LTV-Queue] Cannot start diagnosis: TssUnityApiService is null.");
            }

            return;
        }

        List<Dictionary<string, object>> errorProcedures = tssApi.GetLtvErrorProcedures();
        StartDiagnosis(errorProcedures);
    }

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
                    $"[LTV-Queue] All steps completed for error {currentError.Code}. " +
                    "Verifying resolution with TSS..."
                );
            }

            StartVerification();
            return;
        }

        currentStepIndex = nextIndex;

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[LTV-Queue] Step {currentStepIndex + 1}/{currentError.Procedures.Count} " +
                $"for error {currentError.Code}: {currentError.Procedures[currentStepIndex]}"
            );
        }

        StepChanged?.Invoke(currentError, currentStepIndex);
    }

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
            Debug.Log("[LTV-Queue] Diagnosis stopped.");
        }
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
                Debug.Log("[LTV-Queue] All errors resolved.");
            }

            AllErrorsResolved?.Invoke();
            return;
        }

        currentError = errorHeap.ExtractMax();
        currentStepIndex = 0;
        retryCount = 0;

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[LTV-Queue] Now working on error {currentError.Code} ({currentError.Description}) " +
                $"priority={currentError.Priority}, steps={currentError.Procedures.Count}, " +
                $"remaining={errorHeap.Count}"
            );
        }

        ErrorChanged?.Invoke(currentError);
        StepChanged?.Invoke(currentError, currentStepIndex);
    }

    private void StartVerification()
    {
        verifying = true;
        StopVerification();
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
        float elapsed = 0f;
        WaitForSeconds wait = new WaitForSeconds(verificationPollSeconds);

        while (elapsed < verificationTimeoutSeconds)
        {
            if (tssApi == null)
            {
                break;
            }

            bool stillActive = IsErrorStillActive(currentError);

            if (!stillActive)
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[LTV-Queue] Error {currentError.Code} verified resolved by TSS.");
                }

                verifying = false;
                verificationCoroutine = null;
                PopNextError();
                yield break;
            }

            elapsed += verificationPollSeconds;
            yield return wait;
        }

        verifying = false;
        verificationCoroutine = null;
        HandleResolutionFailed();
    }

    private void HandleResolutionFailed()
    {
        retryCount++;

        if (enableDebugLogs)
        {
            Debug.LogWarning(
                $"[LTV-Queue] Error {currentError.Code} NOT resolved after completing all steps. " +
                $"Retry #{retryCount}. Re-showing all instructions."
            );
        }

        currentStepIndex = 0;
        ResolutionFailed?.Invoke(currentError);
        StepChanged?.Invoke(currentError, currentStepIndex);
    }

    private bool IsErrorStillActive(LtvError error)
    {
        if (tssApi == null || error == null)
        {
            return false;
        }

        string errorKey = MapCodeToErrorKey(error.Code);
        if (string.IsNullOrEmpty(errorKey))
        {
            return false;
        }

        Dictionary<string, object> errors = tssApi.GetLtvErrors();
        if (errors == null)
        {
            return false;
        }

        if (!errors.TryGetValue(errorKey, out object value))
        {
            return false;
        }

        return ToBool(value);
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
            default: return string.Empty;
        }
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
