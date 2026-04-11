using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Test script: finds the existing "AG NAV" text in the scene and
/// replaces it with cycling NL descriptions + criticality colors.
/// Everything else in the HUD stays the same.
/// </summary>
public class LtvHudColorTest : MonoBehaviour
{
    private static readonly Color ColorCritical = new Color32(229, 57, 53, 220);
    private static readonly Color ColorHigh     = new Color32(251, 140, 0, 220);
    private static readonly Color ColorMedium   = new Color32(253, 216, 53, 220);
    private static readonly Color ColorLow      = new Color32(67, 160, 71, 220);

    private struct TestError
    {
        public string code;
        public string description;
        public int criticality;
        public Color color;
    }

    private List<TestError> testErrors;
    private TextMeshProUGUI agNavText;
    private Image agNavBackground;
    private int currentIndex = 0;

    private void Start()
    {
        testErrors = new List<TestError>
        {
            new TestError { code = "4761", description = "Dust Sensor",    criticality = 4, color = ColorCritical },
            new TestError { code = "4155", description = "Power Dist",     criticality = 4, color = ColorCritical },
            new TestError { code = "3700", description = "Heater Reset",   criticality = 3, color = ColorHigh },
            new TestError { code = "2900", description = "Power Bus",      criticality = 2, color = ColorMedium },
            new TestError { code = "2132", description = "Comm Link",      criticality = 2, color = ColorMedium },
            new TestError { code = "2131", description = "LiDAR",          criticality = 2, color = ColorMedium },
            new TestError { code = "2130", description = "NAV Restart",    criticality = 2, color = ColorMedium },
            new TestError { code = "2129", description = "Blown Fuse",     criticality = 2, color = ColorMedium },
            new TestError { code = "0000", description = "Recovery",       criticality = 0, color = ColorLow },
        };

        // Find the AG NAV text in the scene
        TextMeshProUGUI[] allTexts = FindObjectsOfType<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI tmp in allTexts)
        {
            if (tmp.text.Contains("AG NAV"))
            {
                agNavText = tmp;
                // Get the Image component on the parent (the background box)
                agNavBackground = tmp.GetComponentInParent<Image>();
                Debug.Log("[ColorTest] Found AG NAV text, will replace with NL descriptions.");
                break;
            }
        }

        if (agNavText == null)
        {
            Debug.LogWarning("[ColorTest] Could not find AG NAV text in scene.");
            return;
        }

        StartCoroutine(CycleErrors());
    }

    private IEnumerator CycleErrors()
    {
        while (true)
        {
            TestError err = testErrors[currentIndex];

            // Change text, shrink to fit, single line
            agNavText.text = err.description;
            agNavText.enableAutoSizing = true;
            agNavText.fontSizeMin = 8;
            agNavText.fontSizeMax = 30;
            agNavText.enableWordWrapping = false;
            agNavText.overflowMode = TextOverflowModes.Ellipsis;

            if (agNavBackground != null)
                agNavBackground.color = err.color;

            currentIndex = (currentIndex + 1) % testErrors.Count;

            yield return new WaitForSeconds(2f);
        }
    }
}
