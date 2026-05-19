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

    // ── Internals ──────────────────────────────────────────────────────
    private enum CondType { Timed, UiaBool, DcuBool, DcuBattBool }

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
    }

    private void OnEnable()
    {
        Resolve();
        BuildSteps();
        _current    = 0;
        _latestData = null;

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

        _steps = new List<Step>
        {
            // ── Connect UIA to DCU & start Depress ────────────────────
            T("EV1: Verify umbilical connection from UIA to DCU"),
            U("UIA: EV1 EMU PWR – ON\n",
                uiaPwrSprite,        "eva1_power",         true),
            B("DCU: BATT – UMB\n",
                dcuBattLocalUmbSprite, "lu",               true),
            U("UIA: DEPRESS PUMP – ON\n",
                uiaDepressPumpSprite, "depress",            true),

            // ── Prep O2 Tanks ─────────────────────────────────────────
            U("UIA: OXYGEN O2 VENT – OPEN\n",
                uiaO2VentSprite,     "oxy_vent",           true),
            T("HMD: Wait until both OXY tanks < 10 psi"),
            U("UIA: OXYGEN O2 VENT – CLOSE\n",
                uiaO2VentSprite,     "oxy_vent",           false),
            D("DCU: OXY – PRI\n",
                dcuOxySprite,        "oxy",                true),
            U("UIA: OXYGEN EMU-1 – OPEN\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           true),
            T("HMD: Wait until EV1 Primary O2 tank > 2950 psi"),
            U("UIA: OXYGEN EMU-1 – CLOSE\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           false),
            D("DCU: OXY – SEC\n",
                dcuOxySprite,        "oxy",                false),
            U("UIA: OXYGEN EMU-1 – OPEN\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           true),
            T("HMD: Wait until EV1 Secondary O2 tank > 2950 psi"),
            U("UIA: OXYGEN EMU-1 – CLOSE\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           false),
            D("DCU: OXY – PRI\n",
                dcuOxySprite,        "oxy",                true),

            // ── Prep Coolant Tank ─────────────────────────────────────
            D("DCU: PUMP – OPEN\n",
                dcuPumpSprite,       "pump",               true),
            U("UIA: EV-1 SUPPLY WATER – OPEN\n",
                uiaWaterSupplySprite,"eva1_water_supply",  true),
            T("HMD: Wait until EV1 Coolant Storage > 95%"),
            U("UIA: EV-1 SUPPLY WATER – CLOSE\n",
                uiaWaterSupplySprite,"eva1_water_supply",  false),

            // ── END Depress, Check Switches & Disconnect ───────────────
            T("HMD: Wait until SUIT Pressure and O2 Pressure = 4"),
            U("UIA: DEPRESS PUMP PWR – OFF\n",
                uiaDepressPumpSprite, "depress",            false),
            B("DCU: BATT – PRI\n",
                dcuBattSecPriSprite, "lu",                 true),
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
            T("EV-1: Disconnect UIA and DCU umbilical"),
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

        if (displayImage != null)
        {
            Sprite sprite = step.Image != null ? step.Image : uiaPanelSprite;
            // Ensure DCU CO₂ steps always use `dcu-co2.png` even if a sprite slot was cleared in the inspector.
            if (dcuCo2Sprite != null &&
                step.Label != null &&
                step.Label.IndexOf("CO2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sprite = dcuCo2Sprite;
            }

            // Force the correct BATT image based on the label. "BATT – UMB" / "BATT – LOCAL"
            // must use `dcu-batt-local-umb.png`; "BATT – PRI" / "BATT – SEC" must use `dcu-batt-sec-pri.png`.
            if (step.Label != null &&
                step.Label.IndexOf("BATT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bool isLocalOrUmb =
                    step.Label.IndexOf("UMB",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                    step.Label.IndexOf("LOCAL", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSecOrPri =
                    step.Label.IndexOf("SEC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    step.Label.IndexOf("PRI", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isLocalOrUmb && dcuBattLocalUmbSprite != null) sprite = dcuBattLocalUmbSprite;
                else if (isSecOrPri && dcuBattSecPriSprite != null) sprite = dcuBattSecPriSprite;
            }

            displayImage.sprite = sprite;
        }

        AnnounceStep(index, step);

        if (step.Cond == CondType.Timed)
            _timerCo = StartCoroutine(TimedAdvance(step.Secs));
        else if (_latestData != null)
            TryAdvance();   // condition might already be satisfied from last packet
    }

    private void AnnounceStep(int index, Step step)
    {
        if (stepSpeaker == null) stepSpeaker = FindObjectOfType<ProcedureStepSpeaker>();
        if (stepSpeaker == null || step == null || string.IsNullOrWhiteSpace(step.Label)) return;

        stepSpeaker.Announce($"Step {index + 1} of {_steps.Count}. {step.Label}");
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

        bool met = step.Cond switch
        {
            CondType.UiaBool    => ReadUiaBool(step.Field) == step.Expected,
            CondType.DcuBool    => ReadDcuEva1Bool(step.Field) == step.Expected,
            CondType.DcuBattBool => ReadDcuBattBool(step.Field) == step.Expected,
            _                   => false,
        };

        if (met) Advance();
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
