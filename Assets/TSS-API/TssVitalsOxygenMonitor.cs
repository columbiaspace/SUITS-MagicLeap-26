using System;
using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;

public class TssVitalsOxygenMonitor : MonoBehaviour
{
    public enum OxygenState
    {
        Unknown,
        Good,
        RunningLow,
        CriticallyLow
    }

    public static bool IsCriticalOxygenLow { get; private set; }
    public static event Action<float> CriticalOxygenEntered;
    public static event Action<OxygenState, float> OxygenStateChanged;

    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;
    [SerializeField] private float refreshIntervalSeconds = 0.25f;
    [SerializeField] private string primaryOxygenPath = "telemetry.eva1.oxy_pri_storage";
    [SerializeField] private string secondaryOxygenPath = "telemetry.eva1.oxy_sec_storage";
    [SerializeField] private bool useLowestAvailableSource = true;

    [Header("Thresholds (%)")]
    [SerializeField] private float runningLowThresholdPercent = 30f;
    [SerializeField] private float criticalThresholdPercent = 15f;

    [Header("Popup Alert")]
    [SerializeField] private bool showPopupAlert = true;
    [SerializeField] private Vector2 popupSize = new Vector2(420f, 72f);
    [SerializeField] private Vector2 popupMargin = new Vector2(16f, 16f);

    [Header("Debug Output")]
    [SerializeField] private bool logStateTransitions = true;
    [SerializeField] private bool logEverySample = false;

    public OxygenState CurrentState { get; private set; } = OxygenState.Unknown;
    public float CurrentOxygenPercent { get; private set; }
    public string CurrentSourcePath { get; private set; } = string.Empty;
    public string LastOutputMessage { get; private set; } = "No oxygen telemetry received yet.";

    private Coroutine refreshCoroutine;
    private GUIStyle popupStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMonitorExists()
    {
        if (FindObjectOfType<TssVitalsOxygenMonitor>() != null)
        {
            return;
        }

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

        if (refreshCoroutine == null)
        {
            refreshCoroutine = StartCoroutine(RefreshLoop());
        }
    }

    private void OnDisable()
    {
        if (refreshCoroutine != null)
        {
            StopCoroutine(refreshCoroutine);
            refreshCoroutine = null;
        }

        if (tssApi != null)
        {
            tssApi.EvaUpdated -= OnEvaUpdated;
        }

        IsCriticalOxygenLow = false;
    }

    private void OnValidate()
    {
        refreshIntervalSeconds = Mathf.Max(0.05f, refreshIntervalSeconds);
        ApplyThresholdConstraints();
    }

