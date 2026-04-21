using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TssApi;

/// <summary>
/// EVA Ingress procedure (~2 min).
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
    [SerializeField] private Sprite uiaO2VentSprite;      // UIA-O2.png — OXYGEN O2 VENT
    [SerializeField] private Sprite uiaWaterWasteSprite;  // UIA-water-waste.png

    [Header("DCU Sprites")]
    [SerializeField] private Sprite dcuPanelSprite;       // dcu.png — BATT and disconnect
    [SerializeField] private Sprite dcuPumpSprite;

    private enum CondType
    {
        Timed,
        UiaBool,
        DcuBool,
        DcuBattBool,
        TelemetryOxyBothUnder10Psi,
        TelemetryCoolantUnder5Percent
    }

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

        Step HmdOxy() => new Step
        {
            Label = "HMD: Wait until both Primary and Secondary OXY tanks are < 10 psi\n",
            Image = uiaPanelSprite,
            Cond  = CondType.TelemetryOxyBothUnder10Psi,
            Secs  = 0f
        };
        Step HmdCoolant() => new Step
        {
            Label = "HMD: Wait until water EV1 Coolant tank is < 5%\n",
            Image = uiaPanelSprite,
            Cond  = CondType.TelemetryCoolantUnder5Percent,
            Secs  = 0f
        };

        // 12 steps total
        _steps = new List<Step>
        {
            T("UIA and DCU: EV1 connect UIA and DCU umbilical"),
            U("UIA: EV-1 EMU PWR – ON\n",
                uiaPwrSprite, "eva1_power", true),
            B("DCU: BATT – UMB\n",
                dcuPanelSprite, "ps", false),
            U("UIA: OXYGEN O2 VENT – OPEN (Vent O2 tanks)\n",
                uiaO2VentSprite, "oxy_vent", true),
            HmdOxy(),
            U("UIA: OXYGEN O2 VENT – CLOSE\n",
                uiaO2VentSprite, "oxy_vent", false),
            D("DCU: PUMP – OPEN (Empty water tanks)\n",
                dcuPumpSprite, "pump", true),
            U("UIA: EV-1 WASTE WATER – OPEN\n",
                uiaWaterWasteSprite, "eva1_water_waste", true),
            HmdCoolant(),
            U("UIA: EV-1 WASTE WATER – CLOSE\n",
                uiaWaterWasteSprite, "eva1_water_waste", false),
            U("UIA: EV-1 EMU PWR – OFF\n",
                uiaPwrSprite, "eva1_power", false),
            T("DCU: EV-1 disconnect umbilical", dcuPanelSprite, 3f),
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
            CondType.UiaBool                    => ReadUiaBool(step.Field) == step.Expected,
            CondType.DcuBool                    => ReadDcuEva1Bool(step.Field) == step.Expected,
            CondType.DcuBattBool                => ReadDcuBattBool(step.Field) == step.Expected,
            CondType.TelemetryOxyBothUnder10Psi => TelemetryOxyBothUnder10Psi(),
            CondType.TelemetryCoolantUnder5Percent => TelemetryCoolantUnder5Percent(),
            _                                   => false,
        };

        if (met) Advance();
    }

    private bool TelemetryOxyBothUnder10Psi()
    {
        try
        {
            if (_latestData == null) return false;
            if (!_latestData.TryGetValue("telemetry", out var tRaw) ||
                tRaw is not Dictionary<string, object> tel) return false;
            if (!tel.TryGetValue("eva1", out var eRaw) ||
                eRaw is not Dictionary<string, object> eva1) return false;

            float pri = ReadFloat(eva1, "oxy_pri_pressure");
            float sec = ReadFloat(eva1, "oxy_sec_pressure");
            if (float.IsNaN(pri) || float.IsNaN(sec)) return false;
            return pri < 10f && sec < 10f;
        }
        catch (Exception e) { Debug.LogWarning($"[Ingress] telemetry O2: {e.Message}"); }
        return false;
    }

    private bool TelemetryCoolantUnder5Percent()
    {
        try
        {
            if (_latestData == null) return false;
            if (!_latestData.TryGetValue("telemetry", out var tRaw) ||
                tRaw is not Dictionary<string, object> tel) return false;
            if (!tel.TryGetValue("eva1", out var eRaw) ||
                eRaw is not Dictionary<string, object> eva1) return false;

            float coolant = ReadFloat(eva1, "coolant_storage");
            if (float.IsNaN(coolant)) return false;
            return coolant < 5f;
        }
        catch (Exception e) { Debug.LogWarning($"[Ingress] telemetry coolant: {e.Message}"); }
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
