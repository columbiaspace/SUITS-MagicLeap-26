using System.Collections.Generic;
using LtvDiagnostics;
using TssApi;
using UnityEngine;

public class LtvTestHarness : MonoBehaviour
{
    [Header("Service References (auto-found if empty)")]
    [SerializeField] private TssUnityApiService tssApi;
    [SerializeField] private LtvInstructionService instructionService;

    [Header("Connection Status (read-only)")]
    [SerializeField] private bool serverOnline;

    [Header("Diagnosis Status (read-only)")]
    [SerializeField] private bool diagnosisActive;
    [SerializeField] private string currentErrorCode;
    [SerializeField] private string currentErrorDescription;
    [SerializeField] private int currentPriority;
    [SerializeField] private int currentStepIndex;
    [SerializeField] private int totalSteps;
    [SerializeField] private string currentInstruction;
    [SerializeField] private int remainingErrors;
    [SerializeField] private bool isVerifying;
    [SerializeField] private int retryCount;

    [Header("Live Error Flags (read-only)")]
    [SerializeField] private bool recoveryMode;
    [SerializeField] private bool powerDistribution;
    [SerializeField] private bool electronicHeater;
    [SerializeField] private bool navSystem;
    [SerializeField] private bool fuse;
    [SerializeField] private bool comms;
    [SerializeField] private bool powerSubsystemBus;
    [SerializeField] private bool dustSensor;

    private void Awake()
    {
        FindServices();
    }

    private void OnEnable()
    {
        FindServices();

        if (instructionService != null)
        {
            instructionService.InstructionUpdated += OnInstructionUpdated;
            instructionService.ErrorChanged += OnErrorChanged;
            instructionService.StepChanged += OnStepChanged;
            instructionService.ResolutionFailed += OnResolutionFailed;
            instructionService.MaxRetriesExceeded += OnMaxRetriesExceeded;
            instructionService.AllErrorsResolved += OnAllErrorsResolved;
        }
    }

    private void OnDisable()
    {
        if (instructionService != null)
        {
            instructionService.InstructionUpdated -= OnInstructionUpdated;
            instructionService.ErrorChanged -= OnErrorChanged;
            instructionService.StepChanged -= OnStepChanged;
            instructionService.ResolutionFailed -= OnResolutionFailed;
            instructionService.MaxRetriesExceeded -= OnMaxRetriesExceeded;
            instructionService.AllErrorsResolved -= OnAllErrorsResolved;
        }
    }

    private void Update()
    {
        UpdateConnectionStatus();
        UpdateErrorFlags();
        UpdateDiagnosisStatus();
    }

    // --- Inspector Button Methods ---

    public void TriggerStartDiagnosis()
    {
        if (instructionService != null)
        {
            instructionService.StartDiagnosisFromTss();
            Debug.Log("[LTV-Test] Started diagnosis from TSS.");
        }
    }

    public void TriggerAdvanceStep()
    {
        if (instructionService != null)
        {
            bool result = instructionService.MarkCurrentStepDone();
            Debug.Log($"[LTV-Test] AdvanceStep result={result}");
        }
    }

    public void TriggerStopDiagnosis()
    {
        if (instructionService != null)
        {
            instructionService.StopDiagnosis();
            Debug.Log("[LTV-Test] Stopped diagnosis.");
        }
    }

    public void LogFullSnapshot()
    {
        if (instructionService != null)
        {
            Dictionary<string, object> snapshot = instructionService.GetCurrentSnapshot();
            Debug.Log("[LTV-Test] Full Snapshot:");
            foreach (KeyValuePair<string, object> kvp in snapshot)
            {
                Debug.Log($"  {kvp.Key} = {kvp.Value}");
            }
        }

        if (tssApi != null)
        {
            Dictionary<string, object> errors = tssApi.GetLtvErrors();
            Debug.Log("[LTV-Test] TSS LTV Error Flags:");
            foreach (KeyValuePair<string, object> kvp in errors)
            {
                Debug.Log($"  {kvp.Key} = {kvp.Value}");
            }

            List<Dictionary<string, object>> procs = tssApi.GetLtvErrorProcedures();
            Debug.Log($"[LTV-Test] TSS error_procedures count={procs.Count}");
            for (int i = 0; i < procs.Count; i++)
            {
                Dictionary<string, object> p = procs[i];
                string code = p.ContainsKey("code") ? System.Convert.ToString(p["code"]) : "?";
                string desc = p.ContainsKey("description") ? System.Convert.ToString(p["description"]) : "?";
                bool needs = p.ContainsKey("needs_resolved") && p["needs_resolved"] is bool b && b;
                int stepCount = 0;
                if (p.ContainsKey("procedures") && p["procedures"] is List<object> steps)
                {
                    stepCount = steps.Count;
                }

                Debug.Log($"  [{i}] code={code} desc={desc} needs_resolved={needs} steps={stepCount}");
            }
        }
    }

