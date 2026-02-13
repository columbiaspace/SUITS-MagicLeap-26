// Assets/Scripts/Inference/IFrameSource.cs
using UnityEngine;

public interface IFrameSource
{
    // Return false if no frame this tick.
    bool TryGetFrame(out Texture2D frame, out long timestampMs);
}
