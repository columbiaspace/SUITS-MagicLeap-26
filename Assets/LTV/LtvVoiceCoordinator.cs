using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives in the Mission scene. <see cref="VoiceIntents"/> calls
/// <see cref="TryHandleVoiceCommand"/> with each transcript before forwarding
/// to the AI; if the transcript matches the LTV-repair trigger phrase we set
/// the cross-scene flag and load the LTV scene. The actual flash + TTS + scene
/// auto-return live in <see cref="LtvSceneBootstrapper"/> so the in-scene
/// camera and overlay are addressable.
/// </summary>
public class LtvVoiceCoordinator : MonoBehaviour
{
    /// <summary>Set true when the voice trigger fires; LtvSceneBootstrapper consumes and clears it.</summary>
    public static bool PendingVoiceTrigger { get; private set; }

    [Tooltip("Scene to load when the LTV-repair voice command fires. Must be in Build Settings.")]
    public string ltvSceneName = "LTV";

    [Tooltip("Case-insensitive substring(s) that trigger the LTV-repair flow. " +
             "Whitespace and punctuation are normalized before matching.")]
    public string[] triggerPhrases = { "ltv repair", "ltv-repair", "lt v repair" };

    [Tooltip("Log every transcript we evaluate, even non-matching ones. Off in flight.")]
    public bool enableDebugLogs = true;

    /// <summary>
    /// Returns true if the transcript matched and the LTV scene transition was queued.
    /// VoiceIntents should suppress the AI request when this returns true.
    /// </summary>
    public bool TryHandleVoiceCommand(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript) || triggerPhrases == null || triggerPhrases.Length == 0)
        {
            return false;
        }

        string normalized = NormalizeForMatch(transcript);

        for (int i = 0; i < triggerPhrases.Length; i++)
        {
            string phrase = triggerPhrases[i];
            if (string.IsNullOrWhiteSpace(phrase)) continue;
            if (normalized.IndexOf(NormalizeForMatch(phrase), System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[LtvVoice] Matched trigger '{phrase}' in transcript '{transcript}'. Loading scene '{ltvSceneName}'.", this);
            }

            PendingVoiceTrigger = true;
            LunaTtsBridge.Instance?.Speak("Loading LTV repair console.");
            SceneManager.LoadScene(ltvSceneName);
            return true;
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[LtvVoice] No trigger phrase matched. Transcript: '{transcript}' (normalized='{normalized}'). Falling through to AI.", this);
        }

        return false;
    }

    /// <summary>Test hook so the LTV-repair flow can be entered without a real voice trigger.</summary>
    public static void ClearPendingTrigger()
    {
        PendingVoiceTrigger = false;
    }

    private static string NormalizeForMatch(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        // Lowercase, replace runs of non-letter characters with single spaces.
        return Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
    }
}
