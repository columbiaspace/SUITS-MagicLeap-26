using System.Collections;
using TMPro;
using TssApi;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Shows a persistent warning on the Mission dashboard when TSS UDP polling is offline.
/// </summary>
public class TssConnectionWarningUI : MonoBehaviour
{
    public const string OfflineMessage =
        "You are not able to establish connection with TSS server.";

    private const string MissionSceneName = "Mission";

    [SerializeField] private TssUnityApiService tssApi;
    [SerializeField] private GameObject warningPanel;
    [SerializeField] private TextMeshProUGUI warningText;
    [SerializeField] private bool onlyInMissionScene = true;
    [SerializeField] private float pollIntervalSeconds = 0.5f;

    private Coroutine _pollCo;

    private void OnEnable()
    {
        if (onlyInMissionScene &&
            !string.Equals(SceneManager.GetActiveScene().name, MissionSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
            SetVisible(false);
            return;
        }

        EnsureUi();
        ResolveTssApi();
        Subscribe();
        Refresh();
        _pollCo = StartCoroutine(PollUntilTssReady());
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (_pollCo != null)
        {
            StopCoroutine(_pollCo);
            _pollCo = null;
        }
    }

    private IEnumerator PollUntilTssReady()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.1f, pollIntervalSeconds));
        while (isActiveAndEnabled && tssApi == null)
        {
            ResolveTssApi();
            if (tssApi != null)
            {
                Subscribe();
                Refresh();
                yield break;
            }

            yield return wait;
        }
    }

    private void ResolveTssApi()
    {
        if (TssUnityApiService.Instance != null)
        {
            tssApi = TssUnityApiService.Instance;
        }
        else if (tssApi == null)
        {
            tssApi = FindObjectOfType<TssUnityApiService>();
        }
    }

    private void Subscribe()
    {
        if (tssApi == null)
        {
            return;
        }

        tssApi.SourceOnlineChanged -= OnSourceOnlineChanged;
        tssApi.SourceOnlineChanged += OnSourceOnlineChanged;
    }

    private void Unsubscribe()
    {
        if (tssApi != null)
        {
            tssApi.SourceOnlineChanged -= OnSourceOnlineChanged;
        }
    }

    private void OnSourceOnlineChanged(bool online)
    {
        SetVisible(!online);
    }

    private void Refresh()
    {
        if (tssApi == null)
        {
            SetVisible(true);
            return;
        }

        SetVisible(!tssApi.IsSourceOnline);
    }

    private void SetVisible(bool show)
    {
        EnsureUi();
        if (warningPanel != null)
        {
            warningPanel.SetActive(show);
        }

        if (warningText != null && show)
        {
            warningText.text = OfflineMessage;
        }
    }

    private void EnsureUi()
    {
        if (warningPanel != null && warningText != null)
        {
            return;
        }

        var panelGo = new GameObject("TssConnectionWarning", typeof(RectTransform));
        panelGo.transform.SetParent(transform, false);

        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 1f);
        panelRt.anchorMax = new Vector2(0.5f, 1f);
        panelRt.pivot = new Vector2(0.5f, 1f);
        panelRt.anchoredPosition = new Vector2(0f, -12f);
        panelRt.sizeDelta = new Vector2(520f, 72f);

        var image = panelGo.AddComponent<Image>();
        image.color = new Color(0.85f, 0.12f, 0.12f, 0.92f);
        image.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(panelGo.transform, false);

        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(12f, 8f);
        textRt.offsetMax = new Vector2(-12f, -8f);

        warningText = textGo.AddComponent<TextMeshProUGUI>();
        warningText.text = OfflineMessage;
        warningText.fontSize = 18f;
        warningText.fontStyle = FontStyles.Bold;
        warningText.color = Color.white;
        warningText.alignment = TextAlignmentOptions.Center;
        warningText.enableWordWrapping = true;

        warningPanel = panelGo;
        warningPanel.SetActive(false);
    }
}
