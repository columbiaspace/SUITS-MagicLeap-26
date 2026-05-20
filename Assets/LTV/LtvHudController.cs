using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using LtvDiagnostics;

public class LtvHudController : MonoBehaviour
{
    [Header("Backend Service")]
    public LtvInstructionService queueService;

    [Header("UI Elements")]
    public TextMeshProUGUI errorCodeText;   // The left box (formerly "AG NAV")
    public TextMeshProUGUI instructionText; // The middle instructional text
    public Button checkmarkButton;          // The right side button (advance step)
    public Button previousButton;           // The left of the checkmark (retreat step)
    public Image leftPanelImage;            // Background image of the left box (colored by danger level)

    [Header("Danger-level colors")]
    public Color highDangerColor = new Color(0.85f, 0.10f, 0.10f, 0.85f);   // red
    public Color mediumDangerColor = new Color(0.95f, 0.75f, 0.15f, 0.85f); // yellow
    public Color lowDangerColor = new Color(0.18f, 0.70f, 0.30f, 0.85f);    // green
    public Color idleColor = new Color(1f, 1f, 1f, 0.627f);                 // matches original BG

    [Header("Keyboard fallback (editor/device)")]
    [Tooltip("If enabled, pressing these keys triggers OnCheckmarkClicked. Works in editor and on device.")]
    public bool enableKeyboardFallback = true;
    public KeyCode[] fallbackKeys = { KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter };

    [Header("Debug")]
    public bool enableDebugLogs = true;

    public int ClickCount { get; private set; } // Exposed for PlayMode tests / proof-of-click

    private void OnEnable()
    {
        if (queueService != null)
        {
            queueService.StepChanged += OnStepChanged;
            queueService.ErrorChanged += OnErrorChanged;
            queueService.AllErrorsResolved += OnAllResolved;
        }
    }

    private void OnDisable()
    {
        if (queueService != null)
        {
            queueService.StepChanged -= OnStepChanged;
            queueService.ErrorChanged -= OnErrorChanged;
            queueService.AllErrorsResolved -= OnAllResolved;
        }
    }

    private void Start()
    {
        if (leftPanelImage != null)
        {
            leftPanelImage.color = idleColor;
        }

        if (queueService != null && !queueService.IsDiagnosisActive)
        {
            queueService.StartDiagnosisFromTss();
        }
    }

    private void Update()
    {
        if (!enableKeyboardFallback || checkmarkButton == null) return;

        for (int i = 0; i < fallbackKeys.Length; i++)
        {
            if (Input.GetKeyDown(fallbackKeys[i]))
            {
                if (enableDebugLogs)
                {
                    Debug.Log($"[LtvHud] Keyboard fallback '{fallbackKeys[i]}' pressed \u2014 invoking checkmark button.", this);
                }
                InvokeCheckmark("keyboard");
                return;
            }
        }
    }

    /// <summary>
    /// Programmatically clicks the checkmark button through the Button.onClick pipeline.
    /// Used by the keyboard fallback and PlayMode tests so every path goes through the same code.
    /// </summary>
    public void InvokeCheckmark(string source)
    {
        if (checkmarkButton == null || !checkmarkButton.interactable) return;

        if (enableDebugLogs)
        {
            Debug.Log($"[LtvHud] InvokeCheckmark source={source}", this);
        }

        checkmarkButton.onClick.Invoke();
    }

    /// Programmatic equivalent of clicking the previous-step button. Routed through
    /// Button.onClick so voice / keyboard / UI press all share one code path.
    public void InvokePrevious(string source)
    {
        if (previousButton == null || !previousButton.interactable) return;
        if (enableDebugLogs) Debug.Log($"[LtvHud] InvokePrevious source={source}", this);
        previousButton.onClick.Invoke();
    }

    // Called by the Button's OnClick UnityEvent (wired in the scene)
    public void OnCheckmarkClicked()
    {
        ClickCount++;
        if (enableDebugLogs)
        {
            Debug.Log($"[LtvHud] >>> OnCheckmarkClicked fired! (clickCount={ClickCount})", this);
        }

        if (queueService != null && queueService.IsDiagnosisActive && !queueService.IsVerifying)
        {
            queueService.AdvanceStep();
        }
    }

    // Called by the previous-step Button's OnClick UnityEvent (wired in the scene)
    public void OnPreviousClicked()
    {
        Debug.Log("[LtvHud] >>> OnPreviousClicked fired (shared button+voice path)", this);
        if (queueService != null && queueService.IsDiagnosisActive && !queueService.IsVerifying)
        {
            queueService.RetreatStep();
        }
    }

    private void OnErrorChanged(LtvError error)
    {
        if (error == null) return;

        if (errorCodeText != null)
        {
            errorCodeText.text = error.Code;
        }

        ApplyDangerColor(error.Danger);
    }

    private void OnStepChanged(LtvError error, int stepIndex)
    {
        if (error == null) return;

        if (instructionText != null && stepIndex >= 0 && stepIndex < error.Procedures.Count)
        {
            instructionText.text = error.Procedures[stepIndex];
        }

        if (checkmarkButton != null)
        {
            checkmarkButton.interactable = !queueService.IsVerifying;
        }
        if (previousButton != null)
        {
            previousButton.interactable = !queueService.IsVerifying && stepIndex > 0;
        }
    }

    private void OnAllResolved()
    {
        if (errorCodeText != null) errorCodeText.text = "DONE";
        if (instructionText != null) instructionText.text = "All LTV errors resolved. Systems nominal.";
        if (checkmarkButton != null) checkmarkButton.interactable = false;
        if (previousButton != null) previousButton.interactable = false;
        if (leftPanelImage != null) leftPanelImage.color = idleColor;
    }

    private void ApplyDangerColor(LtvDangerLevel level)
    {
        if (leftPanelImage == null) return;

        switch (level)
        {
            case LtvDangerLevel.High:
                leftPanelImage.color = highDangerColor;
                break;
            case LtvDangerLevel.Medium:
                leftPanelImage.color = mediumDangerColor;
                break;
            case LtvDangerLevel.Low:
                leftPanelImage.color = lowDangerColor;
                break;
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[LtvHud] ApplyDangerColor level={level} color={leftPanelImage.color}", this);
        }
    }
}
