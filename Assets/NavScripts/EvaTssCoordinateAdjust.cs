using UnityEngine;

/// <summary>
/// Converts raw EVA IMU posx/posy from TSS into the rock-yard TSS frame used by the
/// minimap, waypoints, and ground arrow. Apply immediately after reading IMU values.
/// </summary>
public static class EvaTssCoordinateAdjust
{
    public const float OffsetX = -5699f;
    public const float OffsetY = -9965f;

    public static Vector2 Apply(float rawX, float rawY) => new Vector2(rawX + OffsetX, rawY + OffsetY);

    public static Vector2 Apply(Vector2 raw) => Apply(raw.x, raw.y);
}
