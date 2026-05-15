using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the WarningCanvas world-space panel.
/// Listens to TssVitalsOxygenMonitor.OxygenStateChanged and shows a
/// yellow (RunningLow) or red (CriticallyLow) banner accordingly.
///
/// Mock / Test cycle
/// -----------------
/// Enable "Run Test Cycle" in the Inspector at runtime to automatically
/// walk through Good → RunningLow → CriticallyLow → Good so you can
/// verify both warning colours without needing a live TSS feed.
/// The test cycle only drives the OxygenMonitor's mock mode — all other
/// TSS data (telemetry, LTV, navigation, etc.) continues reading from
/// the real server.
/// </summary>
public class WarningCanvasUI : MonoBehaviour
{
    [Header("Panel References")]
    [Tooltip("The root WarningPanel Image whose colour changes per state.")]
    [SerializeField] private Image warningPanelImage;
    [Tooltip("The TMP text element that displays the warning message.")]
    [SerializeField] private TextMeshProUGUI warningText;

    [Header("Colours")]
    [SerializeField] private Color runningLowColor    = new Color(0.89f, 0.55f, 0.1f,  0.92f);
    [SerializeField] private Color criticallyLowColor = new Color(0.85f, 0.12f, 0.12f, 0.92f);

    [Header("Mock / Test Cycle")]
    [Tooltip("Tick at runtime to auto-cycle Good → Yellow → Red → Good for visual QA.")]
    [SerializeField] private bool runTestCycle = false;
    [Tooltip("Seconds to spend in each state during the test cycle.")]
    [SerializeField] private float testCycleDurationSeconds = 4f;

    private TssVitalsOxygenMonitor _monitor;
    private Coroutine _testCoroutine;

    private void OnEnable()
    {
        TssVitalsOxygenMonitor.OxygenStateChanged += HandleOxygenStateChanged;
        SetVisible(false);
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
                SetVisible(false);
                break;
        }
    }

    private void ShowWarning(Color color, string message)
    {
        SetVisible(true);
        if (warningPanelImage != null) warningPanelImage.color = color;
        if (warningText != null)       warningText.text         = message;
    }

    private void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    private void StopTestCycle()
    {
        if (_testCoroutine != null)
        {
            StopCoroutine(_testCoroutine);
            _testCoroutine = null;
        }

        // Restore monitor to live TSS mode when the test cycle is stopped.
        if (_monitor != null)
        {
            _monitor.SimulateMockState(TssVitalsOxygenMonitor.OxygenState.Good, 85f);
            // A brief delay then disable mock so live data takes over again.
            StartCoroutine(DisableMockAfterFrame());
        }
    }

    private IEnumerator DisableMockAfterFrame()
    {
        yield return null;
        if (_monitor != null)
        {
            // Reflect the public field so the RefreshLoop goes back to live mode.
            var field = typeof(TssVitalsOxygenMonitor)
                .GetField("mockOxygenEnabled",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(_monitor, false);
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
            // Good — panel should hide
            _monitor.SimulateMockState(TssVitalsOxygenMonitor.OxygenState.Good, 85f);
            yield return new WaitForSeconds(wait);
            if (!runTestCycle) break;

            // Yellow — RunningLow
            _monitor.SimulateMockState(TssVitalsOxygenMonitor.OxygenState.RunningLow, 22f);
            yield return new WaitForSeconds(wait);
            if (!runTestCycle) break;

            // Red — CriticallyLow
            _monitor.SimulateMockState(TssVitalsOxygenMonitor.OxygenState.CriticallyLow, 8f);
            yield return new WaitForSeconds(wait);
        }

        _testCoroutine = null;
    }
}
