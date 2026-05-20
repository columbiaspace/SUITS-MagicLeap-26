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
