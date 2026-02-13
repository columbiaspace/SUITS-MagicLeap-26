// Assets/Scripts/OrtSmokeTest.cs
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

/// <summary>
/// Validates that ONNX Runtime can load and run a model on-device.
/// Skips in Editor (ORT native libs are Android-only).
/// Attach to any GameObject in a test scene.
/// </summary>
public sealed class OrtSmokeTest : MonoBehaviour
{
    private IEnumerator Start()
    {
#if UNITY_EDITOR
        Debug.Log("[ORT] Skipping ORT smoke test in Editor (no native libs).");
        yield break;
#else
        // 1. Copy model from StreamingAssets to persistent path (Android-safe)
        yield return OrtModelLoader.CopyModelToPersistent(
            "Models/smoke.onnx",
            "smoke.onnx"
        );

        var modelPath = Path.Combine(Application.persistentDataPath, "smoke.onnx");
        Debug.Log("[ORT] Loading model from: " + modelPath);

        // 2. Create inference session
        using var session = new InferenceSession(modelPath);

        // 3. Create dummy input tensor
        // Shape: [batch=1, channels=3, height=8, width=8]
        var inputTensor = new DenseTensor<float>(new[] { 1, 3, 8, 8 });
        for (int i = 0; i < inputTensor.Length; i++)
        {
            inputTensor.Buffer.Span[i] = 1.0f;
        }

        // 4. Create input container
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor)
        };

        // 5. Run inference
        using var results = session.Run(inputs);

        // 6. Log output
        foreach (var result in results)
        {
            Debug.Log($"[ORT] Output name: {result.Name}");
            var output = result.AsTensor<float>();
            Debug.Log("[ORT] Output shape: " + string.Join(",", output.Dimensions.ToArray()));
            Debug.Log($"[ORT] First value: {output[0]}");
        }

        Debug.Log("[ORT] Smoke test SUCCESS.");
#endif
    }
}
