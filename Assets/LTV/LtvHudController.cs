using UnityEngine;
using UnityEngine.UI;
using TMPro; // Required for TextMeshPro UI
using LtvDiagnostics;

public class LtvHudController : MonoBehaviour
{
    [Header("Backend Service")]
    public LtvInstructionService queueService;

    [Header("UI Elements")]
    public TextMeshProUGUI errorCodeText;   // The left box (formerly "AG NAV")
    public TextMeshProUGUI instructionText; // The middle instructional text
    public Button checkmarkButton;          // The right side button

    private void OnEnable()
    {
        if (queueService != null)
        {
            // Subscribe to the backend events
            queueService.StepChanged += OnStepChanged;
            queueService.ErrorChanged += OnErrorChanged;
            queueService.AllErrorsResolved += OnAllResolved;
            
            // Listen for button clicks
            checkmarkButton.onClick.AddListener(OnCheckmarkClicked);
        }
    }

    private void OnDisable()
    {
        if (queueService != null)
        {
            // Unsubscribe to prevent memory leaks
            queueService.StepChanged -= OnStepChanged;
            queueService.ErrorChanged -= OnErrorChanged;
            queueService.AllErrorsResolved -= OnAllResolved;
            
            checkmarkButton.onClick.RemoveListener(OnCheckmarkClicked);
        }
    }

    private void Start()
    {
        // Kick off the diagnosis when the scene starts 
        // (You can move this to a voice command later if needed!)
        if (queueService != null && !queueService.IsDiagnosisActive)
        {
            queueService.StartDiagnosisFromTss();
        }
    }

    // 3. When the right-hand button is clicked...
    private void OnCheckmarkClicked()
    {
        // Only advance if a diagnosis is active and we aren't waiting on TSS verification
        if (queueService != null && queueService.IsDiagnosisActive && !queueService.IsVerifying)
        {
            queueService.AdvanceStep();
        }
    }

    // 1 & 4. Displays the error code (and updates when the next error starts)
    private void OnErrorChanged(LtvError error)
    {
        errorCodeText.text = error.Code;
    }

    // 2. Displays the current instruction
    private void OnStepChanged(LtvError error, int stepIndex)
    {
        instructionText.text = error.Procedures[stepIndex];

        // Disable the button temporarily if the system is verifying the fix via TSS
        checkmarkButton.interactable = !queueService.IsVerifying;
    }

    // Handles the end-state when everything is fixed
    private void OnAllResolved()
    {
        errorCodeText.text = "DONE";
        instructionText.text = "All LTV errors resolved. Systems nominal.";
        checkmarkButton.interactable = false; // Disable button since we are done
    }
}