    /// <summary>
    /// End-to-end test: connects to TSS, fetches errors, validates priority ordering,
    /// and logs the full diagnosis flow.
    /// </summary>
    public void RunEndToEndTest()
    {
        Debug.Log("=== [LTV-E2E] Starting End-to-End Test ===");

        if (tssApi == null)
        {
            Debug.LogError("[LTV-E2E] FAIL: TssUnityApiService not found.");
            return;
        }

        // Step 1: Check server connectivity
        Dictionary<string, object> health = tssApi.GetHealth();
        bool online = health != null && health.ContainsKey("source_online") && health["source_online"] is bool b && b;
        Debug.Log($"[LTV-E2E] Server online: {online}");

        if (!online)
        {
            Debug.LogError("[LTV-E2E] FAIL: TSS server not reachable. Check host/port.");
            return;
        }

        // Step 2: Fetch error procedures
        List<Dictionary<string, object>> errorProcs = tssApi.GetLtvErrorProcedures();
        Debug.Log($"[LTV-E2E] Fetched {errorProcs.Count} error_procedures from TSS.");

        if (errorProcs.Count == 0)
        {
            Debug.LogWarning("[LTV-E2E] No error_procedures returned. Server may have no active errors.");
            return;
        }

        // Step 3: Parse and validate priority ordering
        MaxHeap<LtvError> testHeap = new MaxHeap<LtvError>();
        for (int i = 0; i < errorProcs.Count; i++)
        {
            Dictionary<string, object> raw = errorProcs[i];
            string code = raw.ContainsKey("code") ? System.Convert.ToString(raw["code"]) : "0000";
            string desc = raw.ContainsKey("description") ? System.Convert.ToString(raw["description"]) : "";
            bool needs = raw.ContainsKey("needs_resolved") && raw["needs_resolved"] is bool nb && nb;
            List<string> steps = new List<string>();
            if (raw.ContainsKey("procedures") && raw["procedures"] is List<object> rawSteps)
            {
                for (int j = 0; j < rawSteps.Count; j++)
                {
                    steps.Add(System.Convert.ToString(rawSteps[j]));
                }
            }

            LtvError error = new LtvError(code, desc, needs, steps);
            Debug.Log(
                $"[LTV-E2E] Error code={error.Code} desc={error.Description} " +
                $"priority={error.Priority} criticality={error.Criticality} " +
                $"subsystem={error.SubsystemId} needs_resolved={error.NeedsResolved} " +
                $"steps={error.Procedures.Count}"
            );

            if (needs && steps.Count > 0)
            {
                testHeap.Insert(error);
            }
        }

        Debug.Log($"[LTV-E2E] {testHeap.Count} actionable errors queued.");

        // Step 4: Validate priority ordering by extracting in order
        Debug.Log("[LTV-E2E] Priority order (highest first):");
        int order = 1;
        int lastPriority = int.MaxValue;
        bool orderCorrect = true;
        while (testHeap.Count > 0)
        {
            LtvError e = testHeap.ExtractMax();
            Debug.Log($"  {order}. [{e.Code}] {e.Description} (priority={e.Priority})");
            if (e.Priority > lastPriority)
            {
                orderCorrect = false;
                Debug.LogError($"[LTV-E2E] FAIL: Priority order violated! {e.Priority} > {lastPriority}");
            }

            lastPriority = e.Priority;
            order++;
        }

        if (orderCorrect)
        {
            Debug.Log("[LTV-E2E] PASS: Priority ordering is correct.");
        }

        // Step 5: Verify error flags are available
        Dictionary<string, object> errorFlags = tssApi.GetLtvErrors();
        Debug.Log($"[LTV-E2E] Error flags from TSS: {errorFlags.Count} keys");
        foreach (KeyValuePair<string, object> kvp in errorFlags)
        {
            Debug.Log($"  {kvp.Key} = {kvp.Value}");
        }

        // Step 6: Start actual diagnosis via service
        if (instructionService != null)
        {
            Debug.Log("[LTV-E2E] Starting diagnosis via LtvInstructionService...");
            instructionService.StartDiagnosisFromTss();

            Dictionary<string, object> snap = instructionService.GetCurrentSnapshot();
            Debug.Log($"[LTV-E2E] After start - active={snap["active"]} error_code={snap["error_code"]} " +
                      $"step={snap["current_step_index"]} instruction={snap["current_instruction"]}");

            Debug.Log("[LTV-E2E] PASS: Diagnosis started successfully.");
        }

        Debug.Log("=== [LTV-E2E] End-to-End Test Complete ===");
    }

