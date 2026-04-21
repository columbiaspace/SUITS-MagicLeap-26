using System;
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

    [Header("Oxygen Warning")]
    public GameObject oxygenWarningPanel;
    public TextMeshProUGUI oxygenWarningText;

    [Header("Health Panels")]
    public GameObject vitalsPanel;
    public GameObject suitPanel;

    private void OnEnable()
    {
        if (TssUnityApiService.Instance != null)
            TssUnityApiService.Instance.EvaUpdated += HandleEvaUpdate;

        TssVitalsOxygenMonitor.OxygenStateChanged += HandleOxygenStateChanged;
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
    }

    private void OnDisable()
    {
        if (TssUnityApiService.Instance != null)
            TssUnityApiService.Instance.EvaUpdated -= HandleEvaUpdate;

        TssVitalsOxygenMonitor.OxygenStateChanged -= HandleOxygenStateChanged;
    }

    // Remove this method once testing is done
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            HandleOxygenStateChanged(TssVitalsOxygenMonitor.OxygenState.Good, 85f);

        if (Input.GetKeyDown(KeyCode.Alpha2))
            HandleOxygenStateChanged(TssVitalsOxygenMonitor.OxygenState.RunningLow, 22f);

        if (Input.GetKeyDown(KeyCode.Alpha3))
            HandleOxygenStateChanged(TssVitalsOxygenMonitor.OxygenState.CriticallyLow, 8f);
    }

    private void HandleEvaUpdate(Dictionary<string, object> fullEvaData)
    {
        if (fullEvaData == null) return;

        var health = TssUnityApiService.Instance.GetHealth();
        bool online = health != null && health.ContainsKey("source_online") && (bool)health["source_online"];
        SetText(connectionStatusText, online
            ? "<color=#00FF88>CONNECTED</color>"
            : "<color=#FF4444>DISCONNECTED</color>");

        var evaData = TssUnityApiService.Instance.GetEvaById(evaId);
        if (evaData == null) return;

        var telemetry = evaData.ContainsKey("telemetry")
            ? evaData["telemetry"] as Dictionary<string, object>
            : null;

        if (telemetry == null) return;

        DisplayTelemetry(telemetry);
        UpdateOxygenWarningLive();
    }

    private void UpdateOxygenWarningLive()
    {
        if (oxygenWarningPanel == null || !oxygenWarningPanel.activeSelf) return;
        if (oxygenWarningText == null) return;

        var monitor = FindObjectOfType<TssVitalsOxygenMonitor>();
        if (monitor == null) return;

        float percent = monitor.CurrentOxygenPercent;

        switch (monitor.CurrentState)
        {
            case TssVitalsOxygenMonitor.OxygenState.CriticallyLow:
                oxygenWarningText.text = $"CRITICAL O2: {percent:F1}% — RETURN IMMEDIATELY";
                break;

            case TssVitalsOxygenMonitor.OxygenState.RunningLow:
                oxygenWarningText.text = $"O2 LOW: {percent:F1}%";
                break;
        }
    }

    private void HandleOxygenStateChanged(
        TssVitalsOxygenMonitor.OxygenState state, float percent)
    {
        if (oxygenWarningPanel == null || oxygenWarningText == null) return;

        switch (state)
        {
            case TssVitalsOxygenMonitor.OxygenState.CriticallyLow:
                oxygenWarningPanel.SetActive(true);
                oxygenWarningText.text = $"CRITICAL O2: {percent:F1}% — RETURN IMMEDIATELY";
                oxygenWarningText.color = new Color(1f, 0.2f, 0.2f);
                break;

            case TssVitalsOxygenMonitor.OxygenState.RunningLow:
                oxygenWarningPanel.SetActive(true);
                oxygenWarningText.text = $"O2 LOW: {percent:F1}%";
                oxygenWarningText.color = new Color(1f, 0.7f, 0.15f);
                break;

            default:
                oxygenWarningPanel.SetActive(false);
                if (vitalsPanel != null) vitalsPanel.SetActive(true);
                if (suitPanel != null) suitPanel.SetActive(true);
                break;
        }
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

        SetText(suitPressureOxyText,  $"Suit Pressure O2: {GetNum(t, "suit_pressure_oxy")} psi");
        SetText(suitPressureCO2Text,  $"Suit Pressure CO2: {GetNum(t, "suit_pressure_co2")} psi");
        SetText(suitPressureOtherText, $"Suit Pressure Other: {GetNum(t, "suit_pressure_other")} psi");
        SetText(suitPressureTotalText,$"Suit Pressure Total: {GetNum(t, "suit_pressure_total")} psi");
        SetText(helmetPressureCO2Text,$"Helmet CO2: {GetNum(t, "helmet_pressure_co2")} psi");

        SetText(heartRateText,        $"Heart Rate: {GetNum(t, "heart_rate")} bpm");
        SetText(temperatureText,      $"Temperature: {GetNum(t, "temperature")} °F");
        SetText(co2ProductionText,    $"CO2 Production: {GetNum(t, "co2_production")} psi/min");
        SetText(fanPrimary,        $"Fan Primary: {GetNum(t, "fan_pri_rpm")} rpm");
        SetText(fanSecondary,        $"Fan Secondary: {GetNum(t, "fan_sec_rpm")} rpm");
        SetText(scrubberPrimary,     $"Scrubber Primary: {GetNum(t, "scrubber_a_co2_storage")}%");
        SetText(scrubberSecondary,     $"Scrubber Secondary: {GetNum(t, "scrubber_b_co2_storage")}%");

        SetText(coolantStorageText,        $"Coolant Storage: {GetNum(t, "coolant_storage")}%");
        SetText(coolantLiquidPressureText, $"Coolant Liquid Pressure: {GetNum(t, "coolant_liquid_pressure")} psi");
        SetText(coolantGasPressureText,    $"Coolant Gas Pressure: {GetNum(t, "coolant_gas_pressure")} psi");

        SetText(evaElapsedTimeText, $"EVA Time: {FormatTime(GetDouble(t, "eva_elapsed_time"))}");
    }

    private void SetText(TextMeshProUGUI field, string value)
    {
        if (field != null)
            field.text = value;
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