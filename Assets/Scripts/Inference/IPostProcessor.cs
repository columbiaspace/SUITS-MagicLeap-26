// Assets/Scripts/Inference/IPostProcessor.cs
using System.Collections.Generic;

/// <summary>
/// A single object detection result.
/// Coordinates are in pixel space (top-left origin).
/// </summary>
public readonly struct Detection
{
    public readonly int classId;
    public readonly float score;
    public readonly float x;  // top-left x (pixels)
    public readonly float y;  // top-left y (pixels)
    public readonly float w;  // width (pixels)
    public readonly float h;  // height (pixels)

    public Detection(int classId, float score, float x, float y, float w, float h)
    {
        this.classId = classId;
        this.score = score;
        this.x = x;
        this.y = y;
        this.w = w;
        this.h = h;
    }
}

public interface IPostProcessor
{
    IReadOnlyList<Detection> PostProcess(
        Dictionary<string, float[]> modelOutputs,
        int imageW,
        int imageH);
}
