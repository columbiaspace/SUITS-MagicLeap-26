using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TssApi;

/// <summary>
/// Drives the sequential EVA egress checklist.
/// Each step either waits a fixed number of seconds (timed) or polls the TSS
/// data until a specific UIA / DCU boolean field reaches the expected value.
/// </summary>
public class EgressProcedureManager : MonoBehaviour
{
    // ── UI wiring ──────────────────────────────────────────────────────
    [SerializeField] private Image               displayImage;
    [SerializeField] private Text                stepText;
    [SerializeField] private TssUnityApiService  tssApi;

    [Header("Completion")]
    [Tooltip("Optional speaker for spoken announcements. Auto-found in scene if left empty.")]
    [SerializeField] private ProcedureStepSpeaker stepSpeaker;
    [Tooltip("Scene to load once Egress completes.")]
    [SerializeField] private string completionScene = "Mission";
    [Tooltip("Seconds to wait after the completion announcement before loading Mission.")]
    [SerializeField] private float completionRedirectDelay = 3f;

    // ── Sprites ────────────────────────────────────────────────────────
    [Header("UIA Sprites")]
    [SerializeField] private Sprite uiaPanelSprite;        // UIA.jpg  (default / no-image steps)
    [SerializeField] private Sprite uiaPwrSprite;          // UIA-pwr.png
    [SerializeField] private Sprite uiaO2VentSprite;       // UIA-O2.png (OXYGEN O2 VENT)
    [SerializeField] private Sprite uiaOxygenEmu1Sprite;   // UIA-oxygen-emu1.png (OXYGEN EMU-1)
    [SerializeField] private Sprite uiaDepressPumpSprite;  // UIA-depress-pump.png (DEPRESS PUMP)
    [SerializeField] private Sprite uiaWaterSupplySprite;  // UIA-water-supply.png

    [Header("DCU Sprites")]
    [SerializeField] private Sprite dcuPanelSprite;        // dcu.png  (default / non-batt)
    [SerializeField] private Sprite dcuOxySprite;          // dcu-oxy.png
    [SerializeField] private Sprite dcuFanSprite;          // dcu-fan.png
    [SerializeField] private Sprite dcuPumpSprite;         // dcu-pump.png
    [SerializeField] private Sprite dcuCo2Sprite;          // dcu-co2.png — CO₂ / scrubber
    [SerializeField] private Sprite dcuBattLocalUmbSprite; // dcu-batt-local-umb.png — BATT LOCAL / UMB
    [SerializeField] private Sprite dcuBattSecPriSprite;  // dcu-batt-sec-pri.png — BATT SEC / PRI

    [Header("Display")]
    [Tooltip("Multiplier applied to displayImage scale when showing a DCU sprite (larger so the panel text is readable).")]
    [SerializeField] private float dcuImageScale = 1.5f;

    private Vector3 _baseImageScale = Vector3.one;

    // ── Internals ──────────────────────────────────────────────────────
    private enum CondType { Timed, UiaBool, DcuBool, DcuBattBool, HmdWait }

    private const string HmdOxyTanksBelow10 = "oxy_tanks_below_10";
    private const string HmdOxyPriAbove2950 = "oxy_pri_above_2950";
    private const string HmdOxySecAbove2950 = "oxy_sec_above_2950";
    private const string HmdCoolantAbove95 = "coolant_above_95";
    private const string HmdSuitPressure4 = "suit_pressure_4";

    private class Step
    {
        public string   Label;
        public Sprite   Image;
        public CondType Cond;
        public string   Field;
        public bool     Expected;
        public float    Secs;
    }

    private List<Step>                  _steps;
    private int                         _current;
    private Dictionary<string, object>  _latestData;
    private Coroutine                   _timerCo;

    // ──────────────────────────────────────────────────────────────────
    private void Awake()
    {
        Resolve();
        if (displayImage != null)
        {
            _baseImageScale = displayImage.rectTransform.localScale;
        }
    }

    private void OnEnable()
    {
        Resolve();
        BuildSteps();
        _current    = 0;
        _latestData = null;

        ProcedureVoiceAnnouncements.Announce(ProcedureVoiceAnnouncements.EgressStart, stepSpeaker);

        RegisterTssEva();
        EnterStep(0);
    }

