using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ExpandableTaskBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private RectTransform arrowIcon;
    [SerializeField] private GameObject expandedContent;
    [SerializeField] private RectTransform panelRoot;

    [Header("Animation")]
    [SerializeField] private float arrowRotationDuration = 0.2f;

    private bool isExpanded;
    private Coroutine arrowAnimCoroutine;

    private void Awake()
    {
        if (expandedContent != null)
        {
            expandedContent.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(OnToggleClicked);
        }
    }

    private void OnDisable()
    {
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(OnToggleClicked);
        }
    }

    private void OnToggleClicked()
    {
        isExpanded = !isExpanded;

        if (expandedContent != null)
        {
            expandedContent.SetActive(isExpanded);
        }

        if (panelRoot != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRoot);
        }

        if (arrowIcon != null)
        {
            if (arrowAnimCoroutine != null)
            {
                StopCoroutine(arrowAnimCoroutine);
            }

            float targetZ = isExpanded ? 180f : 0f;
            arrowAnimCoroutine = StartCoroutine(AnimateArrow(targetZ));
        }
    }

    private IEnumerator AnimateArrow(float targetZ)
    {
        float elapsed = 0f;
        float startZ = arrowIcon.localEulerAngles.z;

        // Normalize to avoid wrapping artifacts (e.g. 350 → 180 instead of 350 → -180)
        if (startZ > 180f)
        {
            startZ -= 360f;
        }

        while (elapsed < arrowRotationDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / arrowRotationDuration);
            float currentZ = Mathf.Lerp(startZ, targetZ, t);
            arrowIcon.localEulerAngles = new Vector3(0f, 0f, currentZ);
            yield return null;
        }

        arrowIcon.localEulerAngles = new Vector3(0f, 0f, targetZ);
        arrowAnimCoroutine = null;
    }
}
