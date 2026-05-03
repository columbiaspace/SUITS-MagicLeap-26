using System;
using System.Collections.Generic;
using TssApi;
using UnityEngine;

public class TssVitalsBatteryMonitor : MonoBehaviour
{
    public enum BatteryState { Unknown, Good, RunningLow, CriticallyLow }

    public static event Action<float> CriticalBatteryEntered;
    public static event Action<BatteryState, float> BatteryStateChanged;

    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;
    [SerializeField] private string primaryBatteryPath = "telemetry.eva1.primary_battery_level";
    [SerializeField] private string secondaryBatteryPath = "telemetry.eva1.secondary_battery_level";
    [SerializeField] private bool useLowestAvailableSource = true;

    [Header("Thresholds (%)")]
    [SerializeField] private float runningLowThresholdPercent = 30f;
    [SerializeField] private float criticalThresholdPercent = 15f;

    public BatteryState CurrentState { get; private set; } = BatteryState.Unknown;
    public float CurrentBatteryPercent { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMonitorExists()
    {
        if (FindObjectOfType<TssVitalsBatteryMonitor>() != null) return;
        var obj = new GameObject("TssVitalsBatteryMonitor");
        DontDestroyOnLoad(obj);
        obj.AddComponent<TssVitalsBatteryMonitor>();
    }

    private void OnEnable()
    {
        if (tssApi == null) tssApi = TssUnityApiService.Instance ?? FindObjectOfType<TssUnityApiService>();
        if (tssApi != null)
        {
            tssApi.EvaUpdated += OnEvaUpdated;
            EvaluatePacket(tssApi.GetEva());
        }
    }

    private void OnDisable()
    {
        if (tssApi != null) tssApi.EvaUpdated -= OnEvaUpdated;
    }

    private void OnEvaUpdated(Dictionary<string, object> packet) => EvaluatePacket(packet);

    private void EvaluatePacket(Dictionary<string, object> packet)
    {
        if (packet == null || packet.Count == 0) return;
        if (!TryReadPercent(packet, out float percent)) return;

        BatteryState next = EvaluateState(percent);
        BatteryState prev = CurrentState;

        CurrentBatteryPercent = percent;
        CurrentState = next;

        if (next != prev)
        {
            BatteryStateChanged?.Invoke(next, percent);
            if (next == BatteryState.CriticallyLow) CriticalBatteryEntered?.Invoke(percent);
        }
    }

    private bool TryReadPercent(Dictionary<string, object> packet, out float percent)
    {
        percent = 0f;
        bool hasPri = TryGetFloat(packet, primaryBatteryPath, out float pri);
        bool hasSec = TryGetFloat(packet, secondaryBatteryPath, out float sec);

        if (useLowestAvailableSource && hasPri && hasSec) { percent = Mathf.Min(pri, sec); return true; }
        if (hasPri) { percent = pri; return true; }
        if (hasSec) { percent = sec; return true; }
        return false;
    }

    private BatteryState EvaluateState(float percent)
    {
        if (percent <= criticalThresholdPercent) return BatteryState.CriticallyLow;
        if (percent <= runningLowThresholdPercent) return BatteryState.RunningLow;
        return BatteryState.Good;
    }

    private bool TryGetFloat(Dictionary<string, object> src, string path, out float val)
    {
        val = 0f;
        object raw = GetPath(src, path);
        if (raw == null) return false;
        try { val = Convert.ToSingle(raw); return true; }
        catch { return false; }
    }

    private static object GetPath(Dictionary<string, object> src, string path)
    {
        if (src == null || string.IsNullOrWhiteSpace(path)) return null;
        object current = src;
        foreach (var part in path.Split('.'))
        {
            if (!(current is Dictionary<string, object> dict) || !dict.TryGetValue(part, out current))
                return null;
        }
        return current;
    }
}