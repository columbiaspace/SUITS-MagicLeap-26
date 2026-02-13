// Assets/Scripts/Pipeline/UI/StatusBorderEffect.cs
using UnityEngine;

/// <summary>
/// Full-screen green flash effect for LTV activation and TSS step/task completion.
/// Uses OnGUI + 1x1 texture pattern consistent with OverlayRenderer and DebugHud.
/// Timer runs in Update() so OnGUI's multiple-calls-per-frame doesn't skew timing.
///
/// State machine:
///   Idle ──TriggerActivation()──> Flash ──(flash ends)──> Idle
///   Idle ──TriggerCompletion()──> Flash ──(flash ends)──> Idle
///   Any  ──StopEffects()────────> Idle
/// </summary>
public sealed class StatusBorderEffect : MonoBehaviour
{
    [Header("Flash")]
    [SerializeField] private Color flashColor = new Color(0f, 1f, 0f, 0.15f);
    [SerializeField] private float flashDuration = 0.4f;

    private bool _flashing;
    private float _flashTimer;
    private float _flashAlpha;
    private Texture2D _tex;

    void Awake()
    {
        _tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _tex.SetPixel(0, 0, Color.white);
        _tex.Apply();
    }

    /// <summary>
    /// Flash green full-screen.
    /// Call when the pipeline / LTV detection becomes active.
    /// </summary>
    public void TriggerActivation()
    {
        _flashTimer = 0f;
        _flashing = true;
    }

    /// <summary>
    /// Flash green full-screen.
    /// Call on TSS step or task completion.
    /// </summary>
    public void TriggerCompletion()
    {
        _flashTimer = 0f;
        _flashing = true;
    }

    /// <summary>
    /// Immediately stop all effects.
    /// </summary>
    public void StopEffects()
    {
        _flashing = false;
    }

    void Update()
    {
        if (!_flashing) return;

        _flashTimer += Time.deltaTime;
        var t = Mathf.Clamp01(_flashTimer / flashDuration);

        _flashAlpha = flashColor.a * (1f - SmoothStep(t));

        if (t >= 1f)
        {
            _flashing = false;
            _flashAlpha = 0f;
        }
    }

    void OnGUI()
    {
        if (!_flashing || _flashAlpha <= 0f) return;

        var prevColor = GUI.color;
        GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, _flashAlpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _tex);
        GUI.color = prevColor;
    }

    private static float SmoothStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    void OnDestroy()
    {
        if (_tex != null)
        {
            Destroy(_tex);
        }
    }
}
