using System;
using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;

/// <summary>
/// Polls the TSS EVA telemetry packet and grades every configured vital against
/// min/max/nominal thresholds per the EVA Telemetry Ranges spec. Fires
/// VitalsAlertChanged with the full list of active alerts (critical-first) so
/// the WarningCanvasUI can display all of them simultaneously.
/// </summary>
public class TssVitalsOxygenMonitor : MonoBehaviour
{
    public enum OxygenState
    {
        Unknown,
        Good,
        RunningLow,
        CriticallyLow
    }

    public enum AlertDirection
    {
        /// <summary>Trigger when value drops at/below threshold (oxygen, battery, coolant…).</summary>
        Low,
        /// <summary>Trigger when value rises at/above threshold (heart rate, CO2, temperature…).</summary>
        High,
        /// <summary>Trigger when value falls outside a min/max band. Low-side uses warningThreshold/criticalThreshold; high-side uses warningThresholdHigh/criticalThresholdHigh.</summary>
        Range
    }

    [Serializable]
    public class VitalRule
    {
        [Tooltip("Display name used in the warning banner, e.g. 'O2', 'BATTERY', 'CO2'.")]
        public string label;

        [Tooltip("Dotted path inside the EVA packet, e.g. telemetry.eva1.heart_rate")]
        public string path;

        [Tooltip("Optional second path. Used if the first is missing or, for Low rules, to take the lowest value.")]
        public string secondaryPath;

        [Tooltip("Suffix appended to the value in the warning text (%, psi, bpm, °C).")]
        public string unit = "%";

        [Tooltip("Whether the alert fires when value goes Low (≤), High (≥), or outside a Range.")]
        public AlertDirection direction = AlertDirection.Low;

        [Tooltip("Yellow-warning threshold (low-side for Range).")]
        public float warningThreshold = 30f;

        [Tooltip("Red-critical threshold (low-side for Range).")]
        public float criticalThreshold = 15f;

        [Tooltip("For Range direction: high-side yellow-warning threshold.")]
        public float warningThresholdHigh;

        [Tooltip("For Range direction: high-side red-critical threshold.")]
        public float criticalThresholdHigh;

        [Tooltip("Actionable procedure shown in the warning headline (e.g. 'Swap to secondary fan (DCU).').")]
        public string actionMessage;

        [Tooltip("For Range direction: alternate action message shown when the low side triggers. Leave empty to use actionMessage for both sides.")]
        public string actionMessageLow;

        [Tooltip("Format string for the value, e.g. F1 → '12.3'.")]
        public string valueFormat = "F1";

        [Tooltip("If the rule is disabled it is skipped entirely.")]
        public bool enabled = true;
    }

    public struct VitalsAlert
    {
        public OxygenState State;
        public string Headline;
        public VitalRule Rule;
        public float Value;
    }

    public static bool IsCriticalOxygenLow { get; private set; }
    public static event Action<float> CriticalOxygenEntered;
    /// <summary>Backwards-compat event — fires whenever the *oxygen* rule changes state.</summary>
    public static event Action<OxygenState, float> OxygenStateChanged;
    /// <summary>Fires whenever the set of active alerts changes. List is sorted critical-first.</summary>
    public static event Action<IReadOnlyList<VitalsAlert>> VitalsAlertChanged;

    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;
    [SerializeField] private float refreshIntervalSeconds = 0.25f;

    [Header("Oxygen (also drives OxygenStateChanged for legacy listeners)")]
    [SerializeField] private string primaryOxygenPath = "telemetry.eva1.oxy_pri_storage";
    [SerializeField] private string secondaryOxygenPath = "telemetry.eva1.oxy_sec_storage";
    [SerializeField] private float oxygenLowPercent = 30f;
    [SerializeField] private float oxygenCriticalPercent = 20f;
    [SerializeField] private bool useLowestAvailableSource = true;

