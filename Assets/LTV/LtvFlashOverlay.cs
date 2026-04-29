using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fullscreen green-gradient flash used to bookend LTV-repair (start + end).
/// Attach this to a UI Image whose RectTransform fills the canvas. The image's
/// alpha is driven from 0 -> peak -> 0 over the configured duration, with a
/// smoothstep curve so it reads as a slow gradient rather than a hard blink.
/// Call <see cref="Flash"/> from any caller; pass an action that runs after the
/// fade completes (used to chain TTS / scene transitions).
/// </summary>
[RequireComponent(typeof(Image))]
public class LtvFlashOverlay : MonoBehaviour
{
    [Tooltip("Color of the flash. Alpha here is ignored; alpha is driven by the curve.")]
    public Color flashColor = new Color(0.2f, 0.85f, 0.35f, 1f);

    [Tooltip("Total duration in seconds: half is fade-in, half is fade-out.")]
    public float duration = 1.5f;

    [Tooltip("Peak alpha at the midpoint (0..1).")]
    [Range(0f, 1f)]
    public float peakAlpha = 0.5f;

    private Image _image;
    private Coroutine _running;

    private void Awake()
    {
        _image = GetComponent<Image>();
        SetAlpha(0f);
    }

    /// <summary>Starts the flash. <paramref name="onComplete"/> runs after the fade ends. Idempotent.</summary>
    public void Flash(Action onComplete = null)
    {
        if (_running != null)
        {
            StopCoroutine(_running);
        }

        _running = StartCoroutine(FlashRoutine(onComplete));
    }

    private IEnumerator FlashRoutine(Action onComplete)
    {
        if (_image == null)
        {
            onComplete?.Invoke();
            _running = null;
            yield break;
        }

        float t = 0f;
        float total = Mathf.Max(0.05f, duration);
        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            float n = Mathf.Clamp01(t / total);
            // Triangular envelope: ramp 0->1 in first half, 1->0 in second half.
            float envelope = n < 0.5f ? n * 2f : (1f - n) * 2f;
            // Smoothstep so the gradient feels soft rather than linear.
            float smooth = envelope * envelope * (3f - 2f * envelope);
            SetAlpha(smooth * peakAlpha);
            yield return null;
        }

        SetAlpha(0f);
        _running = null;
        onComplete?.Invoke();
    }

    private void SetAlpha(float a)
    {
        if (_image == null) return;
        Color c = flashColor;
        c.a = a;
        _image.color = c;
    }
}
