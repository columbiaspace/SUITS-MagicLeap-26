using System;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays the current wall-clock time (CDT) and mission elapsed time.
///
/// Offline-safe: uses a hardcoded CDT offset (UTC-5) instead of relying on
/// the IANA/Windows timezone database, which is absent on Magic Leap 2.
///
/// When <see cref="initialCdtTime"/> is set (e.g. "12:30"), the clock starts
/// from that value and advances in real-time — useful when the headset's
/// system UTC clock is not NTP-synced.
/// </summary>
public class MissionTimeHUD : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI clockText;
    [SerializeField] private TextMeshProUGUI elapsedText;

    [Tooltip("Append ' CDT' to the clock readout.")]
    [SerializeField] private bool showZoneSuffix = true;

    [Tooltip("Optional CDT start time (HH:mm or HH:mm:ss). " +
             "When non-empty, the displayed clock starts from this value and " +
             "advances with the real passage of time rather than reading " +
             "DateTime.UtcNow. Set this to the current CDT time when the " +
             "headset is offline and its system clock may be wrong.")]
    [SerializeField] private string initialCdtTime = "12:30";

    // CDT = UTC − 5 hours (May–Nov in US; hardcoded to avoid IANA dependency).
    private const double CdtOffsetHours = -5.0;

    private float _enabledAtRealtime;
    private TimeSpan _manualBaseTime;
    private bool _useManualBase;

    private void OnEnable()
    {
        _enabledAtRealtime = Time.realtimeSinceStartup;
        _useManualBase = TryParseHhMm(initialCdtTime, out _manualBaseTime);
    }

    private void Update()
    {
        float elapsed = Time.realtimeSinceStartup - _enabledAtRealtime;

        if (clockText != null)
            clockText.text = BuildClockText(elapsed);

        if (elapsedText != null)
        {
            int h = (int)(elapsed / 3600);
            int m = (int)((elapsed % 3600) / 60);
            int s = (int)(elapsed % 60);
            elapsedText.text = $"T+ {h:D2}:{m:D2}:{s:D2}";
        }
    }

    private string BuildClockText(float elapsedSeconds)
    {
        DateTime display;

        if (_useManualBase)
        {
            // Advance the manually set start time at 1:1 real-time rate.
            TimeSpan current = _manualBaseTime + TimeSpan.FromSeconds(elapsedSeconds);
            // Wrap at midnight so hours stay 0–23.
            current = TimeSpan.FromSeconds(current.TotalSeconds % 86400);
            display = DateTime.Today + current;
        }
        else
        {
            // Fall back to live UTC with CDT offset.
            display = DateTime.UtcNow.AddHours(CdtOffsetHours);
        }

        string suffix = showZoneSuffix ? " CDT" : string.Empty;
        return display.ToString("HH:mm:ss") + suffix;
    }

    /// <summary>Parses "HH:mm" or "HH:mm:ss" into a TimeSpan.</summary>
    private static bool TryParseHhMm(string s, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        string[] parts = s.Trim().Split(':');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out int h) ||
            !int.TryParse(parts[1], out int m)) return false;

        int sec = 0;
        if (parts.Length >= 3) int.TryParse(parts[2], out sec);

        if (h < 0 || h > 23 || m < 0 || m > 59 || sec < 0 || sec > 59) return false;
        result = new TimeSpan(h, m, sec);
        return true;
    }
}
