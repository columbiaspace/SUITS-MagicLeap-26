// Assets/Scripts/Inference/FolderFrameSource.cs
//
// Editor/Desktop only. On Android, StreamingAssets is inside the APK
// and Directory.GetFiles() won't work. Use a headset camera source instead.

using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Loads images from a StreamingAssets subfolder and cycles through them
/// at a configurable FPS. Editor/Desktop only — not compatible with Android.
/// </summary>
public sealed class FolderFrameSource : MonoBehaviour, IFrameSource
{
    [Tooltip("Relative to StreamingAssets, e.g. TestFrames")]
    [SerializeField] private string relativeFolder = "TestFrames";
    [SerializeField] private float fps = 5f;

    private readonly List<string> _paths = new();
    private int _idx;
    private float _nextTime;
    private Texture2D _tex;

    void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.LogError("[FrameSource] FolderFrameSource is not supported on Android. Use a camera-based IFrameSource.");
        return;
#else
        var folder = Path.Combine(Application.streamingAssetsPath, relativeFolder);
        if (!Directory.Exists(folder))
        {
            Debug.LogError($"[FrameSource] Folder not found: {folder}");
            return;
        }

        foreach (var p in Directory.GetFiles(folder))
        {
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg") _paths.Add(p);
        }

        _paths.Sort();
        Debug.Log($"[FrameSource] Loaded {_paths.Count} images from {folder}");
#endif
    }

    public bool TryGetFrame(out Texture2D frame, out long timestampMs)
    {
        frame = null;
        timestampMs = 0;

        if (_paths.Count == 0) return false;
        if (Time.time < _nextTime) return false;

        _nextTime = Time.time + (1f / Mathf.Max(0.01f, fps));
        var path = _paths[_idx];
        _idx = (_idx + 1) % _paths.Count;

        var bytes = File.ReadAllBytes(path);
        if (_tex == null) _tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!_tex.LoadImage(bytes))
        {
            Debug.LogError($"[FrameSource] Failed to load image: {path}");
            return false;
        }

        frame = _tex;
        timestampMs = (long)(Time.realtimeSinceStartup * 1000);
        return true;
    }

    void OnDestroy()
    {
        if (_tex != null)
        {
            Destroy(_tex);
            _tex = null;
        }
    }
}
