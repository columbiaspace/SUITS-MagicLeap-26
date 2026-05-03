using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using TssApi;

public class HealthPanelUI : MonoBehaviour
{
    [Header("Which EVA suit to display")]
    [SerializeField] private string evaId = "eva1";

    [Header("Connection Status")]
    public TextMeshProUGUI connectionStatusText;

    [Header("Battery & Power")]
    public TextMeshProUGUI primaryBatteryText;
    public TextMeshProUGUI secondaryBatteryText;

    [Header("Oxygen")]
    public TextMeshProUGUI oxyPriStorageText;
    public TextMeshProUGUI oxySecStorageText;
    public TextMeshProUGUI oxyPriPressureText;
    public TextMeshProUGUI oxySecPressureText;
    public TextMeshProUGUI oxyConsumptionText;

    [Header("Suit Pressure")]
    public TextMeshProUGUI suitPressureOxyText;
    public TextMeshProUGUI suitPressureCO2Text;
    public TextMeshProUGUI suitPressureOtherText;
    public TextMeshProUGUI suitPressureTotalText;
    public TextMeshProUGUI helmetPressureCO2Text;

    [Header("Life Support")]
    public TextMeshProUGUI heartRateText;
    public TextMeshProUGUI temperatureText;
    public TextMeshProUGUI co2ProductionText;
    public TextMeshProUGUI fanPrimary;
    public TextMeshProUGUI fanSecondary;
    public TextMeshProUGUI scrubberPrimary;
    public TextMeshProUGUI scrubberSecondary;

    [Header("Coolant")]
    public TextMeshProUGUI coolantStorageText;
    public TextMeshProUGUI coolantLiquidPressureText;
    public TextMeshProUGUI coolantGasPressureText;

    [Header("Mission")]
    public TextMeshProUGUI evaElapsedTimeText;

    [Header("Top Warning (10 second auto-hide)")]
    public GameObject oxygenWarningPanel;
    public TextMeshProUGUI oxygenWarningText;

    [Header("Persistent Warning (stays until resolved)")]
    public GameObject persistentWarningPanel;
    public TextMeshProUGUI persistentWarningText;

    private string oxygenMessage = null;
    private string batteryMessage = null;
    private Coroutine warningHideCoroutine;

    private TssVitalsOxygenMonitor _oxygenMonitor;
    private TssVitalsBatteryMonitor _batteryMonitor;

    private void OnEnable()
    {
        if (TssUnityApiService.Instance != null)
            TssUnityApiService.Instance.EvaUpdated += HandleEvaUpdate;

        TssVitalsOxygenMonitor.OxygenStateChanged += HandleOxygenStateChanged;
        TssVitalsBatteryMonitor.BatteryStateChanged += HandleBatteryStateChanged;
    }

    private void Start()
    {
        if (TssUnityApiService.Instance != null)
        {
            TssUnityApiService.Instance.EvaUpdated -= HandleEvaUpdate;
            TssUnityApiService.Instance.EvaUpdated += HandleEvaUpdate;
            Debug.Log("[HealthPanel] Subscribed to TssUnityApiService");
        }
        else
        {
            Debug.LogError("[HealthPanel] TssUnityApiService.Instance is NULL — is the GameObject in the scene?");
        }

        _oxygenMonitor = FindObjectOfType<TssVitalsOxygenMonitor>();
        _batteryMonitor = FindObjectOfType<TssVitalsBatteryMonitor>();
    }

    private void OnDisable()
    {
        if (TssUnityApiService.Instance != null)
            TssUnityApiService.Instance.EvaUpdated -= HandleEvaUpdate;

        TssVitalsOxygenMonitor.OxygenStateChanged -= HandleOxygenStateChanged;
        TssVitalsBatteryMonitor.BatteryStateChanged -= HandleBatteryStateChanged;
    }

    private void HandleEvaUpdate(Dictionary<string, object> fullEvaData)
    {
        if (fullEvaData == null) return;
        if (TssUnityApiService.Instance == null) return;

        var health = TssUnityApiService.Instance.GetHealth();
        bool online = health != null && health.ContainsKey("source_online") && (bool)health["source_online"];
        SetText(connectionStatusText, online
            ? "<color=#00FF88>CONNECTED</color>"
            : "<color=#FF4444>DISCONNECTED</color>");

        var evaData = TssUnityApiService.Instance.GetEvaById(evaId);
        if (evaData == null) return;

        var telemetry = evaData.ContainsKey("telemetry") ? evaData["telemetry"] as Dictionary<string, object> : null;
        if (telemetry == null) return;

        DisplayTelemetry(telemetry);
        UpdateWarningsLive();
    }

