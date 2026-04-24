using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

public class LtvInstructionDebugPanel : MonoBehaviour
{
    [Header("Optional explicit service link")]
    [SerializeField] private LtvInstructionService ltvService;

    [Header("Layout")]
    [SerializeField] private Vector2 panelSize = new Vector2(720f, 300f);
    [SerializeField] private Vector2 panelOffset = new Vector2(16f, 16f);

    private Text titleText;
    private Text instructionText;
    private Text statusText;
    private Button startButton;
    private Button nextStepButton;
    private Button stopButton;

    private LtvInstructionService subscribedService;

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateInEditorPlayMode()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (ShouldSkipAutoCreateForActiveScene())
        {
            return;
        }

        if (FindObjectOfType<LtvInstructionDebugPanel>() != null)
        {
            return;
        }

        GameObject panelRoot = new GameObject("LtvInstructionDebugPanel");
        panelRoot.AddComponent<LtvInstructionDebugPanel>();
    }

    private static bool ShouldSkipAutoCreateForActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            return false;
        }

        var path = scene.path.Replace('\\', '/');
        if (path.IndexOf("/final_scenes/", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return path.EndsWith("/Egress.unity", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/Ingress.unity", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/MIssion.unity", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/Mission.unity", StringComparison.OrdinalIgnoreCase);
    }
#endif

    private void Awake()
    {
        EnsureEventSystem();
        BuildUi();
    }

    private void OnEnable()
    {
        TryBindService();
        RefreshFromService();
    }

    private void OnDisable()
    {
        UnbindService();
    }

    private void Update()
    {
        if (ltvService != null)
        {
            UpdateButtonStates();
            return;
        }

        TryBindService();
        if (ltvService != null)
        {
            RefreshFromService();
        }
    }

    private void TryBindService()
    {
        if (ltvService == null)
        {
            ltvService = FindObjectOfType<LtvInstructionService>();
        }

        if (ltvService == null || subscribedService == ltvService)
        {
            return;
        }

        UnbindService();
        subscribedService = ltvService;
        subscribedService.InstructionUpdated += OnInstructionUpdated;
    }

    private void UnbindService()
    {
        if (subscribedService == null)
        {
            return;
        }

        subscribedService.InstructionUpdated -= OnInstructionUpdated;
        subscribedService = null;
    }

    private void OnInstructionUpdated(Dictionary<string, object> snapshot)
    {
        RenderSnapshot(snapshot);
    }

    private void RefreshFromService()
    {
        if (ltvService == null)
        {
            SetDisconnectedState();
            return;
        }

        RenderSnapshot(ltvService.GetCurrentInstruction());
    }

    private void OnStartPressed()
    {
        if (ltvService == null)
        {
            SetDisconnectedState();
            return;
        }

        ltvService.StartDiagnosisFromTss();
    }

    private void OnNextStepPressed()
    {
        if (ltvService == null)
        {
            SetDisconnectedState();
            return;
        }

        bool ok = ltvService.MarkCurrentStepDone();
        statusText.text = ok
            ? "Step advanced."
            : "No active step or verifying.";
    }

    private void OnStopPressed()
    {
        if (ltvService == null)
        {
            SetDisconnectedState();
            return;
        }

        ltvService.StopDiagnosis();
        statusText.text = "Diagnosis stopped.";
    }

    private void RenderSnapshot(Dictionary<string, object> snapshot)
    {
        bool hasError = GetBool(snapshot, "has_active_error");
        bool isVerifying = GetBool(snapshot, "verifying");
        string errorCode = GetString(snapshot, "error_code");
        string errorDesc = GetString(snapshot, "error_description");
        int priority = GetInt(snapshot, "priority");
        int stepIndex = GetInt(snapshot, "current_step_index");
        int totalSteps = GetInt(snapshot, "total_steps");
        int remaining = GetInt(snapshot, "remaining_errors");
        int retries = GetInt(snapshot, "retry_count");
        string instruction = GetString(snapshot, "next_instruction");
        if (string.IsNullOrEmpty(instruction))
        {
            instruction = GetString(snapshot, "current_instruction");
        }

        titleText.text = hasError
            ? $"LTV Repair: Error {errorCode}"
            : "LTV Repair";

        instructionText.text = string.IsNullOrEmpty(instruction)
            ? "No instruction available."
            : instruction;

        string status = hasError
            ? $"error={errorCode} ({errorDesc}) | priority={priority} | " +
              $"step={stepIndex + 1}/{totalSteps} | remaining={remaining} | retries={retries}"
            : "No active errors.";

        if (isVerifying)
        {
            status += " | VERIFYING...";
        }

        statusText.text = status;
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        if (ltvService == null)
        {
            return;
        }

        bool active = ltvService.IsDiagnosisActive;
        bool verifying = ltvService.IsVerifying;
        bool hasStep = ltvService.CurrentError != null;

        startButton.interactable = !active;
        nextStepButton.interactable = active && hasStep && !verifying;
        stopButton.interactable = active;
    }

    private void SetDisconnectedState()
    {
        titleText.text = "LTV Repair (Disconnected)";
        instructionText.text = "LtvInstructionService not found in scene.";
        statusText.text = "Add LtvInstructionService to any active GameObject.";
        startButton.interactable = false;
        nextStepButton.interactable = false;
        stopButton.interactable = false;
    }

    private void BuildUi()
    {
        Font font = GetBuiltinFont();

        GameObject canvasGo = new GameObject(
            "LtvInstructionDebugCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = CreatePanel(canvasGo.transform, panelSize, panelOffset);

        titleText = CreateText(panel, "Title", font, 24, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(16f, -12f), new Vector2(-16f, -44f));
        instructionText = CreateText(panel, "Instruction", font, 19, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(16f, -52f), new Vector2(-16f, -156f));
        statusText = CreateText(panel, "Status", font, 16, FontStyle.Italic, TextAnchor.UpperLeft, new Vector2(16f, -164f), new Vector2(-16f, -198f));

        startButton = CreateButton(panel, font, "Start Diagnosis", new Vector2(16f, 12f), new Vector2(170f, 42f), OnStartPressed);
        nextStepButton = CreateButton(panel, font, "Next Step", new Vector2(196f, 12f), new Vector2(140f, 42f), OnNextStepPressed);
        stopButton = CreateButton(panel, font, "Stop", new Vector2(346f, 12f), new Vector2(100f, 42f), OnStopPressed);
    }

    private static RectTransform CreatePanel(Transform parent, Vector2 size, Vector2 offset)
    {
        GameObject panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(parent, false);

        RectTransform rect = panelGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;

        Image bg = panelGo.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.08f, 0.12f, 0.9f);

        return rect;
    }

    private static Text CreateText(
        RectTransform parent,
        string name,
        Font font,
        int fontSize,
        FontStyle style,
        TextAnchor anchor,
        Vector2 topLeftOffset,
        Vector2 bottomRightOffset)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(topLeftOffset.x, bottomRightOffset.y);
        rect.offsetMax = new Vector2(bottomRightOffset.x, topLeftOffset.y);

        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.color = Color.white;
        text.text = string.Empty;

        return text;
    }

    private static Button CreateButton(
        RectTransform parent,
        Font font,
        string label,
        Vector2 anchoredPosition,
        Vector2 size,
        Action onClick)
    {
        GameObject go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image bg = go.GetComponent<Image>();
        bg.color = new Color(0.18f, 0.26f, 0.36f, 1f);

        Button button = go.GetComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(() => onClick?.Invoke());

        GameObject labelGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);

        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Text labelText = labelGo.GetComponent<Text>();
        labelText.font = font;
        labelText.fontSize = 18;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = Color.white;
        labelText.text = label;

        return button;
    }

    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        eventSystemGo.AddComponent<InputSystemUIInputModule>();
#else
        eventSystemGo.AddComponent<StandaloneInputModule>();
#endif
    }

    private static Font GetBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            return font;
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private static string GetString(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object value) && value != null)
        {
            return Convert.ToString(value);
        }

        return string.Empty;
    }

    private static bool GetBool(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object value))
        {
            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (value is string strValue && bool.TryParse(strValue, out bool parsed))
            {
                return parsed;
            }

            if (value is IConvertible convertible)
            {
                try
                {
                    return Math.Abs(convertible.ToDouble(null)) > double.Epsilon;
                }
                catch
                {
                }
            }
        }

        return false;
    }

    private static int GetInt(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object value))
        {
            if (value is int intValue)
            {
                return intValue;
            }

            if (value is long longValue)
            {
                if (longValue <= int.MaxValue && longValue >= int.MinValue)
                {
                    return (int)longValue;
                }
            }

            if (value is double doubleValue)
            {
                if (doubleValue <= int.MaxValue && doubleValue >= int.MinValue)
                {
                    return (int)doubleValue;
                }
            }

            if (value is string strValue && int.TryParse(strValue, out int parsed))
            {
                return parsed;
            }
        }

        return 0;
    }
}
