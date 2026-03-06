using System;
using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;

public class LtvInstructionService : MonoBehaviour
{
    [Serializable]
    private class ErrorProcedureRule
    {
        public string errorKey = "recovery_mode";
        public string procedureId = "erm";
        public int priority = 100;
        public bool enabled = true;
    }

    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;

    [Header("Polling")]
    [SerializeField] private bool autoRefresh = true;
    [SerializeField] private float pollIntervalSeconds = 0.25f;

    [Header("Priority Source")]
    [SerializeField] private bool preferProcedurePriority = true;

    [Header("Workflow")]
    [SerializeField] private bool strictSingleErrorFlow = true;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;

    [Header("LTV Error -> Procedure")]
    [SerializeField] private List<ErrorProcedureRule> rules = new List<ErrorProcedureRule>();

    public event Action<Dictionary<string, object>> InstructionUpdated;

    private Coroutine pollCoroutine;
    private string lastFingerprint = string.Empty;
    private Dictionary<string, object> currentInstruction = new Dictionary<string, object>();
    private readonly Dictionary<string, int> procedurePriorityCache = new Dictionary<string, int>();
    private readonly Dictionary<string, HashSet<string>> manualCompletedStepsByProcedure =
        new Dictionary<string, HashSet<string>>();
    private readonly HashSet<string> completedProceduresWhileErrorActive =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private ErrorProcedureRule lockedRule;
    private int lockedPriority;

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

