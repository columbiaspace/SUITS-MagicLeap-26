using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives a warning banner. The root GameObject of this canvas should stay
/// active at all times so the script keeps running; only the child <c>warningPanel</c>
/// (the visible banner) is toggled on/off.
///
/// Listens to <see cref="TssVitalsOxygenMonitor.OxygenStateChanged"/> and shows
/// yellow (RunningLow) or red (CriticallyLow) accordingly.
///
/// Mock / Test cycle
/// -----------------
/// Tick "Run Test Cycle" in the Inspector at runtime to walk through
/// Good → Yellow → Red → Good. Lets you eyeball both colours without a live TSS.
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

    [Header("Mock / Test Cycle")]
    [Tooltip("Tick at runtime to auto-cycle Good → Yellow → Red for visual QA.")]
    [SerializeField] private bool runTestCycle = false;
    [Tooltip("Seconds to spend in each state during the test cycle.")]
    [SerializeField] private float testCycleDurationSeconds = 4f;

    private TssVitalsOxygenMonitor _monitor;
    private Coroutine _testCoroutine;

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
        StopTestCycle();
    }

    private void Update()
    {
        if (runTestCycle && _testCoroutine == null)
        {
            _testCoroutine = StartCoroutine(TestCycleRoutine());
        }
        else if (!runTestCycle && _testCoroutine != null)
        {
            StopTestCycle();
        }
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

    private void StopTestCycle()
    {
        if (_testCoroutine != null)
        {
            StopCoroutine(_testCoroutine);
            _testCoroutine = null;
        }
    }

    private IEnumerator TestCycleRoutine()
    {
        _monitor = FindObjectOfType<TssVitalsOxygenMonitor>();
        if (_monitor == null)
        {
            Debug.LogWarning("[WarningCanvasUI] TestCycle: no TssVitalsOxygenMonitor found in scene.");
            _testCoroutine = null;
            runTestCycle = false;
            yield break;
        }

        float wait = Mathf.Max(1f, testCycleDurationSeconds);

        while (runTestCycle)
        {
            _monitor.SimulateMockState(TssVitalsOxygenMonitor.OxygenState.Good, 85f);
            yield return new WaitForSeconds(wait);
            if (!runTestCycle) break;

            _monitor.SimulateMockState(TssVitalsOxygenMonitor.OxygenState.RunningLow, 22f);
            yield return new WaitForSeconds(wait);
            if (!runTestCycle) break;

            _monitor.SimulateMockState(TssVitalsOxygenMonitor.OxygenState.CriticallyLow, 8f);
            yield return new WaitForSeconds(wait);
        }

        _testCoroutine = null;
    }
}