    private void Update()
    {
        if (tssApi != null)
        {
            return;
        }

        Resolve();
        RegisterTssEva();
    }

    private void OnDisable()
    {
        if (tssApi != null)
        {
            tssApi.EvaUpdated -= OnEvaUpdated;
        }

        KillTimer();
    }

    private void Resolve()
    {
        // Always prefer the persistent singleton over an Inspector-wired reference,
        // because scene-embedded TssUnityApiService components destroy themselves
        // when a singleton from an earlier scene already exists, leaving any
        // pre-assigned tssApi pointing at a destroyed component (no EVA updates).
        if (TssUnityApiService.Instance != null)
            tssApi = TssUnityApiService.Instance;
        else if (tssApi == null)
            tssApi = FindObjectOfType<TssUnityApiService>();

        if (stepSpeaker == null)
            stepSpeaker = FindObjectOfType<ProcedureStepSpeaker>();
    }

    /// <summary>
    /// Binds EVA telemetry once TSS is available. If <see cref="TssUnityApiService"/> wakes up after
    /// this object, <see cref="Update"/> retries. Seeds with <see cref="TssUnityApiService.GetEva"/> so
    /// we do not miss a step when telemetry already matches before the next UDP poll.
    /// </summary>
    private void RegisterTssEva()
    {
        if (tssApi == null)
        {
            return;
        }

        tssApi.EvaUpdated -= OnEvaUpdated;
        tssApi.EvaUpdated += OnEvaUpdated;
        OnEvaUpdated(tssApi.GetEva());
    }

