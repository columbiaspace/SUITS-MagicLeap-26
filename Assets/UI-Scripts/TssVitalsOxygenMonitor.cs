using System;
using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;

/// <summary>
/// Polls the TSS EVA telemetry packet and grades every configured vital against
/// "low / critical" thresholds. Originally oxygen-only — kept the class name for
/// existing scene/prefab references — now drives the WarningCanvas off any vital
/// that drifts out of range (oxygen, battery, suit-pressure CO2, heart rate,
/// temperature, coolant, …).
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
        High
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

        [Tooltip("Suffix appended to the value in the warning text (%, psi, bpm, °F).")]
        public string unit = "%";

        [Tooltip("Whether the alert fires when value goes Low (≤) or High (≥).")]
        public AlertDirection direction = AlertDirection.Low;

        [Tooltip("Yellow-warning threshold.")]
        public float warningThreshold = 30f;

        [Tooltip("Red-critical threshold.")]
        public float criticalThreshold = 15f;

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
    /// <summary>Fires whenever the worst-case vital state changes (any vital).</summary>
    public static event Action<VitalsAlert> VitalsAlertChanged;

    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;
    [SerializeField] private float refreshIntervalSeconds = 0.25f;

    [Header("Oxygen (also drives OxygenStateChanged for legacy listeners)")]
    [SerializeField] private string primaryOxygenPath = "telemetry.eva1.oxy_pri_storage";
    [SerializeField] private string secondaryOxygenPath = "telemetry.eva1.oxy_sec_storage";
    [SerializeField] private float oxygenLowPercent = 30f;
    [SerializeField] private float oxygenCriticalPercent = 15f;
    [SerializeField] private bool useLowestAvailableSource = true;

    [Header("Other Vitals — leave empty to disable")]
    [SerializeField]
    private List<VitalRule> vitalRules = new List<VitalRule>
    {
        new VitalRule {
            label = "BATTERY", path = "telemetry.eva1.primary_battery_level",
            secondaryPath = "telemetry.eva1.secondary_battery_level", unit = "%",
            direction = AlertDirection.Low, warningThreshold = 30f, criticalThreshold = 15f },
        new VitalRule {
            label = "HEART RATE", path = "telemetry.eva1.heart_rate", unit = "bpm",
            direction = AlertDirection.High, warningThreshold = 150f, criticalThreshold = 180f, valueFormat = "F0" },
        new VitalRule {
            label = "SUIT CO2", path = "telemetry.eva1.suit_pressure_co2", unit = "psi",
            direction = AlertDirection.High, warningThreshold = 0.5f, criticalThreshold = 1f, valueFormat = "F2" },
        new VitalRule {
            label = "HELMET CO2", path = "telemetry.eva1.helmet_pressure_co2", unit = "psi",
            direction = AlertDirection.High, warningThreshold = 0.5f, criticalThreshold = 1f, valueFormat = "F2" },
        new VitalRule {
            label = "TEMP", path = "telemetry.eva1.temperature", unit = "°F",
            direction = AlertDirection.High, warningThreshold = 100f, criticalThreshold = 105f, valueFormat = "F1" },
        new VitalRule {
            label = "COOLANT", path = "telemetry.eva1.coolant_storage", unit = "%",
            direction = AlertDirection.Low, warningThreshold = 30f, criticalThreshold = 15f, valueFormat = "F1" },
        new VitalRule {
            label = "SUIT PRESSURE", path = "telemetry.eva1.suit_pressure_total", unit = "psi",
            direction = AlertDirection.Low, warningThreshold = 3.5f, criticalThreshold = 3f, valueFormat = "F2" }
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
        if (tssApi != null)
        {
            return;
        }

        tssApi = TssUnityApiService.Instance;
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
        VitalsAlert worst = new VitalsAlert
        {
            State = CurrentState,
            Headline = BuildOxygenHeadline(CurrentState, CurrentOxygenPercent),
            Rule = null,
            Value = CurrentOxygenPercent
        };

        if (vitalRules != null)
        {
            for (int i = 0; i < vitalRules.Count; i++)
            {
                VitalRule rule = vitalRules[i];
                if (rule == null || !rule.enabled || string.IsNullOrWhiteSpace(rule.path)) continue;

                if (!TryReadVitalValue(packet, rule, out float value)) continue;

                OxygenState ruleState = EvaluateRuleState(rule, value);
                if (Severity(ruleState) > Severity(worst.State))
                {
                    worst = new VitalsAlert
                    {
                        State = ruleState,
                        Headline = BuildVitalHeadline(rule, ruleState, value),
                        Rule = rule,
                        Value = value
                    };
                }
            }
        }

        if (worst.State != CurrentAlert.State || worst.Headline != CurrentAlert.Headline)
        {
            CurrentAlert = worst;
            VitalsAlertChanged?.Invoke(worst);

            if (logStateTransitions && worst.State != OxygenState.Good && worst.State != OxygenState.Unknown)
            {
                Debug.Log($"[Vitals] {worst.Headline}");
            }
        }
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
        if (rule.direction == AlertDirection.Low)
        {
            if (value <= rule.criticalThreshold) return OxygenState.CriticallyLow;
            if (value <= rule.warningThreshold) return OxygenState.RunningLow;
            return OxygenState.Good;
        }

        if (value >= rule.criticalThreshold) return OxygenState.CriticallyLow;
        if (value >= rule.warningThreshold) return OxygenState.RunningLow;
        return OxygenState.Good;
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
        return state switch
        {
            OxygenState.CriticallyLow =>
                $"CRITICAL {label}: {formatted}{rule.unit} — RETURN IMMEDIATELY",
            OxygenState.RunningLow =>
                $"{label} WARNING: {formatted}{rule.unit}",
            _ => $"{label}: {formatted}{rule.unit}"
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

        if (raw is float f)
        {
            value = f;
            return true;
        }

        if (raw is double d)
        {
            value = (float)d;
            return true;
        }

        if (raw is long l)
        {
            value = l;
            return true;
        }

        if (raw is int i)
        {
            value = i;
            return true;
        }

        if (raw is string s && float.TryParse(s, out float parsed))
        {
            value = parsed;
            return true;
        }

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
            {
                rule.criticalThreshold = rule.warningThreshold;
            }
            if (rule.direction == AlertDirection.High && rule.criticalThreshold < rule.warningThreshold)
            {
                rule.criticalThreshold = rule.warningThreshold;
            }
        }
    }
}
