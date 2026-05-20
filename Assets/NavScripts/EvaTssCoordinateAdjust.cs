using UnityEngine;

/// <summary>
/// Converts raw EVA IMU posx/posy from TSS into the rock-yard TSS frame used by the
/// minimap, waypoints, and ground arrow. Apply immediately after reading IMU values.
/// </summary>
public static class EvaTssCoordinateAdjust
{
    public const float OffsetX =0f;
    public const float OffsetY =0f;

    public static Vector2 Apply(float rawX, float rawY) => new Vector2(rawX + OffsetX, rawY + OffsetY);

    public static Vector2 Apply(Vector2 raw) => Apply(raw.x, raw.y);

    /// <summary>One-line debug: raw IMU, configured offset, and resulting nav TSS position.</summary>
    public static string FormatPositionLog(float rawX, float rawY)
    {
        Vector2 nav = Apply(rawX, rawY);
        return
            $"IMU raw ({rawX:F1}, {rawY:F1})  offset ({OffsetX:+#0.#;-#0.#;0}, {OffsetY:+#0.#;-#0.#;0})  " +
            $"→ nav ({nav.x:F1}, {nav.y:F1})";
    }
}
