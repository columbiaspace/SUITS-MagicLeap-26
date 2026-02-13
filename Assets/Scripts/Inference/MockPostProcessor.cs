// Assets/Scripts/Inference/MockPostProcessor.cs
using System.Collections.Generic;

/// <summary>
/// Decodes mock model output into pixel-space detections.
/// Expects "mock_dets" key with [classId, score, x, y, w, h] (normalized 0-1).
/// </summary>
public sealed class MockPostProcessor : IPostProcessor
{
    public IReadOnlyList<Detection> PostProcess(
        Dictionary<string, float[]> modelOutputs,
        int imageW,
        int imageH)
    {
        var dets = new List<Detection>();
        if (!modelOutputs.TryGetValue("mock_dets", out var v) || v.Length < 6) return dets;

        // v: classId, score, x, y, w, h (normalized)
        dets.Add(new Detection(
            classId: (int)v[0],
            score:   v[1],
            x:       v[2] * imageW,
            y:       v[3] * imageH,
            w:       v[4] * imageW,
            h:       v[5] * imageH
        ));

        return dets;
    }
}
