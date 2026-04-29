using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stateless helpers shared across the nav-path pipeline. Lives outside <see cref="TileManager"/>
/// so the orchestration class isn't cluttered with grid math, and so the same helpers are
/// available to other consumers (minimap, debug overlay) without going through the scene
/// component.
///
/// All operations are in lunar-mesh grid space — no headset-frame concerns. Conversion to
/// headset world space happens in <see cref="TileManager.ApplyPath"/> via
/// <see cref="CellToLocalTilePos"/> followed by the <c>NavPathTilesRoot</c> translation in
/// <see cref="TileManager.AlignPathRootUnderRigForEvaCell"/>.
/// </summary>
public static class NavGridUtilities
{
    /// <summary>
    /// Edge length, in meters, of a single nav-grid cell. This is the canonical unit shared by
    /// <see cref="TerrainAnalyzer.ObjToGrid"/>, <see cref="CellToLocalTilePos"/>, and the minimap
    /// renderer. Set to 2 ft (0.6096 m) to match the rock-yard tile size; changing this requires
    /// rebuilding the analyzer's grid and reauthoring the tile prefab scale.
    /// </summary>
    public const float TILE_SIZE = 0.6096f;

    /// <summary>
    /// Linear-scan nearest-cell lookup keyed on squared distance. Used by
    /// <see cref="TileManager.ComputePath"/>'s disconnected-island fallbacks and indirectly via
    /// <see cref="SnapToWalkable"/>. Linear is fine because walkable sets are at most a few
    /// thousand cells in the rock-yard meshes; if we ever ship larger maps, swap for a KD-tree.
    /// </summary>
    public static Vector2Int ClosestInSet(Vector2Int p, HashSet<Vector2Int> set)
    {
        Vector2Int best = default;
        float bestD = float.MaxValue;
        foreach (Vector2Int c in set)
        {
            float d = (c - p).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }

        return best;
    }

    /// <summary>
    /// "Round" an arbitrary grid cell to a walkable one: pass-through if already walkable,
    /// otherwise the nearest walkable cell by Euclidean distance. Called for both EVA and LTV
    /// at the start of <see cref="TileManager.ComputePath"/>, and again in the editor debug
    /// overlay so the user can see start/goal markers move when their raw position lands in
    /// blocked terrain.
    /// </summary>
    public static Vector2Int SnapToWalkable(Vector2Int cell, HashSet<Vector2Int> walkableSet)
    {
        if (walkableSet == null || walkableSet.Count == 0)
        {
            return cell;
        }

        if (walkableSet.Contains(cell))
        {
            return cell;
        }

        return ClosestInSet(cell, walkableSet);
    }

    /// <summary>
    /// Maps a grid cell to its position inside <c>NavPathTilesRoot</c>'s local space:
    /// <c>(cell.x · TILE_SIZE, +0.05 m, cell.y · TILE_SIZE)</c>. The constant Y lift keeps tiles
    /// from z-fighting with whatever floor mesh the rig is standing on. Once the root has been
    /// translated by <see cref="TileManager.AlignPathRootUnderRigForEvaCell"/>, this same
    /// calculation places the LTV tile at the correct relative-to-user offset in headset world
    /// space.
    /// </summary>
    public static Vector3 CellToLocalTilePos(Vector2Int cell)
    {
        return new Vector3(cell.x * TILE_SIZE, 0.05f, cell.y * TILE_SIZE);
    }
}
