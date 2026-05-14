using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent (DontDestroyOnLoad) voice coordinator that maps trigger phrases to
/// scene transitions. One instance lives in the bootstrap scene (Mission) and
/// survives every subsequent <see cref="SceneManager.LoadScene(string)"/> call.
///
/// Each <see cref="SceneVoiceTransition"/> entry binds a source scene name + a
/// list of trigger phrases to a target scene + an optional spoken announcement.
/// <see cref="TryHandleVoiceCommand"/> filters the transition list by the
/// currently active scene before matching, so the same phrase can mean
/// different things in different scenes (e.g. "start mission" is only valid
/// from Ingress).
///
/// LTV-specific entry (Mission → LTV) intentionally stays on
/// <see cref="LtvVoiceCoordinator"/> because the LTV scene's bootstrapper
/// consumes its <see cref="LtvVoiceCoordinator.PendingVoiceTrigger"/> flag. The
/// LTV → Mission return direction is configured here.
/// </summary>
public class SceneVoiceCoordinator : MonoBehaviour
{
    public static SceneVoiceCoordinator Instance { get; private set; }

    [Serializable]
    public class SceneVoiceTransition
    {
        [Tooltip("Active scene name (SceneManager.GetActiveScene().name) for which this transition is valid.")]
        public string sourceSceneName;

        [Tooltip("Case-insensitive substring(s) that trigger this transition. " +
                 "Whitespace and punctuation are normalized before matching.")]
        public string[] triggerPhrases;

        [Tooltip("Scene to load. Must be in Build Settings.")]
        public string targetSceneName;

        [Tooltip("Optional sentence spoken via LunaTtsBridge before the scene loads. " +
                 "Leave blank for a silent transition.")]
        public string announcement;
    }

    [Tooltip("All voice-driven scene transitions. Filtered by current scene at runtime.")]
    public SceneVoiceTransition[] transitions;

    [Tooltip("Log every transcript we evaluate, even non-matching ones. Off in flight.")]
    public bool enableDebugLogs = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Returns true if the transcript matched a transition for the active
    /// scene and the load was queued. Callers should suppress further routing
    /// (e.g. the AI submit path) when this returns true.
    /// </summary>
    public bool TryHandleVoiceCommand(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript) || transitions == null || transitions.Length == 0)
        {
            return false;
        }

        string currentSceneName = SceneManager.GetActiveScene().name;
        string normalizedTranscript = NormalizeForMatch(transcript);

        for (int i = 0; i < transitions.Length; i++)
        {
            SceneVoiceTransition transition = transitions[i];
            if (transition == null) continue;
            if (string.IsNullOrWhiteSpace(transition.sourceSceneName)) continue;
            if (!string.Equals(transition.sourceSceneName, currentSceneName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (transition.triggerPhrases == null || transition.triggerPhrases.Length == 0) continue;
            if (string.IsNullOrWhiteSpace(transition.targetSceneName)) continue;

            for (int j = 0; j < transition.triggerPhrases.Length; j++)
            {
                string phrase = transition.triggerPhrases[j];
                if (string.IsNullOrWhiteSpace(phrase)) continue;
                if (normalizedTranscript.IndexOf(NormalizeForMatch(phrase), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (enableDebugLogs)
                {
                    Debug.Log(
                        $"[SceneVoice] Matched '{phrase}' in scene '{currentSceneName}' " +
                        $"(transcript='{transcript}'). Loading '{transition.targetSceneName}'.", this);
                }

                if (!string.IsNullOrWhiteSpace(transition.announcement))
                {
                    LunaTtsBridge.Instance?.Speak(transition.announcement);
                }

                SceneManager.LoadScene(transition.targetSceneName);
                return true;
            }
        }

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[SceneVoice] No transition matched in scene '{currentSceneName}'. " +
                $"Transcript: '{transcript}' (normalized='{normalizedTranscript}').", this);
        }

        return false;
    }

    private static string NormalizeForMatch(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
    }
}