    private void UpdateWarningsLive()
    {
        if (_oxygenMonitor != null
            && _oxygenMonitor.CurrentState != TssVitalsOxygenMonitor.OxygenState.Good
            && _oxygenMonitor.CurrentState != TssVitalsOxygenMonitor.OxygenState.Unknown)
        {
            HandleOxygenStateChanged(_oxygenMonitor.CurrentState, _oxygenMonitor.CurrentOxygenPercent, isStateChange: false);
        }

        if (_batteryMonitor != null
            && _batteryMonitor.CurrentState != TssVitalsBatteryMonitor.BatteryState.Good
            && _batteryMonitor.CurrentState != TssVitalsBatteryMonitor.BatteryState.Unknown)
        {
            HandleBatteryStateChanged(_batteryMonitor.CurrentState, _batteryMonitor.CurrentBatteryPercent, isStateChange: false);
        }
    }

    private void HandleOxygenStateChanged(TssVitalsOxygenMonitor.OxygenState state, float percent)
    {
        HandleOxygenStateChanged(state, percent, isStateChange: true);
    }

    private void HandleOxygenStateChanged(
        TssVitalsOxygenMonitor.OxygenState state, float percent, bool isStateChange)
    {
        switch (state)
        {
            case TssVitalsOxygenMonitor.OxygenState.CriticallyLow:
                oxygenMessage = $"<color=#FF3333>CRITICAL O2: {percent:F1}%</color>";
                break;
            case TssVitalsOxygenMonitor.OxygenState.RunningLow:
                oxygenMessage = $"<color=#FFB326>O2 LOW: {percent:F1}%</color>";
                break;
            default:
                oxygenMessage = null;
                break;
        }
        RefreshWarningPanels(isStateChange);
    }

    private void HandleBatteryStateChanged(TssVitalsBatteryMonitor.BatteryState state, float percent)
    {
        HandleBatteryStateChanged(state, percent, isStateChange: true);
    }

    private void HandleBatteryStateChanged(
        TssVitalsBatteryMonitor.BatteryState state, float percent, bool isStateChange)
    {
        switch (state)
        {
            case TssVitalsBatteryMonitor.BatteryState.CriticallyLow:
                batteryMessage = $"<color=#FF3333>CRITICAL BATTERY: {percent:F1}%</color>";
                break;
            case TssVitalsBatteryMonitor.BatteryState.RunningLow:
                batteryMessage = $"<color=#FFB326>BATTERY LOW: {percent:F1}%</color>";
                break;
            default:
                batteryMessage = null;
                break;
        }
        RefreshWarningPanels(isStateChange);
    }

