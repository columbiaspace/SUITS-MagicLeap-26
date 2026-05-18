using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the warning display. Each active alert gets its own colored block
/// (red = critical, yellow = warning), stacked vertically with manual positioning.
/// </summary>
public class WarningCanvasUI : MonoBehaviour
{
    [Header("Container")]
    [SerializeField] private GameObject warningContainer;

    [Header("Entry Appearance")]
    [SerializeField] private float entryHeight   = 50f;
    [SerializeField] private float entrySpacing  = 4f;
    [SerializeField] private float entryPaddingX = 12f;
    [SerializeField] private float entryPaddingY = 6f;
    [SerializeField] private int   fontSize      = 20;

    [Header("Colours")]
    [SerializeField] private Color runningLowColor    = new Color(0.89f, 0.55f, 0.1f,  0.92f);
    [SerializeField] private Color criticallyLowColor = new Color(0.85f, 0.12f, 0.12f, 0.92f);

    private readonly List<GameObject> _activeEntries = new List<GameObject>();

    private void Awake()
    {
        if (warningContainer == null && transform.childCount > 0)
            warningContainer = transform.GetChild(0).gameObject;

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
    }

    private void SyncWithCurrentState()
    {
        TssVitalsOxygenMonitor monitor = FindObjectOfType<TssVitalsOxygenMonitor>();
        if (monitor == null) return;
        HandleVitalsAlerts(monitor.CurrentAlerts);
    }

    private void HandleVitalsAlerts(IReadOnlyList<TssVitalsOxygenMonitor.VitalsAlert> alerts)
    {
        ClearEntries();

        if (alerts == null || alerts.Count == 0)
        {
            warningContainer.SetActive(false);
            return;
        }

        warningContainer.SetActive(true);

        // Resize container to fit all entries
        float totalHeight = alerts.Count * entryHeight + (alerts.Count - 1) * entrySpacing;
        var containerRt = warningContainer.GetComponent<RectTransform>();
        if (containerRt != null)
            containerRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);

        for (int i = 0; i < alerts.Count; i++)
            CreateEntry(alerts[i], i);
    }

    // -------------------------------------------------------------------------
    // Entry creation
    // -------------------------------------------------------------------------

    private void CreateEntry(TssVitalsOxygenMonitor.VitalsAlert alert, int index)
    {
        var entry = new GameObject("AlertEntry");
        entry.transform.SetParent(warningContainer.transform, false);
        _activeEntries.Add(entry);

        // Position: anchor to top-left, offset down by index
        var rt = entry.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(0f, 0f);
        rt.offsetMax = new Vector2(0f, 0f);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, entryHeight);
        rt.anchoredPosition = new Vector2(0f, -(index * (entryHeight + entrySpacing)));

        // Colored background
        var image = entry.AddComponent<Image>();
        image.color = alert.State == TssVitalsOxygenMonitor.OxygenState.CriticallyLow
            ? criticallyLowColor
            : runningLowColor;

        // Text child
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
