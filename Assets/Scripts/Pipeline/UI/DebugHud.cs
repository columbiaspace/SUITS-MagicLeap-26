// Assets/Scripts/Pipeline/UI/DebugHud.cs
using UnityEngine;

/// <summary>
/// Lightweight on-screen debug overlay showing FPS, detection count,
/// and inference timing. Toggle visibility with the Inspector checkbox.
/// </summary>
public sealed class DebugHud : MonoBehaviour
{
    [SerializeField] private bool showHud = true;
    [SerializeField] private int fontSize = 14;
    [SerializeField] private Color textColor = Color.yellow;
    [SerializeField] private Color bgColor = new Color(0f, 0f, 0f, 0.6f);

    private float _fps;
    private float _fpsTimer;
    private int _frameCount;
    private int _detectionCount;
    private float _inferenceMs;
    private string _modelStatus = "not loaded";
    private Texture2D _bgTex;
    private GUIStyle _style;
    private GUIContent _reusableContent;
    private int _cachedFontSize = -1;
    private Color _cachedTextColor;

    void Awake()
    {
        _bgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _bgTex.SetPixel(0, 0, Color.white);
        _bgTex.Apply();
        _reusableContent = new GUIContent();
    }

    public void SetDetectionCount(int count)
    {
        _detectionCount = count;
    }

    public void SetInferenceMs(float ms)
    {
        _inferenceMs = ms;
    }

    public void SetModelStatus(string status)
    {
        _modelStatus = status ?? "unknown";
    }

    void Update()
    {
        _frameCount++;
        _fpsTimer += Time.unscaledDeltaTime;

        if (_fpsTimer >= 0.5f)
        {
            _fps = _frameCount / _fpsTimer;
            _frameCount = 0;
            _fpsTimer = 0f;
        }
    }

    void OnGUI()
    {
        if (!showHud) return;

        EnsureStyleCurrent();

        var text = $"FPS: {_fps:F0}\nDets: {_detectionCount}\nInf: {_inferenceMs:F1} ms\nModel: {_modelStatus}";

        _reusableContent.text = text;
        var size = _style.CalcSize(_reusableContent);

        var padding = 8f;
        var rect = new Rect(padding, padding, size.x + padding * 2, size.y + padding * 2);

        // Background
        var prevColor = GUI.color;
        GUI.color = bgColor;
        GUI.DrawTexture(rect, _bgTex);
        GUI.color = prevColor;

        // Text
        var textRect = new Rect(rect.x + padding, rect.y + padding, size.x, size.y);
        GUI.Label(textRect, text, _style);
    }

    private void EnsureStyleCurrent()
    {
        if (_style != null && _cachedFontSize == fontSize && _cachedTextColor == textColor) return;

        _style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = textColor }
        };
        _cachedFontSize = fontSize;
        _cachedTextColor = textColor;
    }

    void OnDestroy()
    {
        if (_bgTex != null)
        {
            Destroy(_bgTex);
        }
    }
}
