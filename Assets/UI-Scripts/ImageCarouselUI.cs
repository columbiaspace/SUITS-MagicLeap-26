using System;
using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;

    [Header("Auto Progress (EVA1)")]
    [SerializeField] private bool autoProgressEnabled = true;
    [SerializeField] private float stateSyncIntervalSeconds = 0.2f;
    [SerializeField] private List<EvaStepRule> stepRules = new List<EvaStepRule>();

    private int index;
    private int completedWatermark;
    private Coroutine syncCoroutine;

    private void Awake()
    {
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

        index = 0;
        completedWatermark = 0;
        Refresh();
    }

    private void OnEnable()
    {
        if (!autoProgressEnabled || tssApi == null)
        {
            return;
        }

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
        while (autoProgressEnabled)
        {
            SyncFromApiState();
            yield return wait;
        }
    }

    private void SyncFromApiState()
    {
        if (tssApi == null)
        {
            return;
        }

        Dictionary<string, object> eva = tssApi.GetEva();
        if (eva == null || eva.Count == 0)
        {
            return;
        }

        object uiaEva1Power = GetPath(eva, "uia.eva1_power");
        Debug.Log("[SYNC] uia.eva1_power = " + uiaEva1Power);

        AdvanceIfReady(eva);
    }

    private void AdvanceIfReady(Dictionary<string, object> packet)
    {
        if (stepRules.Count == 0 || slides.Count == 0) return;
        if (packet == null || packet.Count == 0) return;

        int completed = 0;
        for (int i = 0; i < stepRules.Count; i++)
        {
            if (EvaluateRule(stepRules[i], packet)) completed++;
            else break;
        }

        if (completed > completedWatermark)
        {
            completedWatermark = completed;
        }

        index = Mathf.Clamp(completedWatermark, 0, slides.Count - 1);
        Refresh();
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
            new EvaStepRule { name = "Mission started", field = EvaField.MissionStarted, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "EV1 EMU power on", field = EvaField.UiaEva1Power, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "EV1 oxygen on", field = EvaField.UiaEva1Oxy, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "EV1 water supply on", field = EvaField.UiaEva1WaterSupply, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "EV1 water waste on", field = EvaField.UiaEva1WaterWaste, comparison = StepComparison.BoolIsTrue }
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

