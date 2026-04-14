using System;
using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class ImageCarouselUI : MonoBehaviour
{
    private enum EvaField
    {
        MissionStarted,
        UiaEva1Power,
        UiaEva1Oxy,
        UiaEva1WaterSupply,
        UiaEva1WaterWaste,
        UiaDepress,
        UiaOxyVent,
        DcuEva1BattUmb,
        DcuEva1BattLocal,
        DcuEva1Oxy,
        DcuEva1Fan,
        DcuEva1Pump,
        DcuEva1Co2,
        Eva1OxyPriStorage,
        Eva1OxySecStorage,
        Eva1OxyPriPressure,
        Eva1OxySecPressure,
        Eva1SuitPressureOxy
    }

    private enum StepComparison
    {
        BoolIsTrue,
        BoolIsFalse,
        FloatGreaterOrEqual,
        FloatLessOrEqual,
        FloatNear
    }

    [Serializable]
    private class EvaStepRule
    {
        public string name = "Step";
        public EvaField field = EvaField.MissionStarted;
        public StepComparison comparison = StepComparison.BoolIsTrue;
        public float targetValue = 1f;
        public float tolerance = 0.1f;
    }

    [SerializeField] private Image displayImage;
    [SerializeField] private List<Sprite> slides = new List<Sprite>();

    [Tooltip("After every step rule passes, show this slide (e.g. SUITS_UIA_PANEL). -1 = last entry in Slides.")]
    [SerializeField] private int idleSlideIndexAfterComplete = -1;

    [Tooltip("While the mission / first rule is still catching up, show this slide (usually index 1 = first UIA highlight).")]
    [SerializeField] private int firstActiveSlideIndex = 1;

    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;

    [Header("Auto Progress (EVA1)")]
    [SerializeField] private bool autoProgressEnabled = true;
    [SerializeField] private float stateSyncIntervalSeconds = 0.2f;
    [SerializeField] private List<EvaStepRule> stepRules = new List<EvaStepRule>();

    [Header("Debug")]
    [Tooltip("Canvas Text element to display live UIA status (top of panel)")]
    [SerializeField] private Text statusText;
    [Tooltip("Also show OnGUI screen overlay (useful when statusText is not assigned)")]
    [SerializeField] private bool showDebugOverlay = true;

    /// <summary>True once all step rules have been satisfied in sequence this session.</summary>
    public bool IsComplete { get; private set; }

    private int index;
    private Coroutine syncCoroutine;
    private string _debugText = "UIA Debug: waiting for data…";
    private float _debugLogTimer;

    private void Awake()
    {
        if (displayImage == null)
        {
            displayImage = GetComponent<Image>();
        }

        if (tssApi == null)
        {
            tssApi = TssUnityApiService.Instance;
        }

        if (tssApi == null)
        {
            tssApi = FindObjectOfType<TssUnityApiService>();
        }

        if (stepRules.Count == 0)
        {
            LoadDefaultEva1Rules();
        }

        index = Mathf.Clamp(firstActiveSlideIndex, 0, Mathf.Max(0, slides.Count - 1));
        Refresh();
    }

    private void OnEnable()
    {
        if (!autoProgressEnabled || tssApi == null)
        {
            Debug.LogWarning($"[UIACarousel] OnEnable skipped — autoProgress={autoProgressEnabled}, tssApi={(tssApi == null ? "NULL" : tssApi.name)}");
            return;
        }

        Debug.Log($"[UIACarousel] OnEnable — subscribed to tssApi '{tssApi.name}'");
        tssApi.EvaUpdated += OnPacketUpdated;
        SyncFromApiState();
        syncCoroutine = StartCoroutine(SyncLoop());
    }

    private void OnDisable()
    {
        if (syncCoroutine != null)
        {
            StopCoroutine(syncCoroutine);
            syncCoroutine = null;
        }

        if (tssApi != null)
        {
            tssApi.EvaUpdated -= OnPacketUpdated;
        }
    }

    private void OnPacketUpdated(Dictionary<string, object> packet)
    {
        if (packet == null)
        {
            return;
        }

        AdvanceIfReady(packet);
    }

    private IEnumerator SyncLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.05f, stateSyncIntervalSeconds));
        while (true)
        {
            SyncFromApiState();
            yield return wait;
        }
    }

    private void SyncFromApiState()
    {
        if (tssApi == null)
        {
            Debug.LogWarning("[UIACarousel] tssApi is null — cannot sync.");
            return;
        }

        Dictionary<string, object> eva = tssApi.GetEva();
        if (eva == null || eva.Count == 0)
        {
            Debug.LogWarning("[UIACarousel] GetEva() returned null/empty.");
            return;
        }

        AdvanceIfReady(eva);
    }

    private void AdvanceIfReady(Dictionary<string, object> packet)
    {
        if (stepRules.Count == 0 || slides.Count == 0) return;

        int completed = 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[UIACarousel] EVA packet evaluation:");
        for (int i = 0; i < stepRules.Count; i++)
        {
            bool result = EvaluateRule(stepRules[i], packet);
            string label = string.IsNullOrEmpty(stepRules[i].name) ? stepRules[i].field.ToString() : stepRules[i].name;
            sb.AppendLine($"  [{(result ? "✓" : "✗")}] {label}");
            if (result) completed++;
            else break;
        }

        int newIndex;
        if (completed >= stepRules.Count)
        {
            IsComplete = true;
            int idle = idleSlideIndexAfterComplete >= 0 ? idleSlideIndexAfterComplete : slides.Count - 1;
            newIndex = Mathf.Clamp(idle, 0, slides.Count - 1);
        }
        else
        {
            // Show the NEXT step slide: completed=0 → slide[1], completed=1 → slide[2], etc.
            // slide[0] is the overview shown only at idle/complete; slide[1..N] are the step instructions.
            newIndex = Mathf.Clamp(completed + 1, 0, slides.Count - 1);
        }

        sb.Append($"  completed={completed}/{stepRules.Count}  slide={newIndex}");

        // Always update overlay text
        _debugText = sb.ToString();
        if (statusText != null)
            statusText.text = _debugText;

        // Log to Console once a second and on slide changes
        _debugLogTimer -= Time.deltaTime;
        bool indexChanged = newIndex != index;
        if (indexChanged || _debugLogTimer <= 0f)
        {
            Debug.Log(_debugText);
            _debugLogTimer = 1f;
        }

        index = newIndex;
        Refresh();
    }

    private void OnGUI()
    {
        if (!showDebugOverlay || statusText != null) return;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 18,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };
        style.normal.textColor = Color.white;

        float w = 420f, h = 200f;
        GUI.Box(new Rect(10, 10, w, h), _debugText, style);
    }

    private bool EvaluateRule(EvaStepRule rule, Dictionary<string, object> packet)
    {
        if (TryGetBoolValue(rule.field, packet, out bool boolValue))
        {
            return rule.comparison switch
            {
                StepComparison.BoolIsTrue => boolValue,
                StepComparison.BoolIsFalse => !boolValue,
                _ => false
            };
        }

        if (TryGetFloatValue(rule.field, packet, out float floatValue))
        {
            return rule.comparison switch
            {
                StepComparison.FloatGreaterOrEqual => floatValue >= rule.targetValue,
                StepComparison.FloatLessOrEqual => floatValue <= rule.targetValue,
                StepComparison.FloatNear => Mathf.Abs(floatValue - rule.targetValue) <= Mathf.Abs(rule.tolerance),
                _ => false
            };
        }

        return false;
    }

    private static bool TryGetBoolValue(EvaField field, Dictionary<string, object> packet, out bool value)
    {
        value = false;
        if (packet == null)
        {
            return false;
        }

        string path = field switch
        {
            EvaField.MissionStarted => "status.started",
            EvaField.UiaEva1Power => "uia.eva1_power",
            EvaField.UiaEva1Oxy => "uia.eva1_oxy",
            EvaField.UiaEva1WaterSupply => "uia.eva1_water_supply",
            EvaField.UiaEva1WaterWaste => "uia.eva1_water_waste",
            EvaField.UiaDepress => "uia.depress",
            EvaField.UiaOxyVent => "uia.oxy_vent",
            EvaField.DcuEva1BattUmb => "dcu.eva1.batt.lu",
            EvaField.DcuEva1BattLocal => "dcu.eva1.batt.ps",
            EvaField.DcuEva1Oxy => "dcu.eva1.oxy",
            EvaField.DcuEva1Fan => "dcu.eva1.fan",
            EvaField.DcuEva1Pump => "dcu.eva1.pump",
            EvaField.DcuEva1Co2 => "dcu.eva1.co2",
            _ => null
        };

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        object raw = GetPath(packet, path);
        if (raw == null)
        {
            return false;
        }

        if (raw is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        if (raw is string strValue && bool.TryParse(strValue, out bool parsedBool))
        {
            value = parsedBool;
            return true;
        }

        if (raw is IConvertible convertible)
        {
            try
            {
                value = Math.Abs(convertible.ToDouble(null)) > double.Epsilon;
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryGetFloatValue(EvaField field, Dictionary<string, object> packet, out float value)
    {
        value = 0f;
        if (packet == null)
        {
            return false;
        }

        string path = field switch
        {
            EvaField.Eva1OxyPriStorage => "telemetry.eva1.oxy_pri_storage",
            EvaField.Eva1OxySecStorage => "telemetry.eva1.oxy_sec_storage",
            EvaField.Eva1OxyPriPressure => "telemetry.eva1.oxy_pri_pressure",
            EvaField.Eva1OxySecPressure => "telemetry.eva1.oxy_sec_pressure",
            EvaField.Eva1SuitPressureOxy => "telemetry.eva1.suit_pressure_oxy",
            _ => null
        };

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        object raw = GetPath(packet, path);
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

        if (raw is string s && float.TryParse(s, out float parsedFloat))
        {
            value = parsedFloat;
            return true;
        }

        return false;
    }

    private static object GetPath(Dictionary<string, object> source, string path)
    {
        if (source == null || string.IsNullOrEmpty(path))
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

    private void LoadDefaultEva1Rules()
    {
        stepRules = new List<EvaStepRule>
        {
            new EvaStepRule { name = "EV1 Power", field = EvaField.UiaEva1Power, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "EV1 Oxygen", field = EvaField.UiaEva1Oxy, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "EV1 Water Supply", field = EvaField.UiaEva1WaterSupply, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "EV1 Water Waste", field = EvaField.UiaEva1WaterWaste, comparison = StepComparison.BoolIsTrue }
        };
    }

    private void Refresh()
    {
        if (displayImage == null)
        {
            return;
        }

        if (slides.Count == 0)
        {
            displayImage.enabled = false;
            return;
        }

        int safeIndex = Mathf.Clamp(index, 0, slides.Count - 1);
        displayImage.enabled = true;
        displayImage.sprite = slides[safeIndex];
        displayImage.preserveAspect = true;
    }
}

