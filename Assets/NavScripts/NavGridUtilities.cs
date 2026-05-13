using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stateless grid helpers shared across the nav-path pipeline.
/// All operations are in the image-based grid coordinate frame produced by
/// <see cref="TerrainAnalyzer"/>. Inputs and outputs are <see cref="Vector2Int"/> cells.
/// </summary>
public static class NavGridUtilities
{
    /// <summary>
    /// Returns the cell in <paramref name="set"/> that is closest (by squared Euclidean
    /// distance) to <paramref name="p"/>. Linear scan is fine for sets of a few thousand
    /// cells; replace with a KD-tree if maps grow significantly larger.
    /// </summary>
    public static Vector2Int ClosestInSet(Vector2Int p, HashSet<Vector2Int> set)
    {
        Vector2Int best = default;
        float bestD = float.MaxValue;
        foreach (Vector2Int c in set)
        {
            float d = (c - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>
    /// Returns <paramref name="cell"/> unchanged if it is already in
    /// <paramref name="walkableSet"/>, otherwise snaps to the nearest walkable cell.
    /// Used before every A* call so start/goal coordinates that land on blocked pixels
    /// are automatically rounded to the closest traversable cell.
    /// </summary>
    public static Vector2Int SnapToWalkable(Vector2Int cell, HashSet<Vector2Int> walkableSet)
    {
        if (walkableSet == null || walkableSet.Count == 0) return cell;
        if (walkableSet.Contains(cell)) return cell;
        return ClosestInSet(cell, walkableSet);
    }
}