    [Header("Other Vitals — leave empty to disable")]
    [SerializeField]
    private List<VitalRule> vitalRules = new List<VitalRule>
    {
        // --- Battery (min 20%, drops throughout EVA) ---
        new VitalRule {
            label = "PRIMARY BATTERY", path = "telemetry.eva1.primary_battery_level",
            unit = "%", direction = AlertDirection.Low,
            warningThreshold = 30f, criticalThreshold = 20f, valueFormat = "F0",
            actionMessage = "Switch DCU BATT to SEC. If already on SEC, monitor and return to PR when able." },
        new VitalRule {
            label = "SECONDARY BATTERY", path = "telemetry.eva1.secondary_battery_level",
            unit = "%", direction = AlertDirection.Low,
            warningThreshold = 30f, criticalThreshold = 20f, valueFormat = "F0",
            actionMessage = "Battery critically low. Return to PR immediately." },

        // --- Oxygen Storage (min 20%, drops throughout EVA) ---
        new VitalRule {
            label = "O2 PRIMARY STORAGE", path = "telemetry.eva1.oxy_pri_storage",
            unit = "%", direction = AlertDirection.Low,
            warningThreshold = 30f, criticalThreshold = 20f, valueFormat = "F0" },
        new VitalRule {
            label = "O2 SECONDARY STORAGE", path = "telemetry.eva1.oxy_sec_storage",
            unit = "%", direction = AlertDirection.Low,
            warningThreshold = 30f, criticalThreshold = 20f, valueFormat = "F0" },

        // --- Oxygen Tank Pressure (min 600 psi, starts full at 3,000 and drains) ---
        new VitalRule {
            label = "O2 PRIMARY PRESSURE", path = "telemetry.eva1.oxy_pri_pressure",
            unit = " psi", direction = AlertDirection.Low,
            warningThreshold = 700f, criticalThreshold = 600f, valueFormat = "F0" },
        new VitalRule {
            label = "O2 SECONDARY PRESSURE", path = "telemetry.eva1.oxy_sec_pressure",
            unit = " psi", direction = AlertDirection.Low,
            warningThreshold = 700f, criticalThreshold = 600f, valueFormat = "F0" },

        // --- Coolant Storage (warn < 85%, critical <= 80% or > 100%) ---
        new VitalRule {
            label = "COOLANT STORAGE", path = "telemetry.eva1.coolant_storage",
            unit = "%", direction = AlertDirection.Range,
            warningThreshold = 85f, criticalThreshold = 80f,
            warningThresholdHigh = 100.01f, criticalThresholdHigh = 100.01f, valueFormat = "F1" },

        // --- Heart Rate (50–160 bpm) ---
        new VitalRule {
            label = "HEART RATE", path = "telemetry.eva1.heart_rate",
            unit = " bpm", direction = AlertDirection.Range,
            warningThreshold = 60f, criticalThreshold = 50f,
            warningThresholdHigh = 160f, criticalThresholdHigh = 160f, valueFormat = "F0",
            actionMessage = "Heart rate too high. Astronaut must slow down.",
            actionMessageLow = "Heart rate too low. Check astronaut condition." },

        // --- O2 Consumption (nominal 0.10, range 0.05–0.15 psi/min) ---
        new VitalRule {
            label = "O2 CONSUMPTION", path = "telemetry.eva1.oxy_consumption",
            unit = " psi/min", direction = AlertDirection.Range,
            warningThreshold = 0.05f, criticalThreshold = 0.01f,
            warningThresholdHigh = 0.15f, criticalThresholdHigh = 0.20f, valueFormat = "F3" },

        // --- CO2 Production (nominal 0.10, range 0.05–0.15 psi/min) ---
        new VitalRule {
            label = "CO2 PRODUCTION", path = "telemetry.eva1.co2_production",
            unit = " psi/min", direction = AlertDirection.Range,
            warningThreshold = 0.05f, criticalThreshold = 0.01f,
            warningThresholdHigh = 0.15f, criticalThresholdHigh = 0.20f, valueFormat = "F3" },

        // --- Suit Pressure: O2 (nominal 4.0, range 3.5–4.1 psi) ---
        new VitalRule {
            label = "SUIT O2 PRESSURE", path = "telemetry.eva1.suit_pressure_oxy",
            unit = " psi", direction = AlertDirection.Range,
            warningThreshold = 3.625f, criticalThreshold = 3.5f,
            warningThresholdHigh = 4.075f, criticalThresholdHigh = 4.1f, valueFormat = "F2",
            actionMessage = "Swap to secondary O2 tank (DCU). Return to PR." },

        // --- Suit Pressure: CO2 (nominal 0.0, max 0.1 psi) ---
        new VitalRule {
            label = "SUIT CO2 PRESSURE", path = "telemetry.eva1.suit_pressure_co2",
            unit = " psi", direction = AlertDirection.High,
            warningThreshold = 0.075f, criticalThreshold = 0.1f, valueFormat = "F3",
            actionMessage = "Vent scrubber via DCU CO2 switch." },

        // --- Suit Pressure: Other (nominal 0.0, max 0.5 psi) ---
        new VitalRule {
            label = "SUIT OTHER PRESSURE", path = "telemetry.eva1.suit_pressure_other",
            unit = " psi", direction = AlertDirection.High,
            warningThreshold = 0.375f, criticalThreshold = 0.5f, valueFormat = "F3",
            actionMessage = "Return to PR immediately." },

        // --- Suit Pressure: Total (nominal 4.0, range 3.5–4.5 psi) ---
        new VitalRule {
            label = "SUIT TOTAL PRESSURE", path = "telemetry.eva1.suit_pressure_total",
            unit = " psi", direction = AlertDirection.Range,
            warningThreshold = 3.625f, criticalThreshold = 3.5f,
            warningThresholdHigh = 4.375f, criticalThresholdHigh = 4.5f, valueFormat = "F2",
            actionMessage = "Check O2 tank pressure: if off nominal range, perform Off Nominal Suit O2 Pressure procedure. Check CO2 scrubber: if off nom. range, perform Off Nominal Suit CO2 Pressure procedure." },

        // --- Helmet CO2 (nominal 0.0, max 0.15 psi) ---
        new VitalRule {
            label = "HELMET CO2", path = "telemetry.eva1.helmet_pressure_co2",
            unit = " psi", direction = AlertDirection.High,
            warningThreshold = 0.113f, criticalThreshold = 0.15f, valueFormat = "F3",
            actionMessage = "Set DCU FAN to SEC. Return to PR immediately." },

        // --- Fan (checks max of primary and secondary — alerts if neither is at 30,000 rpm) ---
        new VitalRule {
            label = "FAN", path = "telemetry.eva1.fan_pri_rpm",
            secondaryPath = "telemetry.eva1.fan_sec_rpm",
            unit = " rpm", direction = AlertDirection.Range,
            warningThreshold = 29999f, criticalThreshold = 20000f,
            warningThresholdHigh = 30001f, criticalThresholdHigh = 30001f, valueFormat = "F0",
            actionMessage = "Set DCU FAN to SEC. Return to PR immediately." },

        // --- CO2 Scrubbers (max 60% — vent when approaching full) ---
        new VitalRule {
            label = "SCRUBBER A", path = "telemetry.eva1.scrubber_a_co2_storage",
            unit = "%", direction = AlertDirection.High,
            warningThreshold = 50f, criticalThreshold = 60f, valueFormat = "F0",
            actionMessage = "Flip CO2 switch on DCU to vent scrubber." },
        new VitalRule {
            label = "SCRUBBER B", path = "telemetry.eva1.scrubber_b_co2_storage",
            unit = "%", direction = AlertDirection.High,
            warningThreshold = 50f, criticalThreshold = 60f, valueFormat = "F0",
            actionMessage = "Flip CO2 switch on DCU to vent scrubber." },

        // --- Temperature (nominal 21°C, range 10–32°C) ---
        new VitalRule {
            label = "TEMPERATURE", path = "telemetry.eva1.temperature",
            unit = "°C", direction = AlertDirection.Range,
            warningThreshold = 12.75f, criticalThreshold = 10f,
            warningThresholdHigh = 29.25f, criticalThresholdHigh = 32f, valueFormat = "F1",
            actionMessage = "Astronaut must slow down." },

        // --- Coolant Liquid Pressure (nominal 500, range 100–700 psi) ---
        new VitalRule {
            label = "COOLANT LIQUID PRESSURE", path = "telemetry.eva1.coolant_liquid_pressure",
            unit = " psi", direction = AlertDirection.Range,
            warningThreshold = 450f, criticalThreshold = 100f,
            warningThresholdHigh = 550f, criticalThresholdHigh = 700f, valueFormat = "F0" },

        // --- Coolant Gas Pressure (nominal 0 psi — any reading indicates coolant turning gaseous) ---
        new VitalRule {
            label = "COOLANT GAS PRESSURE", path = "telemetry.eva1.coolant_gas_pressure",
            unit = " psi", direction = AlertDirection.High,
            warningThreshold = 525f, criticalThreshold = 700f, valueFormat = "F1" },
    };

