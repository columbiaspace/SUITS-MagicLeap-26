using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LtvDiagnostics;

public class LtvHudController : MonoBehaviour
{
    [Header("Backend Service (auto-finds if empty)")]
    public LtvInstructionService instructionService;

    // Auto-discovered UI elements from the existing scene
    private TextMeshProUGUI errorCodeText;   // The AG NAV text
    private Image errorCodeBackground;        // The AG NAV background image
    private TextMeshProUGUI instructionText;  // The instruction text
    private Button checkmarkButton;           // The checkmark button

    // Criticality colors
    private static readonly Color ColorCritical = new Color32(229, 57, 53, 220);
    private static readonly Color ColorHigh     = new Color32(251, 140, 0, 220);
    private static readonly Color ColorMedium   = new Color32(253, 216, 53, 220);
    private static readonly Color ColorLow      = new Color32(67, 160, 71, 220);

    // Original color so we can restore if needed
    private Color originalBadgeColor;

    // Natural-language short descriptions for known error codes
    private static readonly Dictionary<string, string> ShortDescriptions = new Dictionary<string, string>
    {
        {"0000", "Recovery"},
        {"2129", "Blown Fuse"},
        {"2130", "NAV Restart"},
        {"2131", "LiDAR"},
        {"2132", "Comm Link"},
        {"2900", "Power Bus"},
        {"3700", "Heater Reset"},
        {"4155", "Power Dist"},
        {"4761", "Dust Sensor"},
    };

    private void Awake()
    {
        // Auto-find the instruction service if not assigned
        if (instructionService == null)
            instructionService = FindObjectOfType<LtvInstructionService>();

        // Auto-find UI elements by searching the scene
        FindExistingUiElements();
    }

    private void FindExistingUiElements()
    {
        TextMeshProUGUI[] allTexts = FindObjectsOfType<TextMeshProUGUI>(true);

        foreach (TextMeshProUGUI tmp in allTexts)
        {
            // Find the AG NAV text (the error code display)
            if (tmp.text.Contains("AG NAV") || tmp.text.Contains("ag nav"))
            {
                errorCodeText = tmp;
                errorCodeBackground = tmp.GetComponentInParent<Image>();
                if (errorCodeBackground != null)
                    originalBadgeColor = errorCodeBackground.color;

                // Set up auto-sizing so text fits the box
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 8;
                tmp.fontSizeMax = 30;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Ellipsis;

                Debug.Log("[LtvHud] Found error code text (AG NAV).");
            }
        }

        // Find the instruction text (the longest text element with a procedure-like string)
        foreach (TextMeshProUGUI tmp in allTexts)
        {
            if (tmp != errorCodeText && tmp.text.Contains("Locate"))
            {
                instructionText = tmp;
                Debug.Log("[LtvHud] Found instruction text.");
                break;
            }
        }

        // If we didn't find instruction text by content, try finding it by length
        if (instructionText == null)
        {
            foreach (TextMeshProUGUI tmp in allTexts)
            {
                if (tmp != errorCodeText && tmp.text.Length > 20)
                {
                    instructionText = tmp;
                    Debug.Log("[LtvHud] Found instruction text (by length).");
                    break;
                }
            }
        }

        // Find the checkmark button
        Button[] allButtons = FindObjectsOfType<Button>(true);
        foreach (Button btn in allButtons)
        {
            // Look for a button with a checkmark image or that's near our UI
            Image btnImage = btn.GetComponent<Image>();
            if (btnImage != null)
            {
                checkmarkButton = btn;
                Debug.Log("[LtvHud] Found checkmark button.");
                break;
            }
        }
    }

    private void OnEnable()
    {
        if (instructionService == null) return;

        instructionService.ErrorChanged       += OnErrorChanged;
        instructionService.StepChanged        += OnStepChanged;
        instructionService.AllErrorsResolved  += OnAllResolved;
        instructionService.ResolutionFailed   += OnResolutionFailed;
        instructionService.MaxRetriesExceeded += OnMaxRetriesExceeded;

        if (checkmarkButton != null)
            checkmarkButton.onClick.AddListener(OnCheckmarkClicked);
    }

    private void OnDisable()
    {
        if (instructionService == null) return;

        instructionService.ErrorChanged       -= OnErrorChanged;
        instructionService.StepChanged        -= OnStepChanged;
        instructionService.AllErrorsResolved  -= OnAllResolved;
        instructionService.ResolutionFailed   -= OnResolutionFailed;
        instructionService.MaxRetriesExceeded -= OnMaxRetriesExceeded;

        if (checkmarkButton != null)
            checkmarkButton.onClick.RemoveListener(OnCheckmarkClicked);
    }

    private void Start()
    {
        if (instructionService != null && !instructionService.IsDiagnosisActive)
            instructionService.StartDiagnosisFromTss();
    }

    // ── Event handlers ──────────────────────────────────────────

    private void OnErrorChanged(LtvError error)
    {
        Color color = GetCriticalityColor(error.Criticality);
        string desc = GetShortDescription(error);

        // Update the AG NAV box: text + background color
        if (errorCodeText != null)
            errorCodeText.text = desc;

        if (errorCodeBackground != null)
            errorCodeBackground.color = color;
    }

    private void OnStepChanged(LtvError error, int stepIndex)
    {
        int total = error.Procedures.Count;
        string instruction = error.Procedures[stepIndex];

        if (instructionText != null)
            instructionText.text = $"{stepIndex + 1}. {instruction}";

        if (checkmarkButton != null)
            checkmarkButton.interactable = !instructionService.IsVerifying;
    }

    private void OnResolutionFailed(LtvError error)
    {
        if (instructionText != null)
        {
            int retries = instructionService.RetryCount;
            instructionText.text = $"Verification failed - retrying ({retries}/3)";
        }
    }

    private void OnMaxRetriesExceeded(LtvError error)
    {
        if (instructionText != null)
            instructionText.text = "Max retries exceeded - skipping error";
    }

    private void OnAllResolved()
    {
        if (errorCodeText != null)
            errorCodeText.text = "ALL OK";

        if (errorCodeBackground != null)
            errorCodeBackground.color = ColorLow;

        if (instructionText != null)
            instructionText.text = "All LTV errors resolved. Systems nominal.";

        if (checkmarkButton != null)
            checkmarkButton.interactable = false;
    }

    public void OnCheckmarkClicked()
    {
        if (instructionService != null && instructionService.IsDiagnosisActive && !instructionService.IsVerifying)
            instructionService.AdvanceStep();
    }

    // ── Utilities ───────────────────────────────────────────────

    private static Color GetCriticalityColor(int criticality)
    {
        if (criticality >= 4) return ColorCritical;
        if (criticality == 3) return ColorHigh;
        if (criticality == 2) return ColorMedium;
        return ColorLow;
    }

    private static string GetShortDescription(LtvError error)
    {
        if (ShortDescriptions.TryGetValue(error.Code, out string desc))
            return desc;
        return string.IsNullOrEmpty(error.Description) ? error.Code : error.Description;
    }
}
