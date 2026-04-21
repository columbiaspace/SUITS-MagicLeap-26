using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TssApi;

/// <summary>
/// Sequential ingress checklist: END Depress, check switches, disconnect.
/// Uses the same TSS /eva payload as <see cref="EgressProcedureManager"/>.
/// </summary>
public class IngressProcedureManager : MonoBehaviour
{
    [SerializeField] private Image               displayImage;
    [SerializeField] private Text                stepText;
    [SerializeField] private TssUnityApiService  tssApi;

    [Header("UIA Sprites")]
    [SerializeField] private Sprite uiaPanelSprite;
    [SerializeField] private Sprite uiaPwrSprite;
    [SerializeField] private Sprite uiaDepressPumpSprite;

    [Header("DCU Sprites")]
    [SerializeField] private Sprite dcuPanelSprite;
    [SerializeField] private Sprite dcuOxySprite;
    [SerializeField] private Sprite dcuFanSprite;
    [SerializeField] private Sprite dcuPumpSprite;

    private enum CondType { Timed, UiaBool, DcuBool, DcuBattBool, TelemetrySuitO2Pressure4 }

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

    private const float PressureTarget = 4f;
    private const float PressureTolerance = 0.35f;

    private void Awake() => Resolve();

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
        Step H() => new Step
        {
            Label = "HMD: Wait until SUIT Pressure and O2 Pressure = 4\n(telemetry.eva1 suit_pressure_oxy & suit_pressure_total)",
            Image = uiaPanelSprite,
            Cond  = CondType.TelemetrySuitO2Pressure4,
            Secs  = 0f
        };

        _steps = new List<Step>
        {
            H(),
            U("UIA: DEPRESS PUMP PWR – OFF\n",
                uiaDepressPumpSprite, "depress", false),
            B("DCU: BATT – PRI\n",
                dcuPanelSprite, "lu", true),
            B("DCU: BATT – LOCAL\n",
                dcuPanelSprite, "ps", true),
            U("UIA: EV-1 EMU PWR – OFF\n",
                uiaPwrSprite, "eva1_power", false),
            D("DCU: FAN – PRI\n",
                dcuFanSprite, "fan", true),
            D("DCU: PUMP – CLOSE\n",
                dcuPumpSprite, "pump", false),
            D("DCU: CO2 – PRI\n",
                dcuPanelSprite, "co2", true),
            D("DCU: Verify OXY – PRI\n",
                dcuOxySprite, "oxy", true),
            T("EV-1: Disconnect UIA and DCU umbilical"),
            T("Verbally announce completion of ingress"),
            T("Begin navigation procedure"),
        };
    }

    private void EnterStep(int index)
    {
        KillTimer();

        if (index >= _steps.Count)
        {
            if (stepText != null) stepText.text = "Ingress procedure complete.";
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
            TryAdvance();
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
            CondType.UiaBool                  => ReadUiaBool(step.Field) == step.Expected,
            CondType.DcuBool                => ReadDcuEva1Bool(step.Field) == step.Expected,
            CondType.DcuBattBool            => ReadDcuBattBool(step.Field) == step.Expected,
            CondType.TelemetrySuitO2Pressure4 => TelemetryPressureReady(),
            _                               => false,
        };

        if (met) Advance();
    }

    private bool TelemetryPressureReady()
    {
        try
        {
            if (_latestData == null) return false;
            if (!_latestData.TryGetValue("telemetry", out var tRaw) ||
                tRaw is not Dictionary<string, object> tel) return false;
            if (!tel.TryGetValue("eva1", out var eRaw) ||
                eRaw is not Dictionary<string, object> eva1) return false;

            float oxy   = ReadFloat(eva1, "suit_pressure_oxy");
            float total = ReadFloat(eva1, "suit_pressure_total");
            return Mathf.Abs(oxy - PressureTarget) <= PressureTolerance
                && Mathf.Abs(total - PressureTarget) <= PressureTolerance;
        }
        catch (Exception e) { Debug.LogWarning($"[Ingress] telemetry: {e.Message}"); }
        return false;
    }

    private static float ReadFloat(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return float.NaN;
        return Convert.ToSingle(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    private bool ReadUiaBool(string field)
    {
        try
        {
            if (_latestData.TryGetValue("uia", out var raw) &&
                raw is Dictionary<string, object> uia &&
                uia.TryGetValue(field, out var v))
                return Convert.ToBoolean(v);
        }
        catch (Exception e) { Debug.LogWarning($"[Ingress] UIA({field}): {e.Message}"); }
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
        catch (Exception e) { Debug.LogWarning($"[Ingress] DCU.eva1({field}): {e.Message}"); }
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
        catch (Exception e) { Debug.LogWarning($"[Ingress] DCU.batt({field}): {e.Message}"); }
        return false;
    }
}
