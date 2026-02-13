// Assets/Scripts/Inference/PipelineController.cs
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Orchestrates frame source → model runner → post-processor → overlay.
/// Editor uses MockModelRunner; device uses OrtModelRunner.
/// Attach IFrameSource, OverlayRenderer, and DebugHud on the same GameObject.
/// </summary>
public sealed class PipelineController : MonoBehaviour
{
    [Header("Model")]
    [SerializeField] private string onnxModelRelativePath = "Models/smoke.onnx";

    [Header("Runtime Mode")]
    [SerializeField] private bool forceMockInEditor = true;

    private IFrameSource _frameSource;
    private IModelRunner _runner;
    private IPostProcessor _post;
    private OverlayRenderer _overlay;
    private DebugHud _debugHud;
    private bool _ready;
    private readonly Stopwatch _inferenceStopwatch = new();

    IEnumerator Start()
    {
        _frameSource = GetComponent<IFrameSource>();
        if (_frameSource == null)
        {
            Debug.LogError("[Pipeline] Missing IFrameSource on same GameObject. Pipeline disabled.");
            yield break;
        }

        _overlay = GetComponent<OverlayRenderer>();
        _debugHud = GetComponent<DebugHud>();

        _post = new MockPostProcessor();

#if UNITY_EDITOR
        if (forceMockInEditor)
        {
            Debug.Log("[Pipeline] Using MockModelRunner in Editor.");
            _runner = new MockModelRunner();
            _debugHud?.SetModelStatus("mock");
            _ready = true;
            yield break;
        }
#endif

        _debugHud?.SetModelStatus("loading...");

        // Device path: copy from StreamingAssets via OrtModelLoader (Android-safe)
        var outputFileName = Path.GetFileName(onnxModelRelativePath);
        yield return OrtModelLoader.CopyModelToPersistent(onnxModelRelativePath, outputFileName);

        var modelPath = Path.Combine(Application.persistentDataPath, outputFileName);
        if (!File.Exists(modelPath))
        {
            Debug.LogError($"[Pipeline] Model file missing after copy: {modelPath}");
            _debugHud?.SetModelStatus("FAILED (copy)");
            yield break;
        }

        try
        {
            _runner = new OrtModelRunner(modelPath);
            _debugHud?.SetModelStatus("ORT ready");
            _ready = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Pipeline] Failed to load ORT model: {ex.Message}");
            _debugHud?.SetModelStatus($"FAILED: {ex.Message}");
        }
    }

    void OnDestroy()
    {
        _runner?.Dispose();
    }

    void Update()
    {
        if (!_ready || _frameSource == null || _runner == null) return;

        if (!_frameSource.TryGetFrame(out var frame, out var ts)) return;

        // TODO: real preprocessing later (resize/normalize/CHW)
        var inputs = _runner.BuildDefaultInput();

        _inferenceStopwatch.Restart();
        var outputs = _runner.Run(inputs);
        _inferenceStopwatch.Stop();

        var dets = _post.PostProcess(outputs, frame.width, frame.height);

        _overlay?.SetDetections(dets, frame.width, frame.height);
        _debugHud?.SetDetectionCount(dets.Count);
        _debugHud?.SetInferenceMs((float)_inferenceStopwatch.Elapsed.TotalMilliseconds);
    }
}
