using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the warning banner. The root GameObject of this canvas should stay
/// active at all times so the script keeps running; only the child <c>warningPanel</c>
/// (the visible banner) is toggled on/off.
///
/// Listens to <see cref="TssVitalsOxygenMonitor.VitalsAlertChanged"/> so it surfaces
/// the worst-case vital across oxygen, battery, suit pressure CO2, heart rate,
/// temperature, coolant, etc.  Yellow for warning, red for critical. No mock /
/// test path — alerts only ever fire from real TSS telemetry.
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
        TssVitalsOxygenMonitor.VitalsAlertChanged += HandleVitalsAlert;
        SetPanelVisible(false);
        SyncWithCurrentState();
    }

    private void OnDisable()
    {
        TssVitalsOxygenMonitor.VitalsAlertChanged -= HandleVitalsAlert;
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

    private void SyncWithCurrentState()
    {
        TssVitalsOxygenMonitor monitor = FindObjectOfType<TssVitalsOxygenMonitor>();
        if (monitor == null) return;
        HandleVitalsAlert(monitor.CurrentAlert);
    }

    private void HandleVitalsAlert(TssVitalsOxygenMonitor.VitalsAlert alert)
    {
        switch (alert.State)
        {
            case TssVitalsOxygenMonitor.OxygenState.CriticallyLow:
                ShowWarning(criticallyLowColor, alert.Headline);
                break;

            case TssVitalsOxygenMonitor.OxygenState.RunningLow:
                ShowWarning(runningLowColor, alert.Headline);
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
        if (warningText != null && !string.IsNullOrEmpty(message))
            warningText.text = message;
    }

    private void SetPanelVisible(bool visible)
    {
        if (warningPanel != null) warningPanel.SetActive(visible);
    }
}