    // ── Step list ──────────────────────────────────────────────────────
    private void BuildSteps()
    {
        Step T(string lbl, Sprite img = null, float secs = 3f) => new Step
            { Label = lbl, Image = img, Cond = CondType.Timed, Secs = secs };
        Step U(string lbl, Sprite img, string field, bool exp) => new Step
            { Label = lbl, Image = img, Cond = CondType.UiaBool, Field = field, Expected = exp };
        Step D(string lbl, Sprite img, string field, bool exp) => new Step
            { Label = lbl, Image = img, Cond = CondType.DcuBool, Field = field, Expected = exp };
        Step B(string lbl, Sprite img, string field, bool exp) => new Step
            { Label = lbl, Image = img, Cond = CondType.DcuBattBool, Field = field, Expected = exp };
        Step H(string lbl, Sprite img, string hmdKey) => new Step
            { Label = lbl, Image = img, Cond = CondType.HmdWait, Field = hmdKey };

        _steps = new List<Step>
        {
            // ── Connect UIA to DCU & start Depress ────────────────────
            T("EV1: Verify umbilical connection from UIA to DCU", dcuPanelSprite),
            U("UIA: EV1 EMU PWR – ON\n",
                uiaPwrSprite,        "eva1_power",         true),
            B("DCU: BATT – UMB\n",
                dcuBattLocalUmbSprite, "lu",               true),
            U("UIA: DEPRESS PUMP – ON\n",
                uiaDepressPumpSprite, "depress",            true),

            // ── Prep O2 Tanks ─────────────────────────────────────────
            U("UIA: OXYGEN O2 VENT – OPEN\n",
                uiaO2VentSprite,     "oxy_vent",           true),
            H("HMD: Wait until both OXY tanks < 10 psi", uiaPanelSprite, HmdOxyTanksBelow10),
            U("UIA: OXYGEN O2 VENT – CLOSE\n",
                uiaO2VentSprite,     "oxy_vent",           false),
            D("DCU: OXY – PRI\n",
                dcuOxySprite,        "oxy",                true),
            U("UIA: OXYGEN EMU-1 – OPEN\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           true),
            H("HMD: Wait until EV1 Primary O2 tank > 2950 psi", uiaPanelSprite, HmdOxyPriAbove2950),
            U("UIA: OXYGEN EMU-1 – CLOSE\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           false),
            D("DCU: OXY – SEC\n",
                dcuOxySprite,        "oxy",                false),
            U("UIA: OXYGEN EMU-1 – OPEN\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           true),
            H("HMD: Wait until EV1 Secondary O2 tank > 2950 psi", uiaPanelSprite, HmdOxySecAbove2950),
            U("UIA: OXYGEN EMU-1 – CLOSE\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           false),
            D("DCU: OXY – PRI\n",
                dcuOxySprite,        "oxy",                true),

            // ── Prep Coolant Tank ─────────────────────────────────────
            D("DCU: PUMP – OPEN\n",
                dcuPumpSprite,       "pump",               true),
            U("UIA: EV-1 SUPPLY WATER – OPEN\n",
                uiaWaterSupplySprite,"eva1_water_supply",  true),
            H("HMD: Wait until EV1 Coolant Storage > 95%", uiaPanelSprite, HmdCoolantAbove95),
            U("UIA: EV-1 SUPPLY WATER – CLOSE\n",
                uiaWaterSupplySprite,"eva1_water_supply",  false),

            // ── END Depress, Check Switches & Disconnect ───────────────
            H("HMD: Wait until SUIT Pressure and O2 Pressure = 4", uiaPanelSprite, HmdSuitPressure4),
            U("UIA: DEPRESS PUMP PWR – OFF\n",
                uiaDepressPumpSprite, "depress",            false),
            // PRI/SEC selector lives on `dcu.eva1.batt.ps` (per ImageCarouselUI's
            // EvaField.DcuEva1BattLocal mapping). Using `lu` here would inherit the
            // already-true UMB state from Step 3 and silently skip this step.
            B("DCU: BATT – PRI\n",
                dcuBattSecPriSprite, "ps",                 true),
            B("DCU: BATT – LOCAL\n",
                dcuBattLocalUmbSprite, "lu",               false),
            U("UIA: EV-1 EMU PWR – OFF\n",
                uiaPwrSprite,        "eva1_power",         false),
            D("DCU: FAN – PRI\n",
                dcuFanSprite,        "fan",                true),
            D("DCU: PUMP – CLOSE\n",
                dcuPumpSprite,       "pump",               false),
            // Checklist step 28 (UI: “Step 28 of 32”) — `UI-Prefab/PNG/dcu-co2.png`
            D("DCU: CO2 – PRI\n",
                dcuCo2Sprite,        "co2",                true),
            D("DCU: Verify OXY – PRI\n",
                dcuOxySprite,        "oxy",                true),
            T("EV-1: Disconnect UIA and DCU umbilical", dcuPanelSprite),
            T("Verbally announce completion of egress"),
            T("Begin navigation procedure"),
        };
    }

    // ── Step lifecycle ─────────────────────────────────────────────────
    private void EnterStep(int index)
    {
        KillTimer();

        if (index >= _steps.Count)
        {
            _timerCo = StartCoroutine(AnnounceAndRedirect());
            return;
        }

        var step = _steps[index];

        if (stepText != null)
            stepText.text = $"Step {index + 1} of {_steps.Count}\n{step.Label}";

        ApplyStepImage(step.Image);

        AnnounceStep(index, step);

        if (step.Cond == CondType.Timed)
            _timerCo = StartCoroutine(TimedAdvance(step.Secs));
        else
            TryAdvance();
    }

    private void AnnounceStep(int index, Step step)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.Label)) return;
        ProcedureVoiceAnnouncements.Announce(
            $"Step {index + 1} of {_steps.Count}. {step.Label}", stepSpeaker);
    }

    private void ApplyStepImage(Sprite sprite)
    {
        if (displayImage == null) return;

        Sprite shown = sprite != null ? sprite : uiaPanelSprite;
        displayImage.sprite = shown;

        float multiplier = IsDcuSprite(shown) ? Mathf.Max(0.01f, dcuImageScale) : 1f;
        displayImage.rectTransform.localScale = _baseImageScale * multiplier;
    }

    private bool IsDcuSprite(Sprite sprite)
    {
        return sprite == dcuPanelSprite || sprite == dcuOxySprite || sprite == dcuFanSprite
            || sprite == dcuPumpSprite || sprite == dcuCo2Sprite
            || sprite == dcuBattLocalUmbSprite || sprite == dcuBattSecPriSprite;
    }

    private IEnumerator TimedAdvance(float secs)
    {
        yield return new WaitForSeconds(secs);
        Advance();
    }

    private IEnumerator AnnounceAndRedirect()
    {
        const string completionMessage = ProcedureVoiceAnnouncements.EgressCompletion;
        if (stepText != null) stepText.text = completionMessage;

        if (stepSpeaker == null) stepSpeaker = FindObjectOfType<ProcedureStepSpeaker>();
        ProcedureVoiceAnnouncements.Announce(completionMessage, stepSpeaker);

        if (!string.IsNullOrEmpty(completionScene))
        {
            yield return new WaitForSeconds(Mathf.Max(0f, completionRedirectDelay));
            try { SceneManager.LoadScene(completionScene); }
            catch (Exception e) { Debug.LogWarning($"[Egress] Failed to load '{completionScene}': {e.Message}"); }
        }

        _timerCo = null;
    }

    private void Advance()
    {
        _current++;
        EnterStep(_current);
    }

    private void KillTimer()
    {
        if (_timerCo != null) { StopCoroutine(_timerCo); _timerCo = null; }
    }

    // ── TSS data ───────────────────────────────────────────────────────
    private void OnEvaUpdated(Dictionary<string, object> data)
    {
        _latestData = data;
        TryAdvance();
    }

    private void TryAdvance()
    {
        if (_current >= _steps.Count) return;
        var step = _steps[_current];
        if (step.Cond == CondType.Timed) return;

        if (step.Cond == CondType.HmdWait)
        {
            RefreshHmdStepText(step);
            if (IsHmdWaitMet(step.Field))
            {
                Advance();
            }

            return;
        }

        bool met = step.Cond switch
        {
            CondType.UiaBool    => ReadUiaBool(step.Field) == step.Expected,
            CondType.DcuBool    => ReadDcuEva1Bool(step.Field) == step.Expected,
            CondType.DcuBattBool => ReadDcuBattBool(step.Field) == step.Expected,
            _                   => false,
        };

        if (met) Advance();
    }

    private void RefreshHmdStepText(Step step)
    {
        if (stepText == null || step == null)
        {
            return;
        }

        string progress = step.Field switch
        {
            HmdOxyTanksBelow10 =>
                $"Pri: {FmtTelemetry("oxy_pri_pressure", "psi")} | Sec: {FmtTelemetry("oxy_sec_pressure", "psi")} (need both < 10 psi)",
            HmdOxyPriAbove2950 =>
                $"Pri: {FmtTelemetry("oxy_pri_pressure", "psi")} (need > 2950 psi)",
            HmdOxySecAbove2950 =>
                $"Sec: {FmtTelemetry("oxy_sec_pressure", "psi")} (need > 2950 psi)",
            HmdCoolantAbove95 =>
                $"Coolant: {FmtTelemetry("coolant_storage", "%")} (need > 95%)",
            HmdSuitPressure4 =>
                $"Suit O2: {FmtTelemetry("suit_pressure_oxy", "psi")} | Total: {FmtTelemetry("suit_pressure_total", "psi")} (need ≈ 4 psi)",
            _ => string.Empty,
        };

        stepText.text = string.IsNullOrEmpty(progress)
            ? $"Step {_current + 1} of {_steps.Count}\n{step.Label}"
            : $"Step {_current + 1} of {_steps.Count}\n{step.Label}\n{progress}";
    }

    private bool IsHmdWaitMet(string key)
    {
        if (_latestData == null || string.IsNullOrEmpty(key))
        {
            return false;
        }

        switch (key)
        {
            case HmdOxyTanksBelow10:
                return ReadTelemetry("oxy_pri_pressure", out double priLow) && priLow < 10.0 &&
                       ReadTelemetry("oxy_sec_pressure", out double secLow) && secLow < 10.0;
            case HmdOxyPriAbove2950:
                return ReadTelemetry("oxy_pri_pressure", out double pri) && pri > 2950.0;
            case HmdOxySecAbove2950:
                return ReadTelemetry("oxy_sec_pressure", out double sec) && sec > 2950.0;
            case HmdCoolantAbove95:
                return ReadTelemetry("coolant_storage", out double coolant) && coolant > 95.0;
            case HmdSuitPressure4:
                return ReadTelemetry("suit_pressure_oxy", out double suitOxy) && Near(suitOxy, 4.0) &&
                       ReadTelemetry("suit_pressure_total", out double suitTotal) && Near(suitTotal, 4.0);
            default:
                return false;
        }
    }

    private static bool Near(double value, double target) => Math.Abs(value - target) <= 0.1;

    private string FmtTelemetry(string field, string unit)
    {
        if (!ReadTelemetry(field, out double value))
        {
            return "---";
        }

        return value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " " + unit;
    }

    private bool ReadTelemetry(string field, out double value)
    {
        value = 0;
        object raw = GetPath(_latestData, "telemetry.eva1." + field);
        return TryCoerceDouble(raw, out value);
    }

    private static bool TryCoerceDouble(object raw, out double value)
    {
        value = 0;
        if (raw == null)
        {
            return false;
        }

        if (raw is double d) { value = d; return true; }
        if (raw is float f) { value = f; return true; }
        if (raw is int i) { value = i; return true; }
        if (raw is long l) { value = l; return true; }
        if (raw is string s)
        {
            return double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        if (raw is IConvertible c)
        {
            try
            {
                value = c.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    // ── Data accessors ─────────────────────────────────────────────────
    private bool ReadUiaBool(string field)
    {
        try
        {
            if (_latestData == null) return false;

            // Match ImageCarouselUI paths (uia.eva1_power) and tolerate TSS variants.
            if (TryCoerceBool(GetPath(_latestData, "uia." + field), out bool b)) return b;

            if (field.StartsWith("eva1_", StringComparison.Ordinal))
            {
                string suffix = field.Substring("eva1_".Length);
                if (TryCoerceBool(GetPath(_latestData, "uia.eva1." + suffix), out b)) return b;
            }

            if (field.StartsWith("eva2_", StringComparison.Ordinal))
            {
                string suffix = field.Substring("eva2_".Length);
                if (TryCoerceBool(GetPath(_latestData, "uia.eva2." + suffix), out b)) return b;
            }

            if (_latestData.TryGetValue("uia", out var raw) &&
                raw is Dictionary<string, object> uia &&
                uia.TryGetValue(field, out var v))
                return TryCoerceBool(v, out b) && b;
        }
        catch (Exception e) { Debug.LogWarning($"[Egress] UIA({field}): {e.Message}"); }
        return false;
    }

    private static object GetPath(Dictionary<string, object> source, string path)
    {
        if (source == null || string.IsNullOrEmpty(path)) return null;
        object current = source;
        foreach (string part in path.Split('.'))
        {
            if (!(current is Dictionary<string, object> dict) || !dict.TryGetValue(part, out current))
                return null;
        }
        return current;
    }

    /// <summary>
    /// Mirrors TssUnityApiService.ToBool — Convert.ToBoolean throws on e.g. string "1".
    /// </summary>
    private static bool TryCoerceBool(object raw, out bool value)
    {
        value = false;
        if (raw == null) return false;
        if (raw is bool b) { value = b; return true; }
        if (raw is string s)
        {
            if (bool.TryParse(s, out bool pb)) { value = pb; return true; }
            if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
            {
                value = Math.Abs(d) > double.Epsilon;
                return true;
            }
            return false;
        }
        if (raw is IConvertible c)
        {
            try
            {
                value = Math.Abs(c.ToDouble(null)) > double.Epsilon;
                return true;
            }
            catch { return false; }
        }
        return false;
    }

    private bool ReadDcuEva1Bool(string field)
    {
        try
        {
            if (_latestData.TryGetValue("dcu", out var dRaw) &&
                dRaw is Dictionary<string, object> dcu &&
                dcu.TryGetValue("eva1", out var eRaw) &&
                eRaw is Dictionary<string, object> eva1 &&
                eva1.TryGetValue(field, out var v))
                return Convert.ToBoolean(v);
        }
        catch (Exception e) { Debug.LogWarning($"[Egress] DCU.eva1({field}): {e.Message}"); }
        return false;
    }

    private bool ReadDcuBattBool(string field)
    {
        try
        {
            if (_latestData.TryGetValue("dcu", out var dRaw) &&
                dRaw is Dictionary<string, object> dcu &&
                dcu.TryGetValue("eva1", out var eRaw) &&
                eRaw is Dictionary<string, object> eva1 &&
                eva1.TryGetValue("batt", out var bRaw) &&
                bRaw is Dictionary<string, object> batt &&
                batt.TryGetValue(field, out var v))
                return Convert.ToBoolean(v);
        }
        catch (Exception e) { Debug.LogWarning($"[Egress] DCU.batt({field}): {e.Message}"); }
        return false;
    }
}
