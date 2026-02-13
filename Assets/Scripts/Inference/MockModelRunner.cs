// Assets/Scripts/Inference/MockModelRunner.cs
using System.Collections.Generic;

/// <summary>
/// Returns hardcoded mock detections for editor testing.
/// Output format: classId, score, x, y, w, h (normalized 0-1).
/// </summary>
public sealed class MockModelRunner : IModelRunner
{
    public Dictionary<string, float[]> BuildDefaultInput()
    {
        // Mock runner ignores input; return empty placeholder
        return new Dictionary<string, float[]>
        {
            { "input", new float[0] }
        };
    }

    public Dictionary<string, float[]> Run(Dictionary<string, float[]> inputTensors)
    {
        // Single mock detection: class 0, 95% confidence, centered box
        return new Dictionary<string, float[]>
        {
            { "mock_dets", new float[] { 0, 0.95f, 0.3f, 0.3f, 0.2f, 0.2f } }
        };
    }

    public void Dispose() { }
}
