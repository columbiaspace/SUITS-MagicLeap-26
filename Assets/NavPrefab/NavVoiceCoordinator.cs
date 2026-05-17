using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Voice commands for the Mission minimap yellow navigation path.
/// Wired from <see cref="VoiceIntents"/> (MLVoice and Vosk transcripts).
/// </summary>
public class NavVoiceCoordinator : MonoBehaviour
{
    [SerializeField] private ARMinimapErica minimap;

    [Header("Trigger phrases (case-insensitive substring match)")]
    [SerializeField] private string[] goToLtvPhrases =
    {
        "go to ltv",
        "go to l t v",
        "go to the ltv",
    };

    [SerializeField] private string[] returnPhrases =
    {
        "return to base",
    };

    [SerializeField] private string[] clearPathPhrases =
    {
        "clear path",
        "clear the path",
    };

    [Tooltip("Log every transcript we evaluate, even non-matching ones.")]
    public bool enableDebugLogs = true;

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

        // Most specific phrases first (e.g. "clear path" before "go to ltv").
        if (MatchesAny(normalized, clearPathPhrases))
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
                Debug.Log($"[NavVoice] Cleared yellow path (transcript='{transcript}').", this);
            }

            return true;
        }

        if (MatchesAny(normalized, goToLtvPhrases))
        {
            ARMinimapErica map = ResolveMinimap();
            if (map == null)
            {
                LogFailure("go to LTV", "ARMinimapErica not found in scene.");
                return false;
            }

            if (!map.VoiceGoToLtv())
            {
                LogFailure("go to LTV", "EVA position not available yet — wait for TSS fix.");
                return false;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[NavVoice] Yellow path → green waypoint (transcript='{transcript}').", this);
            }

            return true;
        }

        if (MatchesAny(normalized, returnPhrases))
        {
            ARMinimapErica map = ResolveMinimap();
            if (map == null)
            {
                LogFailure("return to base", "ARMinimapErica not found in scene.");
                return false;
            }

            if (!map.VoiceReturn())
            {
                LogFailure("return to base", "EVA position not available yet — wait for TSS fix.");
                return false;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[NavVoice] Yellow path → blue waypoint (transcript='{transcript}').", this);
            }

            return true;
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[NavVoice] No nav phrase matched. Transcript: '{transcript}'", this);
        }

        return false;
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