    [Header("Popup Alert")]
    [Tooltip("Legacy IMGUI debug overlay. Off in flight; the WarningCanvasUI is the real surface.")]
    [SerializeField] private bool showPopupAlert = false;
    [SerializeField] private Vector2 popupSize = new Vector2(420f, 72f);
    [SerializeField] private Vector2 popupMargin = new Vector2(16f, 16f);

    [Header("Debug Output")]
    [SerializeField] private bool logStateTransitions = true;
    [SerializeField] private bool logEverySample = false;

    // Oxygen-specific tracked state (kept for backward compat with HealthPanelUI etc.).
    public OxygenState CurrentState { get; private set; } = OxygenState.Unknown;
    public float CurrentOxygenPercent { get; private set; }
    public string CurrentSourcePath { get; private set; } = string.Empty;
    public string LastOutputMessage { get; private set; } = "No oxygen telemetry received yet.";

    // Multi-vital tracked state.
    public VitalsAlert CurrentAlert { get; private set; }
    public IReadOnlyList<VitalsAlert> CurrentAlerts => _currentAlerts;
    private List<VitalsAlert> _currentAlerts = new List<VitalsAlert>();

    private Coroutine refreshCoroutine;
    private GUIStyle popupStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMonitorExists()
    {
        if (FindObjectOfType<TssVitalsOxygenMonitor>() != null)
        {
            return;
        }

        var monitorObject = new GameObject("TssVitalsOxygenMonitor");
        DontDestroyOnLoad(monitorObject);
        monitorObject.AddComponent<TssVitalsOxygenMonitor>();
    }