    private IEnumerator RefreshLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.05f, refreshIntervalSeconds));
        while (true)
        {
            if (tssApi == null)
            {
                TryResolveApiService();
            }

            if (tssApi != null)
            {
                EvaluatePacket(tssApi.GetEva());
            }

            yield return wait;
        }
    }

    private void TryResolveApiService()
    {
        if (tssApi != null)
        {
            return;
        }

        tssApi = TssUnityApiService.Instance;
        if (tssApi == null)
        {
            tssApi = FindObjectOfType<TssUnityApiService>();
        }
    }

    private void OnEvaUpdated(Dictionary<string, object> packet)
    {
        EvaluatePacket(packet);
    }

    private void EvaluatePacket(Dictionary<string, object> packet)
    {
        if (packet == null || packet.Count == 0)
        {
            return;
        }

        if (!TryReadOxygenPercent(packet, out float oxygenPercent, out string sourcePath))
        {
            return;
        }

        OxygenState nextState = EvaluateState(oxygenPercent);
        OxygenState previousState = CurrentState;

        CurrentOxygenPercent = oxygenPercent;
        CurrentSourcePath = sourcePath;
        CurrentState = nextState;
        IsCriticalOxygenLow = nextState == OxygenState.CriticallyLow;
        LastOutputMessage = BuildOutputMessage(nextState, oxygenPercent, sourcePath);

        if (logEverySample)
        {
            Debug.Log($"[Vitals] {LastOutputMessage}");
        }

        if (nextState != previousState)
        {
            if (logStateTransitions)
            {
                Debug.Log($"[Vitals] Oxygen state changed: {previousState} -> {nextState}. {LastOutputMessage}");
            }

            OxygenStateChanged?.Invoke(nextState, oxygenPercent);

            if (nextState == OxygenState.CriticallyLow)
            {
                CriticalOxygenEntered?.Invoke(oxygenPercent);
            }
        }
    }

    private bool TryReadOxygenPercent(
        Dictionary<string, object> packet,
        out float oxygenPercent,
        out string sourcePath)
    {
        oxygenPercent = 0f;
        sourcePath = string.Empty;

        bool hasPrimary = TryGetFloatFromPath(packet, primaryOxygenPath, out float primary);
        bool hasSecondary = TryGetFloatFromPath(packet, secondaryOxygenPath, out float secondary);

        if (useLowestAvailableSource && hasPrimary && hasSecondary)
        {
            if (primary <= secondary)
            {
                oxygenPercent = primary;
                sourcePath = primaryOxygenPath;
            }
            else
            {
                oxygenPercent = secondary;
                sourcePath = secondaryOxygenPath;
            }

            return true;
        }

        if (hasPrimary)
        {
            oxygenPercent = primary;
            sourcePath = primaryOxygenPath;
            return true;
        }

        if (hasSecondary)
        {
            oxygenPercent = secondary;
            sourcePath = secondaryOxygenPath;
            return true;
        }

        return false;
    }

    private OxygenState EvaluateState(float oxygenPercent)
    {
        if (oxygenPercent <= criticalThresholdPercent)
        {
            return OxygenState.CriticallyLow;
        }

        if (oxygenPercent <= runningLowThresholdPercent)
        {
            return OxygenState.RunningLow;
        }

        return OxygenState.Good;
    }

    private string BuildOutputMessage(OxygenState state, float oxygenPercent, string sourcePath)
    {
        return state switch
        {
            OxygenState.Good => $"OXYGEN GOOD: {oxygenPercent:F1}% (source: {sourcePath})",
            OxygenState.RunningLow => $"OXYGEN RUNNING LOW: {oxygenPercent:F1}% (source: {sourcePath})",
            OxygenState.CriticallyLow =>
                $"CRITICAL OXYGEN: {oxygenPercent:F1}% (source: {sourcePath}) - RETURN IMMEDIATELY",
            _ => $"OXYGEN UNKNOWN: {oxygenPercent:F1}% (source: {sourcePath})"
        };
    }

    public Dictionary<string, object> GetCurrentOutput()
    {
        return new Dictionary<string, object>
        {
            { "oxygen_percent", CurrentOxygenPercent },
            { "state", CurrentState.ToString() },
            { "is_critical", CurrentState == OxygenState.CriticallyLow },
            { "source_path", CurrentSourcePath },
            { "message", LastOutputMessage },
            {
                "thresholds",
                new Dictionary<string, object>
                {
                    { "good_above_percent", runningLowThresholdPercent },
                    { "running_low_at_or_below_percent", runningLowThresholdPercent },
                    { "critical_at_or_below_percent", criticalThresholdPercent }
                }
            }
        };
    }

    private void OnGUI()
    {
        if (!showPopupAlert)
        {
            return;
        }

        if (CurrentState != OxygenState.RunningLow && CurrentState != OxygenState.CriticallyLow)
        {
            return;
        }

        if (popupStyle == null)
        {
            popupStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                padding = new RectOffset(14, 10, 8, 8)
            };
            popupStyle.normal.textColor = Color.white;
        }

        Rect rect = new Rect(
            popupMargin.x,
            Screen.height - popupSize.y - popupMargin.y,
            popupSize.x,
            popupSize.y);

        Color previousColor = GUI.color;
        GUI.color = CurrentState == OxygenState.CriticallyLow
            ? new Color(0.85f, 0.12f, 0.12f, 0.95f)
            : new Color(0.89f, 0.55f, 0.1f, 0.95f);

        GUI.Box(rect, LastOutputMessage, popupStyle);
        GUI.color = previousColor;
    }

    private bool TryGetFloatFromPath(Dictionary<string, object> source, string path, out float value)
    {
        value = 0f;
        object raw = GetPath(source, path);
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

        if (raw is string s && float.TryParse(s, out float parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static object GetPath(Dictionary<string, object> source, string path)
    {
        if (source == null || string.IsNullOrWhiteSpace(path))
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

    private void ApplyThresholdConstraints()
    {
        runningLowThresholdPercent = Mathf.Clamp(runningLowThresholdPercent, 0f, 100f);
        criticalThresholdPercent = Mathf.Clamp(criticalThresholdPercent, 0f, 100f);

        if (criticalThresholdPercent > runningLowThresholdPercent)
        {
            criticalThresholdPercent = runningLowThresholdPercent;
        }
    }
}
