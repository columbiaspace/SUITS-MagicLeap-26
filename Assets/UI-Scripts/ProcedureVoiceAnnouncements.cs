using UnityEngine;

/// <summary>
/// Spoken procedure completion lines via <see cref="LunaTtsBridge"/> (same stack as
/// <see cref="VoiceIntents"/> / <see cref="SceneVoiceCoordinator"/>). Falls back to
/// <see cref="ProcedureStepSpeaker"/> when the bridge is not loaded yet.
/// </summary>
public static class ProcedureVoiceAnnouncements
{
    public const string IngressCompletion =
        "You have completed the ingress procedure, redirection to the mission dashboard.";

    public const string EgressCompletion =
        "You have completed the egress procedure, redirection to the mission dashboard.";

    public static void Announce(string message, ProcedureStepSpeaker fallbackSpeaker = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (LunaTtsBridge.Instance != null)
        {
            LunaTtsBridge.Instance.Speak(message);
            return;
        }

        if (fallbackSpeaker == null)
        {
            fallbackSpeaker = Object.FindObjectOfType<ProcedureStepSpeaker>();
        }

        fallbackSpeaker?.Announce(message);
    }
}
