using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the warning display. Each active alert gets its own colored block
/// (red = critical, yellow = warning), stacked vertically with manual positioning.
///
/// Position behaviour: the panel starts at its Inspector position (centre).
/// After <see cref="centerDuration"/> seconds it slides to <see cref="sideOffset"/>.
/// Any new or severity-changed alert slides it back to centre and restarts the timer.
/// Alerts disappearing silently remove their block with no position change.
/// </summary>
public class WarningCanvasUI : MonoBehaviour
{
    [Header("Container")]
    [SerializeField] private GameObject warningContainer;

    [Header("Entry Appearance")]
    [SerializeField] private float entrySpacing          = 4f;
    [SerializeField] private float entryPaddingX         = 12f;
    [SerializeField] private float entryPaddingY         = 6f;
    [SerializeField] private int   fontSize              = 20;
    [Tooltip("Fallback container width used for text measurement if the RectTransform width isn't available yet (e.g. first frame).")]
    [SerializeField] private float fallbackContainerWidth = 300f;

    [Header("Colours")]
    [SerializeField] private Color runningLowColor    = new Color(0.89f, 0.55f, 0.1f,  0.92f);
    [SerializeField] private Color criticallyLowColor = new Color(0.85f, 0.12f, 0.12f, 0.92f);

    [Header("Position Behaviour")]
    [Tooltip("Offset applied when sliding to the side (left = negative X, down = negative Y).")]
    [SerializeField] private Vector2 sideOffset     = new Vector2(-300f, -200f);
    [Tooltip("How long the panel stays centred before sliding to the side.")]
    [SerializeField] private float   centerDuration = 10f;
    [Tooltip("How long the slide animation takes.")]
    [SerializeField] private float   slideDuration  = 0.5f;

    // -------------------------------------------------------------------------

    private readonly List<GameObject> _activeEntries = new List<GameObject>();
    private readonly List<TssVitalsOxygenMonitor.VitalsAlert> _previousAlerts
        = new List<TssVitalsOxygenMonitor.VitalsAlert>();

    private RectTransform _containerRt;
    private Vector2       _centerPosition;
    private Coroutine     _positionCoroutine;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (warningContainer == null && transform.childCount > 0)
            warningContainer = transform.GetChild(0).gameObject;

        _containerRt    = warningContainer.GetComponent<RectTransform>();
        _centerPosition = _containerRt.anchoredPosition;

