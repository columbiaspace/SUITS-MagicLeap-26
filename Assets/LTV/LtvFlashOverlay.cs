using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fullscreen green flash used to bookend LTV-repair (start + end). At runtime this
/// spawns a tinted quad parented to the rendering camera, sized to overcover the
/// camera frustum, so the flash fills the user's view in XR rather than being
/// constrained to a world-space HUD canvas. Call <see cref="Flash"/> from any caller;
/// pass an action that runs after the fade completes (used to chain TTS / scene
/// transitions).
/// </summary>
public class LtvFlashOverlay : MonoBehaviour
{
    [Tooltip("Color of the flash. Alpha here is ignored; alpha is driven by the curve.")]
    public Color flashColor = new Color(0.2f, 0.85f, 0.35f, 1f);

    [Tooltip("Total duration in seconds: half is fade-in, half is fade-out.")]
    public float duration = 1.5f;

    [Tooltip("Peak alpha at the midpoint (0..1).")]
    [Range(0f, 1f)]
    public float peakAlpha = 0.5f;

    [Tooltip("Distance from the camera at which the fullscreen quad is placed. " +
             "Should sit just past the camera's near plane.")]
    public float quadDistance = 0.3f;

    [Tooltip("Extra coverage margin so the quad over-fills the frustum and never " +
             "shows a seam at the edges.")]
    public float coverageMargin = 1.4f;

    private Coroutine _running;
    private Camera _camera;
    private Transform _quad;
    private Material _quadMat;
    private Texture2D _quadTex;

    private void Awake()
    {
        // Legacy: this component used to drive a sibling UI Image on a small
        // world-space HUD canvas. Hide it so it doesn't show as a tiny green panel.
        var img = GetComponent<Image>();
        if (img != null)
        {
            var c = img.color;
            c.a = 0f;
            img.color = c;
            img.enabled = false;
        }

        EnsureQuad();
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

    private void EnsureQuad()
    {
        if (_quad != null) return;

        _camera = Camera.main;
        if (_camera == null)
        {
            var all = Camera.allCameras;
            if (all != null && all.Length > 0) _camera = all[0];
        }
        if (_camera == null)
        {
            Debug.LogWarning("[LtvFlashOverlay] No camera found; flash will not be visible.", this);
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "LtvFlashQuad";
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Make the quad render on both sides so we don't have to reason about
        // which way Unity's Quad primitive faces in this Unity version.
        var mf = go.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            var mesh = Instantiate(mf.sharedMesh);
            var tris = mesh.triangles;
            var dbl = new int[tris.Length * 2];
            Array.Copy(tris, 0, dbl, 0, tris.Length);
            for (int i = 0; i < tris.Length; i += 3)
            {
                dbl[tris.Length + i + 0] = tris[i + 2];
                dbl[tris.Length + i + 1] = tris[i + 1];
                dbl[tris.Length + i + 2] = tris[i + 0];
            }
            mesh.triangles = dbl;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }

        _quad = go.transform;
        _quad.SetParent(_camera.transform, false);
        _quad.localRotation = Quaternion.identity;
        _quad.localPosition = new Vector3(0f, 0f, Mathf.Max(0.05f, quadDistance));

        SizeQuadToFrustum();

        var renderer = go.GetComponent<Renderer>();
        Shader sh = Shader.Find("Unlit/Transparent");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("UI/Default");
        _quadMat = new Material(sh);
        _quadMat.renderQueue = 4000; // draw on top of everything

        _quadTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _quadTex.filterMode = FilterMode.Point;
        _quadTex.wrapMode = TextureWrapMode.Clamp;
        _quadMat.mainTexture = _quadTex;

        renderer.sharedMaterial = _quadMat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        SetAlpha(0f);
    }

    private void SizeQuadToFrustum()
    {
        if (_quad == null || _camera == null) return;
        float d = Mathf.Max(0.05f, quadDistance);
        float h = 2f * d * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * Mathf.Max(0.01f, _camera.aspect);
        float m = Mathf.Max(1f, coverageMargin);
        _quad.localScale = new Vector3(w * m, h * m, 1f);
    }

    private IEnumerator FlashRoutine(Action onComplete)
    {
        EnsureQuad();
        if (_quad == null || _quadTex == null)
        {
            onComplete?.Invoke();
            _running = null;
            yield break;
        }

        // Re-fit each flash in case the camera FOV/aspect changed since Awake.
        SizeQuadToFrustum();

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
        if (_quadTex == null) return;
        Color c = flashColor;
        c.a = a;
        _quadTex.SetPixel(0, 0, c);
        _quadTex.Apply(false);
        if (_quadMat != null && _quadMat.HasProperty("_Color"))
        {
            _quadMat.SetColor("_Color", c);
        }
    }

    private void OnDestroy()
    {
        if (_quadMat != null) Destroy(_quadMat);
        if (_quadTex != null) Destroy(_quadTex);
    }
}