    // --- Event Handlers ---

    private void OnInstructionUpdated(Dictionary<string, object> snapshot)
    {
        UpdateFromSnapshot(snapshot);
    }

    private void OnErrorChanged(LtvError error)
    {
        Debug.Log($"[LTV-Test] ErrorChanged: {error.Code} - {error.Description} (priority={error.Priority})");
    }

    private void OnStepChanged(LtvError error, int stepIndex)
    {
        string instruction = stepIndex < error.Procedures.Count ? error.Procedures[stepIndex] : "(done)";
        Debug.Log($"[LTV-Test] StepChanged: error={error.Code} step={stepIndex + 1}/{error.Procedures.Count}: {instruction}");
    }

    private void OnResolutionFailed(LtvError error)
    {
        Debug.LogWarning($"[LTV-Test] ResolutionFailed: error={error.Code} - TSS says still active. Retrying...");
    }

    private void OnMaxRetriesExceeded(LtvError error)
    {
        Debug.LogError($"[LTV-Test] MaxRetriesExceeded: error={error.Code} - Skipping to next.");
    }

    private void OnAllErrorsResolved()
    {
        Debug.Log("[LTV-Test] AllErrorsResolved: All LTV errors have been addressed.");
    }

    // --- Private Helpers ---

    private void FindServices()
    {
        if (tssApi == null)
        {
            tssApi = FindObjectOfType<TssUnityApiService>();
        }

        if (instructionService == null)
        {
            instructionService = FindObjectOfType<LtvInstructionService>();
        }
    }

    private void UpdateFromSnapshot(Dictionary<string, object> snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        currentErrorCode = GetString(snapshot, "error_code");
        currentErrorDescription = GetString(snapshot, "error_description");
        currentInstruction = GetString(snapshot, "current_instruction");
        if (string.IsNullOrEmpty(currentInstruction))
        {
            currentInstruction = GetString(snapshot, "next_instruction");
        }

        currentPriority = GetInt(snapshot, "priority");
    }

    private void UpdateConnectionStatus()
    {
        if (tssApi == null)
        {
            serverOnline = false;
            return;
        }

        Dictionary<string, object> health = tssApi.GetHealth();
        serverOnline = health != null && health.ContainsKey("source_online") && (health["source_online"] is bool b) && b;
    }

    private void UpdateErrorFlags()
    {
        if (tssApi == null)
        {
            return;
        }

        Dictionary<string, object> errors = tssApi.GetLtvErrors();
        recoveryMode = GetBool(errors, "recovery_mode");
        powerDistribution = GetBool(errors, "power_distribution");
        electronicHeater = GetBool(errors, "electronic_heater");
        navSystem = GetBool(errors, "nav_system");
        fuse = GetBool(errors, "fuse");
        comms = GetBool(errors, "comms");
        powerSubsystemBus = GetBool(errors, "power_subsystem_bus");
        dustSensor = GetBool(errors, "dust_sensor");
    }

    private void UpdateDiagnosisStatus()
    {
        if (instructionService == null)
        {
            diagnosisActive = false;
            return;
        }

        diagnosisActive = instructionService.IsDiagnosisActive;
        isVerifying = instructionService.IsVerifying;
        remainingErrors = instructionService.RemainingErrors;
        currentStepIndex = instructionService.CurrentStepIndex;
        retryCount = instructionService.RetryCount;

        LtvError current = instructionService.CurrentError;
        if (current != null)
        {
            currentErrorCode = current.Code;
            currentErrorDescription = current.Description;
            currentPriority = current.Priority;
            totalSteps = current.Procedures.Count;

            if (currentStepIndex >= 0 && currentStepIndex < current.Procedures.Count)
            {
                currentInstruction = current.Procedures[currentStepIndex];
            }
        }
        else
        {
            totalSteps = 0;
        }
    }

    private static string GetString(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object value) && value != null)
        {
            return System.Convert.ToString(value);
        }

        return string.Empty;
    }

    private static bool GetBool(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object value))
        {
            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (value is string strValue && bool.TryParse(strValue, out bool parsed))
            {
                return parsed;
            }
        }

        return false;
    }

    private static int GetInt(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object value))
        {
            if (value is int intValue)
            {
                return intValue;
            }

            if (value is long longValue && longValue <= int.MaxValue && longValue >= int.MinValue)
            {
                return (int)longValue;
            }

            if (value is double doubleValue && doubleValue <= int.MaxValue && doubleValue >= int.MinValue)
            {
                return (int)doubleValue;
            }
        }

        return 0;
    }
}
