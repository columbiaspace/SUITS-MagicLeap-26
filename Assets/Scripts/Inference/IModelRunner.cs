// Assets/Scripts/Inference/IModelRunner.cs
using System;
using System.Collections.Generic;

/// <summary>
/// Abstraction over model inference. Implementations handle
/// tensor creation, session management, and output extraction.
/// </summary>
public interface IModelRunner : IDisposable
{
    /// <summary>
    /// Build a zeroed input dictionary matching this model's expected
    /// input names and shapes. Useful for pipeline smoke-testing
    /// before real preprocessing exists.
    /// </summary>
    Dictionary<string, float[]> BuildDefaultInput();

    /// <summary>
    /// Run inference. Keys are input tensor names; values are flat float arrays.
    /// Returns output tensors in the same format.
    /// </summary>
    Dictionary<string, float[]> Run(Dictionary<string, float[]> inputTensors);
}
