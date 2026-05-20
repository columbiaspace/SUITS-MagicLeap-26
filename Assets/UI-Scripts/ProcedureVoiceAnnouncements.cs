using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Spoken procedure completion lines via <see cref="LunaTtsBridge"/> (same stack as
/// <see cref="VoiceIntents"/> / <see cref="SceneVoiceCoordinator"/>). Falls back to
/// <see cref="ProcedureStepSpeaker"/> when the bridge is not loaded yet.
/// </summary>
public static class ProcedureVoiceAnnouncements
{
    public const string IngressStart =
        "Follow the instructions at the top of the scene to complete the ingress procedure.";

    public const string EgressStart =
        "Follow the instructions at the top of the scene to complete the egress procedure.";

    public const string IngressCompletion =
        "Ingress procedure complete, redirecting to the main dashboard.";

    public const string EgressCompletion =
        "Egress procedure complete, redirecting to the main dashboard.";

    // Speech timing — both Android TextToSpeech and LunaTtsBridge use QUEUE_FLUSH, so
    // each Announce() interrupts the previous one. To gate "wait until the headset
    // finishes" we estimate the duration from character count (≈ 150 wpm) and store
    // the expected finish time; callers poll via WaitUntilFinished().
    private const float CharsPerSecond = 14f;
    private const float StartupPaddingSeconds = 0.4f;
    private const float TrailingPaddingSeconds = 0.3f;
    private const float MinSpeechSeconds = 0.8f;

    private static float _speechFinishUnscaledTime;

    public static void Announce(string message, ProcedureStepSpeaker fallbackSpeaker = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (LunaTtsBridge.Instance != null)
        {
            LunaTtsBridge.Instance.Speak(message);
        }
        else
        {
            if (fallbackSpeaker == null)
            {
                fallbackSpeaker = UnityEngine.Object.FindObjectOfType<ProcedureStepSpeaker>();
            }

            fallbackSpeaker?.Announce(message);
        }

        float duration = StartupPaddingSeconds
            + Mathf.Max(MinSpeechSeconds, message.Length / CharsPerSecond)
            + TrailingPaddingSeconds;
        _speechFinishUnscaledTime = Time.unscaledTime + duration;
    }

    /// <summary>
    /// Yields until the most recent <see cref="Announce"/> is expected to be done.
    /// Returns immediately if no announcement is pending.
    /// </summary>
    public static IEnumerator WaitUntilFinished()
    {
        while (Time.unscaledTime < _speechFinishUnscaledTime)
        {
            yield return null;
        }
    }

    /// <summary>
    /// Reformats labels like "DCU: OXY – PRI" into "turn DCU OXY to PRI" for TTS.
    /// Splits only on en-dash / em-dash with surrounding spaces so identifiers using
    /// hyphen-minus (e.g. "EV-1") stay intact. Falls back to the cleaned label when
    /// no dash separator is found.
    /// </summary>
    public static string FormatStepForSpeech(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        string clean = label.Replace('\r', ' ').Replace('\n', ' ').Trim();

        int dashIdx = clean.IndexOf(" – ", StringComparison.Ordinal);
        if (dashIdx < 0)
        {
            dashIdx = clean.IndexOf(" — ", StringComparison.Ordinal);
        }

        if (dashIdx < 0)
        {
            return clean;
        }

        string before = clean.Substring(0, dashIdx).Replace(":", " ").Trim();
        string after = clean.Substring(dashIdx + 3).Trim();

        while (before.Contains("  "))
        {
            before = before.Replace("  ", " ");
        }

        return "turn " + before + " to " + after;
    }
}