    private void Awake()
    {
        ApplyThresholdConstraints();
        TryResolveApiService();
    }

    private void OnEnable()
    {
        IsCriticalOxygenLow = false;
        TryResolveApiService();

        if (tssApi != null)
        {
            tssApi.EvaUpdated += OnEvaUpdated;
            EvaluatePacket(tssApi.GetEva());
        }

        if (refreshCoroutine == null)
        {
            refreshCoroutine = StartCoroutine(RefreshLoop());
        }
    }

    private void OnDisable()
    {
        if (refreshCoroutine != null)
        {
            StopCoroutine(refreshCoroutine);
            refreshCoroutine = null;
        }

        if (tssApi != null)
        {
            tssApi.EvaUpdated -= OnEvaUpdated;
        }

        IsCriticalOxygenLow = false;
    }

    private void OnValidate()
    {
        refreshIntervalSeconds = Mathf.Max(0.05f, refreshIntervalSeconds);
        ApplyThresholdConstraints();
    }

    private IEnumerator RefreshLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.05f, refreshIntervalSeconds));
        while (true)
        {
            if (tssApi == null)
            {
                TryResolveApiService();
            }

            if (tssApi != null)
            {
                EvaluatePacket(tssApi.GetEva());
            }

            yield return wait;
        }
    }

    private void TryResolveApiService()
    {
        if (TssUnityApiService.Instance != null)
        {
            tssApi = TssUnityApiService.Instance;
            return;
        }

        if (tssApi == null)
        {
            tssApi = FindObjectOfType<TssUnityApiService>();
        }
    }

    private void OnEvaUpdated(Dictionary<string, object> packet)
    {
        EvaluatePacket(packet);
    }

    private void EvaluatePacket(Dictionary<string, object> packet)
    {
        if (packet == null || packet.Count == 0)
        {
            return;
        }

        EvaluateOxygen(packet);
        EvaluateAllVitals(packet);
    }

    // -------------------------------------------------------------------------
    // Oxygen-specific (kept for backward compat with HealthPanelUI, etc.)
    // -------------------------------------------------------------------------

    private void EvaluateOxygen(Dictionary<string, object> packet)
    {
        if (!TryReadOxygenPercent(packet, out float oxygenPercent, out string sourcePath))
        {
            return;
        }

        OxygenState nextState = EvaluateOxygenState(oxygenPercent);
        OxygenState previousState = CurrentState;

        CurrentOxygenPercent = oxygenPercent;
        CurrentSourcePath = sourcePath;
        CurrentState = nextState;
        IsCriticalOxygenLow = nextState == OxygenState.CriticallyLow;
        LastOutputMessage = BuildOxygenMessage(nextState, oxygenPercent, sourcePath);

        if (logEverySample)
        {
            Debug.Log($"[Vitals] {LastOutputMessage}");
        }

        if (nextState != previousState)
        {
            if (logStateTransitions)
            {
                Debug.Log($"[Vitals] Oxygen state changed: {previousState} -> {nextState}. {LastOutputMessage}");
            }

            OxygenStateChanged?.Invoke(nextState, oxygenPercent);

            if (nextState == OxygenState.CriticallyLow)
            {
                CriticalOxygenEntered?.Invoke(oxygenPercent);
            }
        }
    }

    private bool TryReadOxygenPercent(
        Dictionary<string, object> packet,
        out float oxygenPercent,
        out string sourcePath)
    {
        oxygenPercent = 0f;
        sourcePath = string.Empty;

        bool hasPrimary = TryGetFloatFromPath(packet, primaryOxygenPath, out float primary);
        bool hasSecondary = TryGetFloatFromPath(packet, secondaryOxygenPath, out float secondary);

        if (useLowestAvailableSource && hasPrimary && hasSecondary)
        {
            if (primary <= secondary)
            {
                oxygenPercent = primary;
                sourcePath = primaryOxygenPath;
            }
            else
            {
                oxygenPercent = secondary;
                sourcePath = secondaryOxygenPath;
            }

            return true;
        }

        if (hasPrimary)
        {
            oxygenPercent = primary;
            sourcePath = primaryOxygenPath;
            return true;
        }

        if (hasSecondary)
        {
            oxygenPercent = secondary;
            sourcePath = secondaryOxygenPath;
            return true;
        }

        return false;
    }

    private OxygenState EvaluateOxygenState(float oxygenPercent)
    {
        if (oxygenPercent <= oxygenCriticalPercent)
        {
            return OxygenState.CriticallyLow;
        }

        if (oxygenPercent <= oxygenLowPercent)
        {
            return OxygenState.RunningLow;
        }

        return OxygenState.Good;
    }

    private string BuildOxygenMessage(OxygenState state, float oxygenPercent, string sourcePath)
    {
        return state switch
        {
            OxygenState.Good => $"OXYGEN GOOD: {oxygenPercent:F1}% (source: {sourcePath})",
            OxygenState.RunningLow => $"OXYGEN RUNNING LOW: {oxygenPercent:F1}% (source: {sourcePath})",
            OxygenState.CriticallyLow =>
                $"CRITICAL OXYGEN: {oxygenPercent:F1}% (source: {sourcePath}) - RETURN IMMEDIATELY",
            _ => $"OXYGEN UNKNOWN: {oxygenPercent:F1}% (source: {sourcePath})"
        };
    }

    // -------------------------------------------------------------------------
    // Generic multi-vital evaluation
    // -------------------------------------------------------------------------

    private void EvaluateAllVitals(Dictionary<string, object> packet)
    {
        var active = new List<VitalsAlert>();

        // Legacy oxygen path feeds in first
        if (CurrentState == OxygenState.RunningLow || CurrentState == OxygenState.CriticallyLow)
        {
            active.Add(new VitalsAlert
            {
                State = CurrentState,
                Headline = BuildOxygenHeadline(CurrentState, CurrentOxygenPercent),
                Rule = null,
                Value = CurrentOxygenPercent
            });
        }

        if (vitalRules != null)
        {
            for (int i = 0; i < vitalRules.Count; i++)
            {
                VitalRule rule = vitalRules[i];
                if (rule == null || !rule.enabled || string.IsNullOrWhiteSpace(rule.path)) continue;
                if (!TryReadVitalValue(packet, rule, out float value)) continue;

                OxygenState ruleState = EvaluateRuleState(rule, value);
                if (ruleState == OxygenState.RunningLow || ruleState == OxygenState.CriticallyLow)
                {
                    active.Add(new VitalsAlert
                    {
                        State = ruleState,
                        Headline = BuildVitalHeadline(rule, ruleState, value),
                        Rule = rule,
                        Value = value
                    });
                }
            }
        }

        // Sort critical before warning
        active.Sort((a, b) => Severity(b.State).CompareTo(Severity(a.State)));

        // Fire only when the alert set actually changes
        if (!AlertListsEqual(_currentAlerts, active))
        {
            _currentAlerts = active;
            CurrentAlert = active.Count > 0 ? active[0] : default;
            VitalsAlertChanged?.Invoke(_currentAlerts);

            if (logStateTransitions && active.Count > 0)
                Debug.Log($"[Vitals] {active.Count} active alert(s). Worst: {active[0].Headline}");
        }
    }

    private static bool AlertListsEqual(List<VitalsAlert> a, List<VitalsAlert> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].State != b[i].State || a[i].Headline != b[i].Headline) return false;
        }
        return true;
    }

    private bool TryReadVitalValue(Dictionary<string, object> packet, VitalRule rule, out float value)
    {
        bool hasPrimary = TryGetFloatFromPath(packet, rule.path, out float primary);
        bool hasSecondary = !string.IsNullOrWhiteSpace(rule.secondaryPath) &&
                             TryGetFloatFromPath(packet, rule.secondaryPath, out float secondary)
                             ? true : false;
        float secondaryValue = 0f;
        if (hasSecondary)
        {
            TryGetFloatFromPath(packet, rule.secondaryPath, out secondaryValue);
        }

        if (hasPrimary && hasSecondary)
        {
            value = rule.direction == AlertDirection.Low
                ? Mathf.Min(primary, secondaryValue)
                : Mathf.Max(primary, secondaryValue);
            return true;
        }

        if (hasPrimary)
        {
            value = primary;
            return true;
        }

        if (hasSecondary)
        {
            value = secondaryValue;
            return true;
        }

        value = 0f;
        return false;
    }

    private static OxygenState EvaluateRuleState(VitalRule rule, float value)
    {
        switch (rule.direction)
        {
            case AlertDirection.Low:
                if (value <= rule.criticalThreshold) return OxygenState.CriticallyLow;
                if (value <= rule.warningThreshold)  return OxygenState.RunningLow;
                return OxygenState.Good;
            case AlertDirection.High:
                if (value >= rule.criticalThreshold) return OxygenState.CriticallyLow;
                if (value >= rule.warningThreshold)  return OxygenState.RunningLow;
                return OxygenState.Good;
            case AlertDirection.Range:
                if (value <= rule.criticalThreshold || value >= rule.criticalThresholdHigh)
                    return OxygenState.CriticallyLow;
                if (value <= rule.warningThreshold || value >= rule.warningThresholdHigh)
                    return OxygenState.RunningLow;
                return OxygenState.Good;
            default:
                return OxygenState.Good;
        }
    }

    private static int Severity(OxygenState state) => state switch
    {
        OxygenState.CriticallyLow => 3,
        OxygenState.RunningLow => 2,
        OxygenState.Good => 1,
        _ => 0
    };

    private string BuildVitalHeadline(VitalRule rule, OxygenState state, float value)
    {
        string formatted = value.ToString(rule.valueFormat);
        string label = string.IsNullOrEmpty(rule.label) ? rule.path : rule.label;

        string actionMsg = rule.actionMessage;
        if (rule.direction == AlertDirection.Range && !string.IsNullOrEmpty(rule.actionMessageLow))
        {
            bool isLowSide = value <= rule.warningThreshold;
            if (isLowSide) actionMsg = rule.actionMessageLow;
        }

        string action = string.IsNullOrEmpty(actionMsg) ? "" : $" — {actionMsg}";
        return state switch
        {
            OxygenState.CriticallyLow => $"CRITICAL {label}: {formatted}{rule.unit}{action}",
            OxygenState.RunningLow    => $"{label} WARNING: {formatted}{rule.unit}{action}",
            _                         => $"{label}: {formatted}{rule.unit}"
        };
    }

    private string BuildOxygenHeadline(OxygenState state, float percent)
    {
        return state switch
        {
            OxygenState.CriticallyLow => $"CRITICAL O2: {percent:F1}% — RETURN IMMEDIATELY",
            OxygenState.RunningLow => $"O2 LOW: {percent:F1}%",
            _ => $"O2: {percent:F1}%"
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    public Dictionary<string, object> GetCurrentOutput()
    {
        return new Dictionary<string, object>
        {
            { "oxygen_percent", CurrentOxygenPercent },
            { "state", CurrentState.ToString() },
            { "is_critical", CurrentState == OxygenState.CriticallyLow },
            { "source_path", CurrentSourcePath },
            { "message", LastOutputMessage },
            { "alert_state", CurrentAlert.State.ToString() },
            { "alert_headline", CurrentAlert.Headline ?? string.Empty },
            {
                "thresholds",
                new Dictionary<string, object>
                {
                    { "good_above_percent", oxygenLowPercent },
                    { "running_low_at_or_below_percent", oxygenLowPercent },
                    { "critical_at_or_below_percent", oxygenCriticalPercent }
                }
            }
        };
    }

    private void OnGUI()
    {
        if (!showPopupAlert)
        {
            return;
        }

        OxygenState worst = CurrentAlert.State;
        if (worst != OxygenState.RunningLow && worst != OxygenState.CriticallyLow)
        {
            return;
        }

        if (popupStyle == null)
        {
            popupStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                padding = new RectOffset(14, 10, 8, 8)
            };
            popupStyle.normal.textColor = Color.white;
        }

        Rect rect = new Rect(
            popupMargin.x,
            Screen.height - popupSize.y - popupMargin.y,
            popupSize.x,
            popupSize.y);

        Color previousColor = GUI.color;
        GUI.color = worst == OxygenState.CriticallyLow
            ? new Color(0.85f, 0.12f, 0.12f, 0.95f)
            : new Color(0.89f, 0.55f, 0.1f, 0.95f);

        GUI.Box(rect, CurrentAlert.Headline ?? LastOutputMessage, popupStyle);
        GUI.color = previousColor;
    }

    private bool TryGetFloatFromPath(Dictionary<string, object> source, string path, out float value)
    {
        value = 0f;
        object raw = GetPath(source, path);
        if (raw == null)
        {
            return false;
        }

        if (raw is float f) { value = f; return true; }
        if (raw is double d) { value = (float)d; return true; }
        if (raw is long l) { value = l; return true; }
        if (raw is int i) { value = i; return true; }
        if (raw is string s && float.TryParse(s, out float parsed)) { value = parsed; return true; }

        return false;
    }

    private static object GetPath(Dictionary<string, object> source, string path)
    {
        if (source == null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        object current = source;
        string[] parts = path.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!(current is Dictionary<string, object> dict) || !dict.TryGetValue(parts[i], out current))
            {
                return null;
            }
        }

        return current;
    }

    private void ApplyThresholdConstraints()
    {
        oxygenLowPercent = Mathf.Clamp(oxygenLowPercent, 0f, 100f);
        oxygenCriticalPercent = Mathf.Clamp(oxygenCriticalPercent, 0f, 100f);

        if (oxygenCriticalPercent > oxygenLowPercent)
        {
            oxygenCriticalPercent = oxygenLowPercent;
        }

        if (vitalRules == null) return;

        for (int i = 0; i < vitalRules.Count; i++)
        {
            VitalRule rule = vitalRules[i];
            if (rule == null) continue;

            if (rule.direction == AlertDirection.Low && rule.criticalThreshold > rule.warningThreshold)
                rule.criticalThreshold = rule.warningThreshold;

            if (rule.direction == AlertDirection.High && rule.criticalThreshold < rule.warningThreshold)
                rule.criticalThreshold = rule.warningThreshold;

            if (rule.direction == AlertDirection.Range)
            {
                if (rule.criticalThreshold > rule.warningThreshold)
                    rule.criticalThreshold = rule.warningThreshold;
                if (rule.criticalThresholdHigh < rule.warningThresholdHigh)
                    rule.criticalThresholdHigh = rule.warningThresholdHigh;
            }
        }
    }
}
