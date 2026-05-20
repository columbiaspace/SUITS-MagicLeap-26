using UnityEngine;

// LTV-only: routes "next step" / "previous step" MLVoice phrases through the
// existing LtvHudController button onClick paths. One MonoBehaviour, no second
// recognizer, no duplicated step logic.
public class LTVVoiceStepControl : MonoBehaviour
{
    [SerializeField] private LtvHudController hudController;

    private void OnEnable()
    {
        VoiceIntents.LtvNextStepRequested += HandleNext;
        VoiceIntents.LtvPreviousStepRequested += HandlePrevious;
    }

    private void OnDisable()
    {
        VoiceIntents.LtvNextStepRequested -= HandleNext;
        VoiceIntents.LtvPreviousStepRequested -= HandlePrevious;
    }

    private void HandleNext()
    {
        if (hudController == null)
        {
            Debug.LogWarning("[LTVVoiceStepControl] hudController not assigned; ignoring 'next step'.", this);
            return;
        }
        hudController.InvokeCheckmark("voice");
    }

    private void HandlePrevious()
    {
        if (hudController == null)
        {
            Debug.LogWarning("[LTVVoiceStepControl] hudController not assigned; ignoring 'previous step'.", this);
            return;
        }
        hudController.InvokePrevious("voice");
    }
}
