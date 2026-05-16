using System;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays the current wall-clock time (Central Time / CDT) and mission elapsed time.
/// Attach to the TimeDisplay object above the CompassHud.
/// Wire clockText and elapsedText in the Inspector.
/// </summary>
public class MissionTimeHUD : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI clockText;
    [SerializeField] private TextMeshProUGUI elapsedText;

    [Tooltip("Show the current time in Central Time (auto handles CDT/CST). When off, uses the device local time.")]
    [SerializeField] private bool useCentralTime = true;

    [Tooltip("Append the zone label (CDT / CST) to the clock readout.")]
    [SerializeField] private bool showZoneSuffix = true;

    private float _startTime;
    private TimeZoneInfo _centralZone;

    private void OnEnable()
    {
        _startTime = Time.realtimeSinceStartup;
        _centralZone = ResolveCentralZone();
    }

    private void Update()
    {
        if (clockText != null)
            clockText.text = BuildClockText();

        if (elapsedText != null)
        {
            float elapsed = Time.realtimeSinceStartup - _startTime;
            int h = (int)(elapsed / 3600);
            int m = (int)((elapsed % 3600) / 60);
            int s = (int)(elapsed % 60);
            elapsedText.text = $"T+ {h:D2}:{m:D2}:{s:D2}";
        }
    }

    private string BuildClockText()
    {
        DateTime now;
        string suffix = string.Empty;

        if (useCentralTime && _centralZone != null)
        {
            now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, _centralZone);
            if (showZoneSuffix)
                suffix = " " + (_centralZone.IsDaylightSavingTime(now) ? "CDT" : "CST");
        }
        else
        {
            now = DateTime.Now;
        }

        return now.ToString("HH:mm:ss") + suffix;
    }

    private static TimeZoneInfo ResolveCentralZone()
    {
        // Try IANA name first (macOS / Linux / Android), then Windows-style.
        string[] candidates = { "America/Chicago", "Central Standard Time" };
        foreach (string id in candidates)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* try next */ }
        }
        Debug.LogWarning("[MissionTimeHUD] Could not resolve Central Time zone — falling back to system local time.");
        return null;
    }
}
