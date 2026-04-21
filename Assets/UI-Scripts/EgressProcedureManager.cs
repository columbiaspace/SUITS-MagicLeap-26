using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

    // ── Sprites ────────────────────────────────────────────────────────
    [Header("UIA Sprites")]
    [SerializeField] private Sprite uiaPanelSprite;        // UIA.jpg  (default / no-image steps)
    [SerializeField] private Sprite uiaPwrSprite;          // UIA-pwr.png
    [SerializeField] private Sprite uiaO2VentSprite;       // UIA-O2.png (OXYGEN O2 VENT)
    [SerializeField] private Sprite uiaOxygenEmu1Sprite;   // UIA-oxygen-emu1.png (OXYGEN EMU-1)
    [SerializeField] private Sprite uiaDepressPumpSprite;  // UIA-depress-pump.png (DEPRESS PUMP)
    [SerializeField] private Sprite uiaWaterSupplySprite;  // UIA-water-supply.png

    [Header("DCU Sprites")]
    [SerializeField] private Sprite dcuPanelSprite;        // dcu.png  (default)
    [SerializeField] private Sprite dcuOxySprite;          // dcu-oxy.png
    [SerializeField] private Sprite dcuFanSprite;          // dcu-fan.png
    [SerializeField] private Sprite dcuPumpSprite;         // dcu-pump.png

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
        if (tssApi != null) tssApi.EvaUpdated += OnEvaUpdated;

        BuildSteps();
        _current    = 0;
        _latestData = null;
        EnterStep(0);
    }

    private void OnDisable()
    {
        if (tssApi != null) tssApi.EvaUpdated -= OnEvaUpdated;
        KillTimer();
    }

    private void Resolve()
    {
        if (tssApi == null)
            tssApi = TssUnityApiService.Instance ?? FindObjectOfType<TssUnityApiService>();
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
            B("DCU: BATT – UMB\n(batt.ps = false)",
                dcuPanelSprite,      "ps",                 false),
            U("UIA: DEPRESS PUMP – ON\n",
                uiaDepressPumpSprite, "depress",            true),

            // ── Prep O2 Tanks ─────────────────────────────────────────
            U("UIA: OXYGEN O2 VENT – OPEN\n",
                uiaO2VentSprite,     "oxy_vent",           true),
            T("HMD: Wait until both OXY tanks < 10 psi"),
            U("UIA: OXYGEN O2 VENT – CLOSE\n",
                uiaO2VentSprite,     "oxy_vent",           false),
            D("DCU: OXY – PRI\n(oxy = true)",
                dcuOxySprite,        "oxy",                true),
            U("UIA: OXYGEN EMU-1 – OPEN\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           true),
            T("HMD: Wait until EV1 Primary O2 tank > 2950 psi"),
            U("UIA: OXYGEN EMU-1 – CLOSE\n",
                uiaOxygenEmu1Sprite, "eva1_oxy",           false),
            D("DCU: OXY – SEC\n(oxy = false)",
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
                dcuPanelSprite,      "lu",                 true),
            B("DCU: BATT – LOCAL\n",
                dcuPanelSprite,      "ps",                 true),
            U("UIA: EV-1 EMU PWR – OFF\n",
                uiaPwrSprite,        "eva1_power",         false),
            D("DCU: FAN – PRI\n(fan = true)",
                dcuFanSprite,        "fan",                true),
            D("DCU: PUMP – CLOSE\n",
                dcuPumpSprite,       "pump",               false),
            D("DCU: CO2 – PRI\n",
                dcuPanelSprite,      "co2",                true),
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
            if (stepText != null) stepText.text = "Egress procedure complete.";
            return;
        }

        var step = _steps[index];

        if (stepText != null)
            stepText.text = $"Step {index + 1} of {_steps.Count}\n{step.Label}";

        if (displayImage != null)
            displayImage.sprite = step.Image != null ? step.Image : uiaPanelSprite;

        if (step.Cond == CondType.Timed)
            _timerCo = StartCoroutine(TimedAdvance(step.Secs));
        else if (_latestData != null)
            TryAdvance();   // condition might already be satisfied from last packet
    }

    private IEnumerator TimedAdvance(float secs)
    {
        yield return new WaitForSeconds(secs);
        Advance();
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
            if (_latestData.TryGetValue("uia", out var raw) &&
                raw is Dictionary<string, object> uia &&
                uia.TryGetValue(field, out var v))
                return Convert.ToBoolean(v);
        }
        catch (Exception e) { Debug.LogWarning($"[Egress] UIA({field}): {e.Message}"); }
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