        CleanupLegacyComponents();
    }

    private void OnEnable()
    {
        TssVitalsOxygenMonitor.VitalsAlertChanged += HandleVitalsAlerts;
        ClearEntries();
        SyncWithCurrentState();
    }

    private void OnDisable()
    {
        TssVitalsOxygenMonitor.VitalsAlertChanged -= HandleVitalsAlerts;
        ClearEntries();
        StopPositionCoroutine();
    }

    private void SyncWithCurrentState()
    {
        TssVitalsOxygenMonitor monitor = FindObjectOfType<TssVitalsOxygenMonitor>();
        if (monitor == null) return;
        HandleVitalsAlerts(monitor.CurrentAlerts);
    }

    // -------------------------------------------------------------------------
    // Alert handling
    // -------------------------------------------------------------------------

    private void HandleVitalsAlerts(IReadOnlyList<TssVitalsOxygenMonitor.VitalsAlert> alerts)
    {
        bool triggerSlide = HasNewOrChangedAlerts(alerts);

        // Rebuild entries
        ClearEntries();

        // Snapshot current alerts for next comparison
        _previousAlerts.Clear();
        if (alerts != null)
            foreach (var a in alerts) _previousAlerts.Add(a);

        if (alerts == null || alerts.Count == 0)
        {
            warningContainer.SetActive(false);
            StopPositionCoroutine();
            _containerRt.anchoredPosition = _centerPosition; // reset for next appearance
            return;
        }

        warningContainer.SetActive(true);

        float yOffset = 0f;
        for (int i = 0; i < alerts.Count; i++)
        {
            float h = CreateEntry(alerts[i], yOffset);
            yOffset += h + entrySpacing;
        }

        // Trim the trailing spacing and size the container to fit
        float totalHeight = Mathf.Max(0f, yOffset - entrySpacing);
        _containerRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);

        if (triggerSlide)
        {
            StopPositionCoroutine();
            _positionCoroutine = StartCoroutine(CentreHoldThenSlideAway());
        }
    }

    /// <summary>
    /// Returns true if any alert in <paramref name="newAlerts"/> is either
    /// brand-new or has changed severity compared to <see cref="_previousAlerts"/>.
    /// Alerts that simply disappeared are ignored.
    /// </summary>
    private bool HasNewOrChangedAlerts(IReadOnlyList<TssVitalsOxygenMonitor.VitalsAlert> newAlerts)
    {
        if (newAlerts == null) return false;

        foreach (var newAlert in newAlerts)
        {
            string key   = newAlert.Rule?.label ?? "O2";
            bool   found = false;

            foreach (var prev in _previousAlerts)
            {
                if ((prev.Rule?.label ?? "O2") == key)
                {
                    found = true;
                    if (prev.State != newAlert.State) return true; // severity changed
                    break;
                }
            }

            if (!found) return true; // brand-new alert
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Position coroutines
    // -------------------------------------------------------------------------

    /// <summary>
    /// Slides the panel to centre, waits, then slides it to the side.
    /// The whole sequence is one coroutine — cancelling it stops everything cleanly.
    /// </summary>
    private IEnumerator CentreHoldThenSlideAway()
    {
        yield return SlideTo(_centerPosition);
        yield return new WaitForSeconds(centerDuration);
        yield return SlideTo(_centerPosition + sideOffset);
    }

    private IEnumerator SlideTo(Vector2 target)
    {
        Vector2 start   = _containerRt.anchoredPosition;
        float   elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideDuration));
            _containerRt.anchoredPosition = Vector2.Lerp(start, target, t);
            yield return null;
        }

        _containerRt.anchoredPosition = target;
    }

    private void StopPositionCoroutine()
    {
        if (_positionCoroutine != null)
        {
            StopCoroutine(_positionCoroutine);
            _positionCoroutine = null;
        }
    }

    // -------------------------------------------------------------------------
    // Entry creation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates one alert block at the given Y offset and returns its height.
    /// Height is calculated from TMP's preferred size so long messages wrap correctly.
    /// </summary>
    private float CreateEntry(TssVitalsOxygenMonitor.VitalsAlert alert, float yOffset)
    {
        var entry = new GameObject("AlertEntry");
        entry.transform.SetParent(warningContainer.transform, false);
        _activeEntries.Add(entry);

        var rt = entry.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(0f, 0f);
        rt.offsetMax = new Vector2(0f, 0f);

        var image = entry.AddComponent<Image>();
        image.color = alert.State == TssVitalsOxygenMonitor.OxygenState.CriticallyLow
            ? criticallyLowColor
            : runningLowColor;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(entry.transform, false);

        var textRt = textGO.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2( entryPaddingX,  entryPaddingY);
        textRt.offsetMax = new Vector2(-entryPaddingX, -entryPaddingY);

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text               = alert.Headline;
        tmp.fontSize           = fontSize;
        tmp.fontStyle          = FontStyles.Bold;
        tmp.color              = Color.white;
        tmp.enableWordWrapping = true;
        tmp.alignment          = TextAlignmentOptions.Left;

        // Measure how tall this entry needs to be to fit its text
        float containerWidth = _containerRt.rect.width > 0f
            ? _containerRt.rect.width
            : fallbackContainerWidth;
        float availableWidth  = containerWidth - entryPaddingX * 2f;
        float preferredHeight = tmp.GetPreferredValues(alert.Headline, availableWidth, Mathf.Infinity).y;
        float entryHeight     = preferredHeight + entryPaddingY * 2f;

        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, entryHeight);
        rt.anchoredPosition = new Vector2(0f, -yOffset);

        return entryHeight;
    }

    // -------------------------------------------------------------------------
    // Cleanup
    // -------------------------------------------------------------------------

    private void CleanupLegacyComponents()
    {
        if (warningContainer == null) return;

        var oldImage = warningContainer.GetComponent<Image>();
        if (oldImage != null) Destroy(oldImage);

        var oldText = warningContainer.GetComponentInChildren<TextMeshProUGUI>();
        if (oldText != null) Destroy(oldText.gameObject);
    }

    private void ClearEntries()
    {
        for (int i = 0; i < _activeEntries.Count; i++)
        {
            if (_activeEntries[i] != null)
                Destroy(_activeEntries[i]);
        }
        _activeEntries.Clear();
    }
}
