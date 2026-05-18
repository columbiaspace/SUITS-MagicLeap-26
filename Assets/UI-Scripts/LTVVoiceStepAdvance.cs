using UnityEngine;

// LTV-only: subscribes to the "next step" MLVoice phrase via VoiceIntents and
// invokes the checkmark button's existing onClick path. No second recognizer.
public class LTVVoiceStepAdvance : MonoBehaviour
{
    [SerializeField] private LtvHudController hudController;

    private void OnEnable()
    {
        VoiceIntents.LtvNextStepRequested += HandleNextStep;
    }

    private void OnDisable()
    {
        VoiceIntents.LtvNextStepRequested -= HandleNextStep;
    }

    private void HandleNextStep()
    {
        if (hudController == null)
        {
            Debug.LogWarning("[LTVVoiceStepAdvance] hudController not assigned; ignoring 'next step' voice phrase.", this);
            return;
        }

        hudController.InvokeCheckmark("voice");
    }
}
