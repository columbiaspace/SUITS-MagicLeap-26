using System;
using System.Collections.Generic;
using TssApi;
using UnityEngine;
public class TssVitalsOxygenMonitor : MonoBehaviour
{
    public enum OxygenState { Unknown, Good, RunningLow, CriticallyLow }
    public static bool IsCriticalOxygenLow { get; private set; }
    public static event Action<float> CriticalOxygenEntered;
    public static event Action<OxygenState, float> OxygenStateChanged;
    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;
    [SerializeField] private string primaryOxygenPath = "telemetry.eva1.oxy_pri_storage";
    [SerializeField] private string secondaryOxygenPath = "telemetry.eva1.oxy_sec_storage";
    [SerializeField] private bool useLowestAvailableSource = true;
    [Header("Thresholds (%)")]
    [SerializeField] private float runningLowThresholdPercent = 30f;
    [SerializeField] private float criticalThresholdPercent = 15f;
    [Header("Debug Output")]
    [SerializeField] private bool logStateTransitions = true;
    [SerializeField] private bool logEverySample = false;
    public OxygenState CurrentState { get; private set; } = OxygenState.Unknown;
    public float CurrentOxygenPercent { get; private set; }
    public string CurrentSourcePath { get; private set; } = string.Empty;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMonitorExists()
    {
        if (FindObjectOfType<TssVitalsOxygenMonitor>() != null) return;
        var monitorObject = new GameObject("TssVitalsOxygenMonitor");
        DontDestroyOnLoad(monitorObject);
        monitorObject.AddComponent<TssVitalsOxygenMonitor>();
    }
    private void Awake()
    {
        ApplyThresholdConstraints();
        TryResolveApiService();
    }
    private void OnEnable()
    {
        IsCriticalOxygenLow = false;
        TryResolveApiService();
        if (tssApi != null)
        {
            tssApi.EvaUpdated += OnEvaUpdated;
            EvaluatePacket(tssApi.GetEva());
        }
    }
    private void OnDisable()
    {
        if (tssApi != null) tssApi.EvaUpdated -= OnEvaUpdated;
        IsCriticalOxygenLow = false;
    }
    private void OnValidate()
    {
        ApplyThresholdConstraints();
    }
    private void TryResolveApiService()
    {
        if (tssApi != null) return;
        tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();
    }
    private void OnEvaUpdated(Dictionary<string, object> packet) => EvaluatePacket(packet);
    private void EvaluatePacket(Dictionary<string, object> packet)
    {
        if (packet == null || packet.Count == 0) return;
        if (!TryReadOxygenPercent(packet, out float oxygenPercent, out string sourcePath)) return;
        OxygenState nextState = EvaluateState(oxygenPercent);
        OxygenState previousState = CurrentState;
        CurrentOxygenPercent = oxygenPercent;
        CurrentSourcePath = sourcePath;
        CurrentState = nextState;
        IsCriticalOxygenLow = nextState == OxygenState.CriticallyLow;
        if (logEverySample)
            Debug.Log($"[Vitals] Oxygen: {oxygenPercent:F1}% State: {nextState}");
        if (nextState != previousState)
        {
            if (logStateTransitions)
                Debug.Log($"[Vitals] Oxygen state changed: {previousState} -> {nextState} at {oxygenPercent:F1}%");
            OxygenStateChanged?.Invoke(nextState, oxygenPercent);
            if (nextState == OxygenState.CriticallyLow)
                CriticalOxygenEntered?.Invoke(oxygenPercent);
        }
    }
    private bool TryReadOxygenPercent(Dictionary<string, object> packet, out float oxygenPercent, out string sourcePath)
    {
        oxygenPercent = 0f;
        sourcePath = string.Empty;
        bool hasPrimary = TryGetFloatFromPath(packet, primaryOxygenPath, out float primary);
        bool hasSecondary = TryGetFloatFromPath(packet, secondaryOxygenPath, out float secondary);
        if (useLowestAvailableSource && hasPrimary && hasSecondary)
        {
            if (primary <= secondary) { oxygenPercent = primary; sourcePath = primaryOxygenPath; }
            else { oxygenPercent = secondary; sourcePath = secondaryOxygenPath; }
            return true;
        }
        if (hasPrimary) { oxygenPercent = primary; sourcePath = primaryOxygenPath; return true; }
        if (hasSecondary) { oxygenPercent = secondary; sourcePath = secondaryOxygenPath; return true; }
        return false;
    }
    private OxygenState EvaluateState(float oxygenPercent)
    {
        if (oxygenPercent <= criticalThresholdPercent) return OxygenState.CriticallyLow;
        if (oxygenPercent <= runningLowThresholdPercent) return OxygenState.RunningLow;
        return OxygenState.Good;
    }
    private bool TryGetFloatFromPath(Dictionary<string, object> source, string path, out float value)
    {
        value = 0f;
        object raw = GetPath(source, path);
        if (raw == null) return false;
        if (raw is float f) { value = f; return true; }
        if (raw is double d) { value = (float)d; return true; }
        if (raw is long l) { value = l; return true; }
        if (raw is int i) { value = i; return true; }
        if (raw is string s && float.TryParse(s, out float parsed)) { value = parsed; return true; }
        return false;
    }
    private static object GetPath(Dictionary<string, object> source, string path)
    {
        if (source == null || string.IsNullOrWhiteSpace(path)) return null;
        object current = source;
        foreach (var part in path.Split('.'))
        {
            if (!(current is Dictionary<string, object> dict) || !dict.TryGetValue(part, out current))
                return null;
        }
        return current;
    }
    private void ApplyThresholdConstraints()
    {
        runningLowThresholdPercent = Mathf.Clamp(runningLowThresholdPercent, 0f, 100f);
        criticalThresholdPercent = Mathf.Clamp(criticalThresholdPercent, 0f, 100f);
        if (criticalThresholdPercent > runningLowThresholdPercent)
            criticalThresholdPercent = runningLowThresholdPercent;
    }
}