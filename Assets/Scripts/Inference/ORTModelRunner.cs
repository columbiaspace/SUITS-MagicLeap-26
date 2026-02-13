// Assets/Scripts/Inference/OrtModelRunner.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using UnityEngine;

/// <summary>
/// ONNX Runtime inference runner. Reads input names and shapes
/// from the model metadata — no hardcoded dimensions.
/// </summary>
public sealed class OrtModelRunner : IModelRunner
{
    private readonly InferenceSession _session;
    private readonly Dictionary<string, int[]> _inputShapes;

    public OrtModelRunner(string modelPath)
    {
        using var opts = new SessionOptions();
        _session = new InferenceSession(modelPath, opts);

        _inputShapes = BuildInputShapes(_session);

        var shapeLog = string.Join(", ",
            _inputShapes.Select(kv => $"{kv.Key}=[{string.Join(",", kv.Value)}]"));
        Debug.Log($"[ORT] Loaded model: {modelPath}  inputs: {shapeLog}");
    }

    public Dictionary<string, float[]> BuildDefaultInput()
    {
        var inputs = new Dictionary<string, float[]>();
        foreach (var kv in _inputShapes)
        {
            var elementCount = 1;
            foreach (var dim in kv.Value) elementCount *= dim;
            inputs[kv.Key] = new float[elementCount];
        }
        return inputs;
    }

    public Dictionary<string, float[]> Run(Dictionary<string, float[]> inputTensors)
    {
        var namedInputs = new List<NamedOnnxValue>();

        foreach (var kv in inputTensors)
        {
            if (!_inputShapes.TryGetValue(kv.Key, out var shape))
            {
                Debug.LogWarning($"[ORT] Unknown input name '{kv.Key}', skipping.");
                continue;
            }

            var tensor = new DenseTensor<float>(kv.Value, shape);
            namedInputs.Add(NamedOnnxValue.CreateFromTensor(kv.Key, tensor));
        }

        using var results = _session.Run(namedInputs);
        var outputs = new Dictionary<string, float[]>();

        foreach (var r in results)
        {
            try
            {
                var tensor = r.AsTensor<float>();
                var arr = new float[tensor.Length];
                var idx = 0;
                foreach (var v in tensor) arr[idx++] = v;
                outputs[r.Name] = arr;
            }
            catch (InvalidCastException)
            {
                Debug.LogWarning($"[ORT] Output '{r.Name}' is not float tensor, skipping.");
            }
        }

        return outputs;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }

    /// <summary>
    /// Extract input shapes from model metadata.
    /// Dynamic dims (-1) are replaced with 1 so a default tensor can be built.
    /// </summary>
    private static Dictionary<string, int[]> BuildInputShapes(InferenceSession session)
    {
        var shapes = new Dictionary<string, int[]>();

        foreach (var meta in session.InputMetadata)
        {
            var dims = meta.Value.Dimensions;
            var resolved = new int[dims.Length];
            for (int i = 0; i < dims.Length; i++)
            {
                resolved[i] = dims[i] > 0 ? dims[i] : 1; // dynamic → 1
            }
            shapes[meta.Key] = resolved;
        }

        return shapes;
    }
}
