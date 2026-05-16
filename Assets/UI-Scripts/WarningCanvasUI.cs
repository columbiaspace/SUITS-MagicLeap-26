using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives a warning banner. The root GameObject of this canvas should stay
/// active at all times so the script keeps running; only the child <c>warningPanel</c>
/// (the visible banner) is toggled on/off.
///
/// Listens to <see cref="TssVitalsOxygenMonitor.OxygenStateChanged"/> and shows
/// yellow (RunningLow) or red (CriticallyLow) accordingly. The banner stays
/// hidden whenever oxygen is in the Good range, so warnings only appear when
/// vitals actually drop below their thresholds.
/// </summary>
public class WarningCanvasUI : MonoBehaviour
{
    [Header("Panel References")]
    [Tooltip("The child GameObject that holds the visible banner. Toggled on/off based on state.")]
    [SerializeField] private GameObject warningPanel;
    [Tooltip("The Image whose colour reflects yellow vs red.")]
    [SerializeField] private Image warningPanelImage;
    [Tooltip("The TMP text element that displays the warning message.")]
    [SerializeField] private TextMeshProUGUI warningText;

    [Header("Colours")]
    [SerializeField] private Color runningLowColor    = new Color(0.89f, 0.55f, 0.1f,  0.92f);
    [SerializeField] private Color criticallyLowColor = new Color(0.85f, 0.12f, 0.12f, 0.92f);

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();
    }

    private void OnEnable()
    {
        TssVitalsOxygenMonitor.OxygenStateChanged += HandleOxygenStateChanged;
        SetPanelVisible(false);
    }

    private void OnDisable()
    {
        TssVitalsOxygenMonitor.OxygenStateChanged -= HandleOxygenStateChanged;
    }

    private void AutoWireReferences()
    {
        if (warningPanel == null && transform.childCount > 0)
            warningPanel = transform.GetChild(0).gameObject;

        if (warningPanelImage == null && warningPanel != null)
            warningPanelImage = warningPanel.GetComponent<Image>();

        if (warningText == null && warningPanel != null)
            warningText = warningPanel.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private void HandleOxygenStateChanged(TssVitalsOxygenMonitor.OxygenState state, float percent)
    {
        switch (state)
        {
            case TssVitalsOxygenMonitor.OxygenState.CriticallyLow:
                ShowWarning(criticallyLowColor, $"CRITICAL O2: {percent:F1}% — RETURN IMMEDIATELY");
                break;

            case TssVitalsOxygenMonitor.OxygenState.RunningLow:
                ShowWarning(runningLowColor, $"O2 LOW: {percent:F1}%");
                break;

            default:
                SetPanelVisible(false);
                break;
        }
    }

    private void ShowWarning(Color color, string message)
    {
        SetPanelVisible(true);
        if (warningPanelImage != null) warningPanelImage.color = color;
        if (warningText != null)       warningText.text         = message;
    }

    private void SetPanelVisible(bool visible)
    {
        if (warningPanel != null) warningPanel.SetActive(visible);
    }
}
