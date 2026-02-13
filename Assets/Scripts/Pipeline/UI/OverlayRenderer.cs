// Assets/Scripts/Pipeline/UI/OverlayRenderer.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws detection bounding boxes and labels as screen-space overlays.
/// Uses OnGUI for zero-dependency rendering (no Canvas/prefab setup required).
/// Feed detections via SetDetections() each frame.
/// </summary>
public sealed class OverlayRenderer : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] private Color boxColor = Color.green;
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color textBgColor = new Color(0f, 0f, 0f, 0.7f);
    [SerializeField] private float boxThickness = 2f;
    [SerializeField] private int fontSize = 14;

    [Header("Label")]
    [SerializeField] private string[] classNames = { "component" };

    private IReadOnlyList<Detection> _detections = System.Array.Empty<Detection>();
    private int _frameWidth;
    private int _frameHeight;
    private Texture2D _boxTex;
    private GUIStyle _labelStyle;
    private GUIContent _reusableContent;
    private int _cachedFontSize = -1;
    private Color _cachedTextColor;

    void Awake()
    {
        _boxTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _boxTex.SetPixel(0, 0, Color.white);
        _boxTex.Apply();
        _reusableContent = new GUIContent();
    }

    /// <summary>
    /// Call each frame with fresh detections.
    /// Detections use pixel coordinates relative to frameWidth x frameHeight.
    /// </summary>
    public void SetDetections(IReadOnlyList<Detection> detections, int frameWidth, int frameHeight)
    {
        _detections = detections ?? System.Array.Empty<Detection>();
        _frameWidth = frameWidth;
        _frameHeight = frameHeight;
    }

    void OnGUI()
    {
        if (_detections.Count == 0 || _frameWidth <= 0 || _frameHeight <= 0) return;

        EnsureStyleCurrent();

        var scaleX = (float)Screen.width / _frameWidth;
        var scaleY = (float)Screen.height / _frameHeight;

        for (int i = 0; i < _detections.Count; i++)
        {
            DrawDetection(_detections[i], scaleX, scaleY);
        }
    }

    private void EnsureStyleCurrent()
    {
        if (_labelStyle != null && _cachedFontSize == fontSize && _cachedTextColor == textColor) return;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = textColor }
        };
        _cachedFontSize = fontSize;
        _cachedTextColor = textColor;
    }

    private void DrawDetection(Detection det, float scaleX, float scaleY)
    {
        var screenX = det.x * scaleX;
        var screenY = det.y * scaleY;
        var screenW = det.w * scaleX;
        var screenH = det.h * scaleY;

        DrawBox(screenX, screenY, screenW, screenH);
        DrawLabel(det, screenX, screenY);
    }

    private void DrawBox(float x, float y, float w, float h)
    {
        var prevColor = GUI.color;
        GUI.color = boxColor;

        var t = boxThickness;

        // Top
        GUI.DrawTexture(new Rect(x, y, w, t), _boxTex);
        // Bottom
        GUI.DrawTexture(new Rect(x, y + h - t, w, t), _boxTex);
        // Left
        GUI.DrawTexture(new Rect(x, y, t, h), _boxTex);
        // Right
        GUI.DrawTexture(new Rect(x + w - t, y, t, h), _boxTex);

        GUI.color = prevColor;
    }

    private void DrawLabel(Detection det, float screenX, float screenY)
    {
        var className = (det.classId >= 0 && det.classId < classNames.Length)
            ? classNames[det.classId]
            : $"cls_{det.classId}";

        var labelText = $"{className} {det.score:F2}";

        _reusableContent.text = labelText;
        var labelSize = _labelStyle.CalcSize(_reusableContent);
        var labelRect = new Rect(screenX, screenY - labelSize.y - 2f, labelSize.x + 8f, labelSize.y + 4f);

        // Clamp to screen so labels above the screen edge stay visible
        if (labelRect.y < 0f)
        {
            labelRect = new Rect(labelRect.x, screenY + 2f, labelRect.width, labelRect.height);
        }

        // Background
        var prevColor = GUI.color;
        GUI.color = textBgColor;
        GUI.DrawTexture(labelRect, _boxTex);
        GUI.color = prevColor;

        // Text
        var textRect = new Rect(labelRect.x + 4f, labelRect.y + 2f, labelSize.x, labelSize.y);
        GUI.Label(textRect, labelText, _labelStyle);
    }

    void OnDestroy()
    {
        if (_boxTex != null)
        {
            Destroy(_boxTex);
        }
    }
}
