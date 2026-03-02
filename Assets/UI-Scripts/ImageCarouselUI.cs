using System;
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
    [SerializeField] private List<EvaStepRule> stepRules = new List<EvaStepRule>();

    private int index;
    private int stepIndex;
    private bool? lastCurrentStepPassed;

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
        stepIndex = 0;
        lastCurrentStepPassed = null;
        Refresh();
    }

    private void OnEnable()
    {
        if (!autoProgressEnabled || tssApi == null)
        {
            return;
        }

        tssApi.EvaUpdated += OnPacketUpdated;
    }

    private void OnDisable()
    {
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

    private void AdvanceIfReady(Dictionary<string, object> packet)
    {
        if (stepRules.Count == 0 || stepIndex >= stepRules.Count)
        {
            return;
        }

        EvaStepRule currentRule = stepRules[stepIndex];
        bool currentPass = EvaluateRule(currentRule, packet);

        if (!lastCurrentStepPassed.HasValue)
        {
            lastCurrentStepPassed = currentPass;
            return;
        }

        // Advance only on false -> true transition for the current step.
        if (lastCurrentStepPassed.Value || !currentPass)
        {
            lastCurrentStepPassed = currentPass;
            return;
        }

        stepIndex++;
        lastCurrentStepPassed = null;

        if (slides.Count == 0)
        {
            return;
        }

        index = Mathf.Min(stepIndex, slides.Count - 1);
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
            new EvaStepRule { name = "Batt umbilical selected", field = EvaField.DcuEva1BattUmb, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "Depress enabled", field = EvaField.UiaDepress, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "O2 vent open", field = EvaField.UiaOxyVent, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "Primary O2 tank below 10", field = EvaField.Eva1OxyPriStorage, comparison = StepComparison.FloatLessOrEqual, targetValue = 10f },
            new EvaStepRule { name = "Secondary O2 tank below 10", field = EvaField.Eva1OxySecStorage, comparison = StepComparison.FloatLessOrEqual, targetValue = 10f },
            new EvaStepRule { name = "O2 vent closed", field = EvaField.UiaOxyVent, comparison = StepComparison.BoolIsFalse },
            new EvaStepRule { name = "Primary O2 pressure above 3000 psi", field = EvaField.Eva1OxyPriPressure, comparison = StepComparison.FloatGreaterOrEqual, targetValue = 3000f },
            new EvaStepRule { name = "Secondary O2 pressure above 3000 psi", field = EvaField.Eva1OxySecPressure, comparison = StepComparison.FloatGreaterOrEqual, targetValue = 3000f },
            new EvaStepRule { name = "Suit pressure O2 near 4", field = EvaField.Eva1SuitPressureOxy, comparison = StepComparison.FloatNear, targetValue = 4f, tolerance = 0.1f },
            new EvaStepRule { name = "Depress pump off", field = EvaField.UiaDepress, comparison = StepComparison.BoolIsFalse },
            new EvaStepRule { name = "Batt local selected", field = EvaField.DcuEva1BattLocal, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "EV1 EMU power off", field = EvaField.UiaEva1Power, comparison = StepComparison.BoolIsFalse },
            new EvaStepRule { name = "Verify fan primary", field = EvaField.DcuEva1Fan, comparison = StepComparison.BoolIsTrue },
            new EvaStepRule { name = "Verify CO2-A", field = EvaField.DcuEva1Co2, comparison = StepComparison.BoolIsTrue }
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

