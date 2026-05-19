using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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

    [Header("Completion")]
    [Tooltip("Optional speaker for spoken announcements. Auto-found in scene if left empty.")]
    [SerializeField] private ProcedureStepSpeaker stepSpeaker;
    [Tooltip("Scene to load once Ingress completes.")]
    [SerializeField] private string completionScene = "Mission";
    [Tooltip("Seconds to wait after the completion announcement before loading Mission.")]
    [SerializeField] private float completionRedirectDelay = 3f;

    [Header("UIA Sprites")]
    [SerializeField] private Sprite uiaPanelSprite;
    [SerializeField] private Sprite uiaPwrSprite;
    [SerializeField] private Sprite uiaO2VentSprite;      // UIA-O2.png — OXYGEN O2 VENT
    [SerializeField] private Sprite uiaWaterWasteSprite;  // UIA-water-waste.png

    [Header("DCU Sprites")]
    [SerializeField] private Sprite dcuPanelSprite;       // dcu.png — disconnect and non-batt controls
    [SerializeField] private Sprite dcuPumpSprite;
    [SerializeField] private Sprite dcuCo2Sprite;         // dcu-co2.png — CO₂ / scrubber
    [SerializeField] private Sprite dcuBattLocalUmbSprite; // dcu-batt-local-umb.png — BATT LOCAL / UMB
    [SerializeField] private Sprite dcuBattSecPriSprite;   // dcu-batt-sec-pri.png — BATT SEC / PRI

    private enum CondType
    {
        Timed,
        UiaBool,
        DcuBool,
        DcuBattBool,
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
    /// Binds EVA telemetry once TSS is available. Step 2 (EMU PWR) needs <see cref="OnEvaUpdated"/>;
    /// if we miss <c>OnEnable</c> because <see cref="TssUnityApiService"/> was not ready yet, we retry from <see cref="Update"/>.
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

        // 12 steps total (steps 5–9 in checklist are HMD timed waits + surrounding steps)
        _steps = new List<Step>
        {
            T("UIA and DCU: EV1 connect UIA and DCU umbilical", dcuPanelSprite),
            U("UIA: EV-1 EMU PWR – ON\n",
                uiaPwrSprite, "eva1_power", true),
            B("DCU: BATT – UMB\n",
                dcuBattLocalUmbSprite, "ps", false),
            U("UIA: OXYGEN O2 VENT – OPEN (Vent O2 tanks)\n",
                uiaO2VentSprite, "oxy_vent", true),
            T("HMD: Wait until both Primary and Secondary OXY tanks are < 10 psi\n",
                uiaPanelSprite, 3f),
            U("UIA: OXYGEN O2 VENT – CLOSE\n",
                uiaO2VentSprite, "oxy_vent", false),
            D("DCU: PUMP – OPEN (Empty water tanks)\n",
                dcuPumpSprite, "pump", true),
            U("UIA: EV-1 WASTE WATER – OPEN\n",
                uiaWaterWasteSprite, "eva1_water_waste", true),
            T("HMD: Wait until water EV1 Coolant tank is < 5%\n",
                uiaPanelSprite, 3f),
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
            _timerCo = StartCoroutine(ShowCompleteAfterDelay(3f));
            return;
        }

        var step = _steps[index];

        if (stepText != null)
            stepText.text = $"Step {index + 1} of {_steps.Count}\n{step.Label}";

        if (displayImage != null)
        {
            Sprite sprite = step.Image != null ? step.Image : uiaPanelSprite;

            sprite = ResolveDcuSprite(step, sprite);

            displayImage.sprite = sprite;
        }

        AnnounceStep(index, step);

        if (step.Cond == CondType.Timed)
            _timerCo = StartCoroutine(TimedAdvance(step.Secs));
        else if (_latestData != null)
            TryAdvance();
    }

    private void AnnounceStep(int index, Step step)
    {
        if (stepSpeaker == null) stepSpeaker = FindObjectOfType<ProcedureStepSpeaker>();
        if (stepSpeaker == null || step == null || string.IsNullOrWhiteSpace(step.Label)) return;

        stepSpeaker.Announce($"Step {index + 1} of {_steps.Count}. {step.Label}");
    }

    private IEnumerator ShowCompleteAfterDelay(float secs)
    {
        yield return new WaitForSeconds(secs);
        yield return AnnounceCompletionAndRedirect();
    }

    private IEnumerator AnnounceCompletionAndRedirect()
    {
        const string completionMessage = ProcedureVoiceAnnouncements.IngressCompletion;
        if (stepText != null) stepText.text = completionMessage;

        if (stepSpeaker == null) stepSpeaker = FindObjectOfType<ProcedureStepSpeaker>();
        ProcedureVoiceAnnouncements.Announce(completionMessage, stepSpeaker);

        if (!string.IsNullOrEmpty(completionScene))
        {
            yield return new WaitForSeconds(Mathf.Max(0f, completionRedirectDelay));
            try { SceneManager.LoadScene(completionScene); }
            catch (Exception e) { Debug.LogWarning($"[Ingress] Failed to load '{completionScene}': {e.Message}"); }
        }

        _timerCo = null;
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

    private Sprite ResolveDcuSprite(Step step, Sprite sprite)
    {
        return ProcedureDcuSpriteResolver.Resolve(
            step?.Label,
            sprite,
            new ProcedureDcuSpriteResolver.Sprites
            {
                Panel = dcuPanelSprite,
                Pump = dcuPumpSprite,
                Co2 = dcuCo2Sprite,
                BattLocalUmb = dcuBattLocalUmbSprite,
                BattSecPri = dcuBattSecPriSprite,
            });
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
            CondType.UiaBool     => ReadUiaBool(step.Field) == step.Expected,
            CondType.DcuBool     => ReadDcuEva1Bool(step.Field) == step.Expected,
            CondType.DcuBattBool => ReadDcuBattBool(step.Field) == step.Expected,
            _                    => false,
        };

        if (met) Advance();
    }

    private bool ReadUiaBool(string field)
    {
        try
        {
            if (_latestData == null) return false;

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
        catch (Exception e) { Debug.LogWarning($"[Ingress] UIA({field}): {e.Message}"); }
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