        if (rules.Count == 0)
        {
            LoadDefaultRules();
        }

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[LTV] Service ready. TssApi linked={tssApi != null}, " +
                $"autoRefresh={autoRefresh}, pollInterval={pollIntervalSeconds:0.00}s, " +
                $"strictFlow={strictSingleErrorFlow}, rules={rules.Count}",
                this
            );
        }
    }

    private void OnEnable()
    {
        if (!autoRefresh)
        {
            return;
        }

        RefreshNow();
        pollCoroutine = StartCoroutine(PollLoop());
    }

    private void OnDisable()
    {
        if (pollCoroutine != null)
        {
            StopCoroutine(pollCoroutine);
            pollCoroutine = null;
        }
    }

    public Dictionary<string, object> GetCurrentInstruction()
    {
        return new Dictionary<string, object>(currentInstruction);
    }

    // UI trigger: marks the currently displayed manual step as complete.
    // Returns false when there is no active step or when the step is criteria-gated.
    public bool MarkCurrentStepDone()
    {
        string procedureId = Convert.ToString(GetPath(currentInstruction, "procedure_id", string.Empty));
        string stepId = Convert.ToString(GetPath(currentInstruction, "next_step_id", string.Empty));
        bool hasCriteria = ToBool(GetPath(currentInstruction, "next_step_has_criteria", false));

        if (string.IsNullOrEmpty(procedureId) || string.IsNullOrEmpty(stepId))
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning("[LTV] MarkCurrentStepDone ignored: no active step.", this);
            }

            return false;
        }

        if (hasCriteria)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning(
                    $"[LTV] MarkCurrentStepDone ignored: step {stepId} is criteria-gated; waiting for telemetry condition.",
                    this
                );
            }

            return false;
        }

        return MarkStepDone(procedureId, stepId);
    }

    // UI trigger: mark a specific step complete.
    public bool MarkStepDone(string procedureId, string stepId)
    {
        string normalizedProcedureId = (procedureId ?? string.Empty).Trim();
        string normalizedStepId = (stepId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedProcedureId) || string.IsNullOrEmpty(normalizedStepId))
        {
            return false;
        }

        HashSet<string> completed = GetOrCreateManualCompletedSet(normalizedProcedureId);
        bool added = completed.Add(normalizedStepId);

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[LTV] MarkStepDone procedure={normalizedProcedureId} step={normalizedStepId} added={added}",
                this
            );
        }

        RefreshNow();
        return added;
    }

    // Optional utility for UI reset button.
    public void ResetManualProgress(string procedureId = null)
    {
        string normalizedProcedureId = (procedureId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedProcedureId))
        {
            manualCompletedStepsByProcedure.Clear();
            completedProceduresWhileErrorActive.Clear();
            UnlockRule("manual reset all");
            if (enableDebugLogs)
            {
                Debug.Log("[LTV] Reset manual progress for all procedures.", this);
            }
        }
        else
        {
            manualCompletedStepsByProcedure.Remove(normalizedProcedureId);
            completedProceduresWhileErrorActive.Remove(normalizedProcedureId);
            if (lockedRule != null && string.Equals(lockedRule.procedureId, normalizedProcedureId, StringComparison.OrdinalIgnoreCase))
            {
                UnlockRule("manual reset procedure");
            }
            if (enableDebugLogs)
            {
                Debug.Log($"[LTV] Reset manual progress for procedure={normalizedProcedureId}.", this);
            }
        }

        RefreshNow();
    }

    public void RefreshNow()
    {
        if (tssApi == null)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning("[LTV] Refresh skipped: TssUnityApiService is null.", this);
            }
            return;
        }

        Dictionary<string, object> next = BuildInstructionSnapshot();
        string fingerprint = BuildFingerprint(next);
        currentInstruction = next;

        if (fingerprint == lastFingerprint)
        {
            return;
        }

        lastFingerprint = fingerprint;
        InstructionUpdated?.Invoke(new Dictionary<string, object>(currentInstruction));

        if (enableDebugLogs)
        {
            string errorKey = Convert.ToString(GetPath(currentInstruction, "error_key", string.Empty));
            string procedureId = Convert.ToString(GetPath(currentInstruction, "procedure_id", string.Empty));
            string nextInstruction = Convert.ToString(GetPath(currentInstruction, "next_instruction", string.Empty));
            bool hasActiveError = ToBool(GetPath(currentInstruction, "has_active_error", false));
            int priority = 0;
            TryToInt(GetPath(currentInstruction, "priority", 0), out priority);
            bool procedureComplete = ToBool(GetPath(currentInstruction, "procedure_complete", false));

            Debug.Log(
                $"[LTV] Update hasError={hasActiveError} error={errorKey} procedure={procedureId} " +
                $"priority={priority} complete={procedureComplete} next=\"{nextInstruction}\"",
                this
            );
        }
    }

    private IEnumerator PollLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.05f, pollIntervalSeconds));
        while (true)
        {
            RefreshNow();
            yield return wait;
        }
    }

    private Dictionary<string, object> BuildInstructionSnapshot()
    {
        Dictionary<string, object> errors = tssApi.GetLtvErrors();
        PruneCompletedSuppressions(errors);

        ErrorProcedureRule selectedRule = null;
        int selectedPriority = 0;

        if (strictSingleErrorFlow && lockedRule != null)
        {
            selectedRule = lockedRule;
            selectedPriority = lockedPriority;
        }
        else
        {
            selectedRule = SelectHighestPriorityActiveRule(errors, out selectedPriority);
            if (strictSingleErrorFlow && selectedRule != null)
            {
                LockRule(selectedRule, selectedPriority);
            }
        }

        int selectionGuard = 0;
        while (selectedRule != null)
        {
            Dictionary<string, object> status = EvaluateProcedureProgress(selectedRule.procedureId) ?? new Dictionary<string, object>();
            bool procedureComplete = ToBool(GetPath(status, "complete", false));

            if (strictSingleErrorFlow && IsProcedure(selectedRule, "erm"))
            {
                if (TryBuildErmDelegatedSnapshot(
                    selectedRule,
                    selectedPriority,
                    status,
                    errors,
                    out Dictionary<string, object> delegatedSnapshot))
                {
                    return delegatedSnapshot;
                }
            }

            if (!strictSingleErrorFlow || !procedureComplete)
            {
                return BuildActiveSnapshot(selectedRule, selectedPriority, status);
            }

            completedProceduresWhileErrorActive.Add(selectedRule.procedureId ?? string.Empty);
            if (enableDebugLogs)
            {
                Debug.Log(
                    $"[LTV] Procedure complete for error={selectedRule.errorKey}, procedure={selectedRule.procedureId}. Moving to next active error.",
                    this
                );
            }

            UnlockRule("procedure complete");
            selectedRule = SelectHighestPriorityActiveRule(errors, out selectedPriority);
            if (strictSingleErrorFlow && selectedRule != null)
            {
                LockRule(selectedRule, selectedPriority);
            }

            selectionGuard++;
            if (selectionGuard > rules.Count + 2)
            {
                if (enableDebugLogs)
                {
                    Debug.LogWarning("[LTV] Strict-flow selection guard reached. Falling back to idle snapshot.", this);
                }

                break;
            }
        }

        return BuildNoActiveSnapshot();
    }

    private Dictionary<string, object> BuildNoActiveSnapshot()
    {
        return new Dictionary<string, object>
        {
            { "has_active_error", false },
            { "error_key", string.Empty },
            { "procedure_id", string.Empty },
            { "priority", 0 },
            { "procedure_complete", true },
            { "next_step_id", string.Empty },
            { "next_step_has_criteria", false },
            { "next_instruction", "No active LTV errors." },
            { "hint", string.Empty },
            { "voice_short", string.Empty },
            { "strict_flow", strictSingleErrorFlow },
            { "locked_error_key", string.Empty },
            { "locked_procedure_id", string.Empty }
        };
    }

    private Dictionary<string, object> BuildActiveSnapshot(
        ErrorProcedureRule selectedRule,
        int selectedPriority,
        Dictionary<string, object> status)
    {
        Dictionary<string, object> nextStep = GetPath(status, "next_step", null) as Dictionary<string, object>;
        return new Dictionary<string, object>
        {
            { "has_active_error", true },
            { "error_key", selectedRule.errorKey },
            { "procedure_id", selectedRule.procedureId },
            { "priority", selectedPriority },
            { "procedure_complete", ToBool(GetPath(status, "complete", false)) },
            { "next_step_id", Convert.ToString(GetPath(nextStep, "id", string.Empty)) },
            { "next_step_has_criteria", ToBool(GetPath(nextStep, "has_criteria", false)) },
            { "next_instruction", Convert.ToString(GetPath(nextStep, "instruction", "Follow procedure steps.")) },
            { "hint", Convert.ToString(GetPath(nextStep, "hint", string.Empty)) },
            { "voice_short", Convert.ToString(GetPath(nextStep, "voice_short", string.Empty)) },
            { "strict_flow", strictSingleErrorFlow },
            { "locked_error_key", lockedRule != null ? lockedRule.errorKey : string.Empty },
            { "locked_procedure_id", lockedRule != null ? lockedRule.procedureId : string.Empty }
        };
    }

    // When ERM is active, if the current ERM step is blocked by an active ltv.errors.* condition,
    // surface that error's dedicated procedure as the current instruction stream.
    private bool TryBuildErmDelegatedSnapshot(
        ErrorProcedureRule ermRule,
        int ermPriority,
        Dictionary<string, object> ermStatus,
        Dictionary<string, object> errors,
        out Dictionary<string, object> snapshot)
    {
        snapshot = null;

        if (!TryGetBlockingErrorKeyFromErmStatus(ermStatus, errors, out string blockingErrorKey))
        {
            return false;
        }

        ErrorProcedureRule dependencyRule = FindRuleByErrorKey(blockingErrorKey);
        if (dependencyRule == null || IsProcedure(dependencyRule, "erm"))
        {
            return false;
        }

        int dependencyPriority = ResolvePriority(dependencyRule);
        Dictionary<string, object> dependencyStatus = EvaluateProcedureProgress(dependencyRule.procedureId) ?? new Dictionary<string, object>();
        bool dependencyComplete = ToBool(GetPath(dependencyStatus, "complete", false));
        bool errorStillActive = ToBool(GetPath(errors, blockingErrorKey, false));

        Dictionary<string, object> dependencyNextStep = GetPath(dependencyStatus, "next_step", null) as Dictionary<string, object>;

        if (dependencyComplete && errorStillActive)
        {
            dependencyNextStep = new Dictionary<string, object>
            {
                { "id", "verify-" + blockingErrorKey },
                { "instruction", $"Verify ltv.errors.{blockingErrorKey} is false before returning to ERM." },
                { "criticality", "critical" },
                { "hint", "Check telemetry/state source and confirm the fault flag cleared." },
                { "voice_short", $"Verify {blockingErrorKey} fault cleared." },
                { "has_criteria", true },
                { "complete", false },
                {
                    "checks",
                    new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            { "path", "ltv.errors." + blockingErrorKey },
                            { "op", "eq" },
                            { "value", false },
                            { "pass", false }
                        }
                    }
                }
            };
        }

        if (enableDebugLogs)
        {
            string delegatedStepId = Convert.ToString(GetPath(dependencyNextStep, "id", string.Empty));
            Debug.Log(
                $"[LTV] ERM blocked by error={blockingErrorKey}. Delegating to procedure={dependencyRule.procedureId}, step={delegatedStepId}",
                this
            );
        }

        snapshot = new Dictionary<string, object>
        {
            { "has_active_error", true },
            { "error_key", dependencyRule.errorKey },
            { "procedure_id", dependencyRule.procedureId },
            { "priority", dependencyPriority },
            { "procedure_complete", false },
            { "next_step_id", Convert.ToString(GetPath(dependencyNextStep, "id", string.Empty)) },
            { "next_step_has_criteria", ToBool(GetPath(dependencyNextStep, "has_criteria", false)) },
            { "next_instruction", Convert.ToString(GetPath(dependencyNextStep, "instruction", "Follow procedure steps.")) },
            { "hint", Convert.ToString(GetPath(dependencyNextStep, "hint", string.Empty)) },
            { "voice_short", Convert.ToString(GetPath(dependencyNextStep, "voice_short", string.Empty)) },
            { "strict_flow", strictSingleErrorFlow },
            { "locked_error_key", lockedRule != null ? lockedRule.errorKey : string.Empty },
            { "locked_procedure_id", lockedRule != null ? lockedRule.procedureId : string.Empty },
            { "parent_procedure_id", ermRule.procedureId },
            { "parent_priority", ermPriority },
            { "delegated_by_error", blockingErrorKey }
        };

        return true;
    }

    private bool TryGetBlockingErrorKeyFromErmStatus(
        Dictionary<string, object> ermStatus,
        Dictionary<string, object> errors,
        out string errorKey)
    {
        errorKey = string.Empty;
        Dictionary<string, object> nextStep = GetPath(ermStatus, "next_step", null) as Dictionary<string, object>;
        if (nextStep == null || !ToBool(GetPath(nextStep, "has_criteria", false)))
        {
            return false;
        }

        List<object> checks = GetPath(nextStep, "checks", null) as List<object> ?? new List<object>();
        for (int i = 0; i < checks.Count; i++)
        {
            Dictionary<string, object> check = checks[i] as Dictionary<string, object>;
            if (check == null)
            {
                continue;
            }

            bool pass = ToBool(GetPath(check, "pass", false));
            if (pass)
            {
                continue;
            }

            string path = Convert.ToString(GetPath(check, "path", string.Empty));
            string op = Convert.ToString(GetPath(check, "op", "eq"));
            bool targetFalse = !ToBool(GetPath(check, "value", true));
            if (!string.Equals(op, "eq", StringComparison.OrdinalIgnoreCase) || !targetFalse)
            {
                continue;
            }

            const string prefix = "ltv.errors.";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string candidate = path.Substring(prefix.Length);
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            if (ToBool(GetPath(errors, candidate, false)))
            {
                errorKey = candidate;
                return true;
            }
        }

        return false;
    }

    private ErrorProcedureRule FindRuleByErrorKey(string errorKey)
    {
        if (string.IsNullOrEmpty(errorKey))
        {
            return null;
        }

        for (int i = 0; i < rules.Count; i++)
        {
            ErrorProcedureRule candidate = rules[i];
            if (candidate == null || !candidate.enabled)
            {
                continue;
            }

            if (string.Equals(candidate.errorKey, errorKey, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsProcedure(ErrorProcedureRule rule, string procedureId)
    {
        if (rule == null)
        {
            return false;
        }

        return string.Equals(rule.procedureId, procedureId, StringComparison.OrdinalIgnoreCase);
    }

    private void LockRule(ErrorProcedureRule rule, int priority)
    {
        if (rule == null)
        {
            return;
        }

        lockedRule = rule;
        lockedPriority = priority;

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[LTV] Locked error flow to error={rule.errorKey}, procedure={rule.procedureId}, priority={priority}",
                this
            );
        }
    }

    private void UnlockRule(string reason)
    {
        if (lockedRule == null)
        {
            return;
        }

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[LTV] Unlocking error flow from error={lockedRule.errorKey}, procedure={lockedRule.procedureId}. reason={reason}",
                this
            );
        }

        lockedRule = null;
        lockedPriority = 0;
    }

    // Once a procedure is completed for a currently-active error, we suppress reselection of that
    // same procedure until that error goes inactive (which also resets manual progress for that procedure).
    private void PruneCompletedSuppressions(Dictionary<string, object> errors)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            ErrorProcedureRule rule = rules[i];
            if (rule == null || !rule.enabled || string.IsNullOrEmpty(rule.errorKey) || string.IsNullOrEmpty(rule.procedureId))
            {
                continue;
            }

            bool isActive = ToBool(GetPath(errors, rule.errorKey, false));
            bool isLockedProcedure = lockedRule != null &&
                string.Equals(lockedRule.procedureId, rule.procedureId, StringComparison.OrdinalIgnoreCase);
            if (isActive || isLockedProcedure)
            {
                continue;
            }

            if (completedProceduresWhileErrorActive.Remove(rule.procedureId) && enableDebugLogs)
            {
                Debug.Log(
                    $"[LTV] Error {rule.errorKey} is inactive; cleared completion suppression for procedure={rule.procedureId}.",
                    this
                );
            }

            manualCompletedStepsByProcedure.Remove(rule.procedureId);
        }
    }

    // Evaluates procedure progress locally so empty completion_criteria steps do not auto-complete.
    // Checkpoint behavior: criteria-backed steps are telemetry checkpoints; steps between checkpoints
    // are surfaced sequentially as manual instructions for the astronaut.
    private Dictionary<string, object> EvaluateProcedureProgress(string procedureId)
    {
        Dictionary<string, object> procedure = tssApi != null ? tssApi.GetLtvProcedure(procedureId) : null;
        if (procedure == null)
        {
            return null;
        }

        HashSet<string> manualCompleted = GetOrCreateManualCompletedSet(procedureId);
        Dictionary<string, object> mergedState = new Dictionary<string, object>
        {
            { "eva", tssApi.GetEva() },
            { "ltv", tssApi.GetLtv() }
        };

        List<object> stepsRaw = GetPath(procedure, "steps", null) as List<object> ?? new List<object>();
        List<Dictionary<string, object>> stepsMutable = new List<Dictionary<string, object>>();

        for (int i = 0; i < stepsRaw.Count; i++)
        {
            Dictionary<string, object> step = stepsRaw[i] as Dictionary<string, object>;
            if (step == null)
            {
                continue;
            }

            List<object> criteria = GetPath(step, "completion_criteria", null) as List<object> ?? new List<object>();
            List<object> checks = new List<object>();
            string stepId = Convert.ToString(GetPath(step, "id", string.Empty));
            bool hasCriteria = criteria.Count > 0;
            bool criteriaPass = hasCriteria;
            bool manuallyComplete = manualCompleted.Contains(stepId);

            if (hasCriteria)
            {
                for (int c = 0; c < criteria.Count; c++)
                {
                    Dictionary<string, object> criterion = criteria[c] as Dictionary<string, object>;
                    if (criterion == null)
                    {
                        continue;
                    }

                    bool passed = EvaluateCriterion(mergedState, criterion);
                    checks.Add(new Dictionary<string, object>
                    {
                        { "path", GetPath(criterion, "path", string.Empty) },
                        { "op", GetPath(criterion, "op", "eq") },
                        { "value", GetPath(criterion, "value", null) },
                        { "pass", passed }
                    });
                    criteriaPass = criteriaPass && passed;
                }
            }
            else
            {
                criteriaPass = false;
            }

            bool complete = hasCriteria ? criteriaPass : manuallyComplete;

            stepsMutable.Add(new Dictionary<string, object>
            {
                { "id", GetPath(step, "id", string.Empty) },
                { "instruction", GetPath(step, "instruction", string.Empty) },
                { "criticality", GetPath(step, "criticality", "optional") },
                { "hint", GetPath(step, "hint", string.Empty) },
                { "voice_short", GetPath(step, "voice_short", string.Empty) },
                { "checks", checks },
                { "has_criteria", hasCriteria },
                { "criteria_pass", criteriaPass },
                { "complete", complete }
            });
        }

        if (stepsMutable.Count == 0)
        {
            return new Dictionary<string, object>
            {
                { "procedure_id", procedureId },
                { "completed_steps", 0 },
                { "total_steps", 0 },
                { "complete", true },
                { "next_step", null },
                { "steps", new List<object>() }
            };
        }

        bool procedureComplete;
        int completedSteps = 0;
        Dictionary<string, object> nextStep = null;
        List<object> stepsOut = new List<object>();

        for (int i = 0; i < stepsMutable.Count; i++)
        {
            Dictionary<string, object> step = stepsMutable[i];
            bool complete = ToBool(GetPath(step, "complete", false));

            step["complete"] = complete;
            step.Remove("criteria_pass");
            stepsOut.Add(step);

            if (complete)
            {
                completedSteps++;
            }

            if (!complete && nextStep == null)
            {
                nextStep = step;
            }
        }

        procedureComplete = nextStep == null && stepsOut.Count > 0;

        return new Dictionary<string, object>
        {
            { "procedure_id", procedureId },
            { "completed_steps", completedSteps },
            { "total_steps", stepsOut.Count },
            { "complete", procedureComplete },
            { "next_step", nextStep },
            { "steps", stepsOut }
        };
    }

    private HashSet<string> GetOrCreateManualCompletedSet(string procedureId)
    {
        string key = (procedureId ?? string.Empty).Trim();
        if (!manualCompletedStepsByProcedure.TryGetValue(key, out HashSet<string> completed))
        {
            completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            manualCompletedStepsByProcedure[key] = completed;
        }

        return completed;
    }

    private ErrorProcedureRule SelectHighestPriorityActiveRule(Dictionary<string, object> errors, out int selectedPriority)
    {
        ErrorProcedureRule winner = null;
        selectedPriority = int.MinValue;

        for (int i = 0; i < rules.Count; i++)
        {
            ErrorProcedureRule candidate = rules[i];
            if (candidate == null || !candidate.enabled || string.IsNullOrEmpty(candidate.errorKey))
            {
                continue;
            }

            bool isActive = ToBool(GetPath(errors, candidate.errorKey, false));
            if (!isActive)
            {
                continue;
            }

            if (strictSingleErrorFlow &&
                !string.IsNullOrEmpty(candidate.procedureId) &&
                completedProceduresWhileErrorActive.Contains(candidate.procedureId))
            {
                continue;
            }

            int candidatePriority = ResolvePriority(candidate);
            if (winner == null || candidatePriority > selectedPriority)
            {
                winner = candidate;
                selectedPriority = candidatePriority;
            }
        }

        if (winner == null)
        {
            selectedPriority = 0;
        }

        return winner;
    }

    private int ResolvePriority(ErrorProcedureRule rule)
    {
        if (rule == null)
        {
            return 0;
        }

        int fallback = rule.priority;
        if (!preferProcedurePriority || string.IsNullOrEmpty(rule.procedureId))
        {
            return fallback;
        }

        if (procedurePriorityCache.TryGetValue(rule.procedureId, out int cached))
        {
            return cached;
        }

        int resolved = fallback;
        Dictionary<string, object> procedure = tssApi != null ? tssApi.GetLtvProcedure(rule.procedureId) : null;
        if (TryToInt(GetPath(procedure, "priority", null), out int parsed))
        {
            resolved = parsed;
        }

        procedurePriorityCache[rule.procedureId] = resolved;
        return resolved;
    }

    private void LoadDefaultRules()
    {
        rules = new List<ErrorProcedureRule>
        {
            new ErrorProcedureRule { errorKey = "recovery_mode", procedureId = "erm", priority = 100 },
            new ErrorProcedureRule { errorKey = "power_distribution", procedureId = "power_distribution_reset", priority = 90 },
            new ErrorProcedureRule { errorKey = "electronic_heater", procedureId = "electronic_heater_recover", priority = 80 },
            new ErrorProcedureRule { errorKey = "nav_system", procedureId = "nav_restart", priority = 70 },
            new ErrorProcedureRule { errorKey = "fuse", procedureId = "fuse_service", priority = 60 },
            new ErrorProcedureRule { errorKey = "comms", procedureId = "comms_recover", priority = 50 },
            new ErrorProcedureRule { errorKey = "power_subsystem_bus", procedureId = "power_subsystem_bus", priority = 40 },
            new ErrorProcedureRule { errorKey = "dust_sensor", procedureId = "dust_sensor_clean", priority = 20 }
        };
    }

    private static object GetPath(Dictionary<string, object> source, string path, object defaultValue)
    {
        if (source == null || string.IsNullOrEmpty(path))
        {
            return defaultValue;
        }

        object current = source;
        string[] parts = path.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            Dictionary<string, object> dict = current as Dictionary<string, object>;
            if (dict == null || !dict.TryGetValue(parts[i], out current))
            {
                return defaultValue;
            }
        }

        return current ?? defaultValue;
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
            if (bool.TryParse(strValue, out bool parsedBool))
            {
                return parsedBool;
            }

            if (double.TryParse(strValue, out double parsedNumber))
            {
                return Math.Abs(parsedNumber) > double.Epsilon;
            }

            return false;
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

    private static bool TryToInt(object value, out int parsed)
    {
        parsed = 0;
        if (value == null)
        {
            return false;
        }

        if (value is int intValue)
        {
            parsed = intValue;
            return true;
        }

        if (value is long longValue)
        {
            if (longValue > int.MaxValue || longValue < int.MinValue)
            {
                return false;
            }

            parsed = (int)longValue;
            return true;
        }

        if (value is double doubleValue)
        {
            if (doubleValue > int.MaxValue || doubleValue < int.MinValue)
            {
                return false;
            }

            parsed = (int)doubleValue;
            return true;
        }

        if (value is string strValue && int.TryParse(strValue, out int fromString))
        {
            parsed = fromString;
            return true;
        }

        return false;
    }

    private static bool EvaluateCriterion(Dictionary<string, object> state, Dictionary<string, object> criterion)
    {
        object value = GetPath(state, Convert.ToString(GetPath(criterion, "path", string.Empty)), null);
        string op = Convert.ToString(GetPath(criterion, "op", "eq"));
        object target = GetPath(criterion, "value", null);

        switch (op)
        {
            case "eq":
                return ValuesEqual(value, target);
            case "ne":
                return !ValuesEqual(value, target);
            case "gt":
                return CompareNumeric(value, target, out int gtResult) && gtResult > 0;
            case "gte":
                return CompareNumeric(value, target, out int gteResult) && gteResult >= 0;
            case "lt":
                return CompareNumeric(value, target, out int ltResult) && ltResult < 0;
            case "lte":
                return CompareNumeric(value, target, out int lteResult) && lteResult <= 0;
            default:
                return false;
        }
    }

    private static bool ValuesEqual(object a, object b)
    {
        if (a == null && b == null)
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (CompareNumeric(a, b, out int numericResult))
        {
            return numericResult == 0;
        }

        return string.Equals(Convert.ToString(a), Convert.ToString(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompareNumeric(object a, object b, out int result)
    {
        result = 0;
        if (TryToDouble(a, out double da) && TryToDouble(b, out double db))
        {
            result = da.CompareTo(db);
            return true;
        }

        return false;
    }

    private static bool TryToDouble(object value, out double result)
    {
        result = 0.0d;
        if (value == null)
        {
            return false;
        }

        try
        {
            if (value is bool boolValue)
            {
                result = boolValue ? 1.0d : 0.0d;
                return true;
            }

            if (value is IConvertible convertible)
            {
                result = convertible.ToDouble(null);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string BuildFingerprint(Dictionary<string, object> snapshot)
    {
        return string.Join("|",
            Convert.ToString(GetPath(snapshot, "has_active_error", false)),
            Convert.ToString(GetPath(snapshot, "error_key", string.Empty)),
            Convert.ToString(GetPath(snapshot, "procedure_id", string.Empty)),
            Convert.ToString(GetPath(snapshot, "locked_procedure_id", string.Empty)),
            Convert.ToString(GetPath(snapshot, "next_step_id", string.Empty)),
            Convert.ToString(GetPath(snapshot, "next_instruction", string.Empty)),
            Convert.ToString(GetPath(snapshot, "procedure_complete", false))
        );
    }
}
