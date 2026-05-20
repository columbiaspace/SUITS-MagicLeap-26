using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Voice commands for the Mission minimap navigation path (EVA → LTV1 / LTV2 / base).
/// Wired from <see cref="VoiceIntents"/> (MLVoice and Vosk transcripts).
/// </summary>
public class NavVoiceCoordinator : MonoBehaviour
{
    [SerializeField] private ARMinimapErica minimap;

    [Header("Trigger phrases (case-insensitive substring match)")]
    [SerializeField] private string[] goToLtv1Phrases =
    {
        "go to ltv1",
        "go to l tv 1",
        "go to the ltv1",
        "go to ltv",
        "go to l t v",
        "ltv1",
    };

    [SerializeField] private string[] goToLtv2Phrases =
    {
        "go to ltv2",
        "go to l tv 2",
        "go to the ltv2",
        "ltv2",
    };

    [SerializeField] private string[] returnPhrases =
    {
        "return to base",
        "return to home",
        "return home",
        "return to the base",
        "go to base",
        "go to start",
        "back to base",
        "rth",
        "return",
        "base",
    };

    [SerializeField] private string[] clearPathPhrases =
    {
        "clear path",
        "clear the path",
    };

    [Tooltip("Log every transcript we evaluate, even non-matching ones.")]
    public bool enableDebugLogs = true;

    /// <summary>
    /// Handles a Magic Leap nav intent by ID first (reliable even when EventName is empty),
    /// then falls back to phrase matching on <paramref name="eventName"/> or transcript text.
    /// </summary>
    public bool TryHandleNavVoiceEvent(uint eventId, string eventName)
    {
        switch (eventId)
        {
            case 119:
                return ExecuteGoToLtv1($"MLVoice id={eventId}");
            case 120:
                return ExecuteReturnToBase($"MLVoice id={eventId}");
            case 121:
                return ExecuteClearPath($"MLVoice id={eventId}");
            case 122:
                return ExecuteGoToLtv2($"MLVoice id={eventId}");
        }

        if (!string.IsNullOrWhiteSpace(eventName))
        {
            return TryHandleVoiceCommand(eventName);
        }

        return false;
    }

    /// <summary>
    /// Returns true when the transcript matched a nav command and the minimap was updated.
    /// Callers should suppress AI forwarding when this returns true.
    /// </summary>
    public bool TryHandleVoiceCommand(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return false;
        }

        string normalized = NormalizeForMatch(transcript);

        if (MatchesAny(normalized, clearPathPhrases))
        {
            return ExecuteClearPath(transcript);
        }

        // LTV2 before LTV1 so "ltv2" is not swallowed by broader LTV1 phrases.
        if (MatchesAny(normalized, goToLtv2Phrases) || MatchesLtv2Intent(normalized))
        {
            return ExecuteGoToLtv2(transcript);
        }

        if (MatchesAny(normalized, returnPhrases) || MatchesReturnIntent(normalized))
        {
            return ExecuteReturnToBase(transcript);
        }

        if (MatchesAny(normalized, goToLtv1Phrases) || MatchesLtv1Intent(normalized))
        {
            return ExecuteGoToLtv1(transcript);
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[NavVoice] No nav phrase matched. Transcript: '{transcript}'", this);
        }

        return false;
    }

    private bool ExecuteGoToLtv1(string transcript)
    {
        ARMinimapErica map = ResolveMinimap();
        if (map == null)
        {
            LogFailure("go to LTV1", "ARMinimapErica not found in scene.");
            return false;
        }

        if (!map.VoiceGoToLtv1())
        {
            LogFailure("go to LTV1", "EVA position not available yet — wait for TSS fix.");
            return false;
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[NavVoice] Path → LTV1 (transcript='{transcript}').", this);
        }

        return true;
    }

    private bool ExecuteGoToLtv2(string transcript)
    {
        ARMinimapErica map = ResolveMinimap();
        if (map == null)
        {
            LogFailure("go to LTV2", "ARMinimapErica not found in scene.");
            return false;
        }

        if (!map.VoiceGoToLtv2())
        {
            LogFailure("go to LTV2", "EVA position not available yet — wait for TSS fix.");
            return false;
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[NavVoice] Path → LTV2 (transcript='{transcript}').", this);
        }

        return true;
    }

    private bool ExecuteReturnToBase(string transcript)
    {
        ARMinimapErica map = ResolveMinimap();
        if (map == null)
        {
            LogFailure("return to base", "ARMinimapErica not found in scene.");
            return false;
        }

        if (!map.VoiceReturnToBase())
        {
            LogFailure("return to base", "EVA position not available yet — wait for TSS fix.");
            return false;
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[NavVoice] Path → base (transcript='{transcript}').", this);
        }

        return true;
    }

    private bool ExecuteClearPath(string transcript)
    {
        ARMinimapErica map = ResolveMinimap();
        if (map == null)
        {
            LogFailure("clear path", "ARMinimapErica not found in scene.");
            return false;
        }

        map.ClearVoiceNavPath();
        if (enableDebugLogs)
        {
            Debug.Log($"[NavVoice] Cleared voice nav path (transcript='{transcript}').", this);
        }

        return true;
    }

    private static bool MatchesLtv1Intent(string normalizedTranscript)
    {
        return normalizedTranscript.IndexOf("ltv1", System.StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedTranscript.IndexOf("l tv 1", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesLtv2Intent(string normalizedTranscript)
    {
        return normalizedTranscript.IndexOf("ltv2", System.StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedTranscript.IndexOf("l tv 2", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesReturnIntent(string normalizedTranscript)
    {
        if (string.IsNullOrEmpty(normalizedTranscript))
        {
            return false;
        }

        if (normalizedTranscript.IndexOf("base", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        bool mentionsReturn = normalizedTranscript.IndexOf("return", System.StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedTranscript.IndexOf("rth", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (!mentionsReturn)
        {
            return false;
        }

        return normalizedTranscript.IndexOf("home", System.StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedTranscript.IndexOf("start", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private ARMinimapErica ResolveMinimap()
    {
        if (minimap != null)
        {
            return minimap;
        }

        minimap = FindObjectOfType<ARMinimapErica>();
        return minimap;
    }

    private void LogFailure(string command, string reason)
    {
        Debug.LogWarning($"[NavVoice] '{command}' failed: {reason}", this);
    }

    private static bool MatchesAny(string normalizedTranscript, string[] phrases)
    {
        if (phrases == null || phrases.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < phrases.Length; i++)
        {
            string phrase = phrases[i];
            if (string.IsNullOrWhiteSpace(phrase))
            {
                continue;
            }

            if (normalizedTranscript.IndexOf(NormalizeForMatch(phrase), System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeForMatch(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
    }
}