    private void RefreshWarningPanels(bool isStateChange)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(oxygenMessage)) lines.Add(oxygenMessage);
        if (!string.IsNullOrEmpty(batteryMessage)) lines.Add(batteryMessage);

        if (oxygenWarningPanel != null && oxygenWarningText != null)
        {
            if (lines.Count == 0)
            {
                oxygenWarningPanel.SetActive(false);
            }
            else if (isStateChange)
            {
                oxygenWarningPanel.SetActive(true);
                oxygenWarningText.color = Color.white;
                oxygenWarningText.text = string.Join("\n", lines);
                RestartHideTimer();
            }
            else if (oxygenWarningPanel.activeSelf)
            {
                oxygenWarningText.text = string.Join("\n", lines);
            }
        }

        if (persistentWarningPanel != null && persistentWarningText != null)
        {
            if (lines.Count == 0)
            {
                persistentWarningPanel.SetActive(false);
            }
            else
            {
                persistentWarningPanel.SetActive(true);
                persistentWarningText.color = Color.white;
                persistentWarningText.text = string.Join("\n", lines);
            }
        }
    }

    private void RestartHideTimer()
    {
        if (warningHideCoroutine != null) StopCoroutine(warningHideCoroutine);
        warningHideCoroutine = StartCoroutine(HideTopPanelAfterDelay(10f));
    }

    private IEnumerator HideTopPanelAfterDelay(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (oxygenWarningPanel != null) oxygenWarningPanel.SetActive(false);
    }

    private void DisplayTelemetry(Dictionary<string, object> t)
    {
        if (evaId == "eva1")
        {
            SetText(primaryBatteryText,   $"Primary Battery: {GetNum(t, "primary_battery_level")}%");
            SetText(secondaryBatteryText, $"Secondary Battery: {GetNum(t, "secondary_battery_level")}%");
        }
        else
        {
            SetText(primaryBatteryText,   $"Battery: {GetNum(t, "battery_level")}%");
            SetText(secondaryBatteryText, "N/A");
        }

        SetText(oxyPriStorageText,    $"O2 Primary Storage: {GetNum(t, "oxy_pri_storage")}%");
        SetText(oxySecStorageText,    $"O2 Secondary Storage: {GetNum(t, "oxy_sec_storage")}%");
        SetText(oxyPriPressureText,   $"O2 Primary Pressure: {GetNum(t, "oxy_pri_pressure")} psi");
        SetText(oxySecPressureText,   $"O2 Secondary Pressure: {GetNum(t, "oxy_sec_pressure")} psi");
        SetText(oxyConsumptionText,   $"O2 Consumption: {GetNum(t, "oxy_consumption")} psi/min");

        SetText(suitPressureOxyText,  $"O2: {GetNum(t, "suit_pressure_oxy")} psi");
        SetText(suitPressureCO2Text,  $"CO2: {GetNum(t, "suit_pressure_co2")} psi");
        SetText(suitPressureOtherText, $"Other: {GetNum(t, "suit_pressure_other")} psi");
        SetText(suitPressureTotalText,$"Total: {GetNum(t, "suit_pressure_total")} psi");
        SetText(helmetPressureCO2Text,$"Helmet CO2: {GetNum(t, "helmet_pressure_co2")} psi");

        SetText(heartRateText,        $"Heart Rate: {GetNum(t, "heart_rate")} bpm");
        SetText(temperatureText,      $"Temperature: {GetNum(t, "temperature")} °F");
        SetText(co2ProductionText,    $"CO2 Production: {GetNum(t, "co2_production")} psi/min");
        SetText(fanPrimary,           $"Fan Primary: {GetNum(t, "fan_pri_rpm")} rpm");
        SetText(fanSecondary,         $"Fan Secondary: {GetNum(t, "fan_sec_rpm")} rpm");
        SetText(scrubberPrimary,      $"Scrubber Primary: {GetNum(t, "scrubber_a_co2_storage")}%");
        SetText(scrubberSecondary,    $"Scrubber Secondary: {GetNum(t, "scrubber_b_co2_storage")}%");

        SetText(coolantStorageText,        $"Coolant Storage: {GetNum(t, "coolant_storage")}%");
        SetText(coolantLiquidPressureText, $"Coolant Liquid Pressure: {GetNum(t, "coolant_liquid_pressure")} psi");
        SetText(coolantGasPressureText,    $"Coolant Gas Pressure: {GetNum(t, "coolant_gas_pressure")} psi");

        SetText(evaElapsedTimeText, $"EVA Time: {FormatTime(GetDouble(t, "eva_elapsed_time"))}");
    }

    private void SetText(TextMeshProUGUI field, string value)
    {
        if (field != null) field.text = value;
    }

    private string GetNum(Dictionary<string, object> dict, string key)
    {
        if (dict != null && dict.TryGetValue(key, out object val) && val != null)
        {
            try { return Convert.ToDouble(val).ToString("F2"); }
            catch { return Convert.ToString(val); }
        }
        return "---";
    }

    private double GetDouble(Dictionary<string, object> dict, string key)
    {
        if (dict != null && dict.TryGetValue(key, out object val) && val != null)
        {
            try { return Convert.ToDouble(val); }
            catch { return 0; }
        }
        return 0;
    }

    private string FormatTime(double totalSeconds)
    {
        int hours   = (int)(totalSeconds / 3600);
        int minutes = (int)((totalSeconds % 3600) / 60);
        int seconds = (int)(totalSeconds % 60);
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }
}