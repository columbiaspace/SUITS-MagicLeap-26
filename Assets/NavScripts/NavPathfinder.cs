using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stateless grid pathfinding utilities consumed by <see cref="TileManager.ComputePath"/>.
/// Operates entirely in the lunar-mesh grid frame produced by <see cref="TerrainAnalyzer"/>:
/// inputs and outputs are <see cref="Vector2Int"/> cells, no world-space concerns. The
/// returned path is later mapped back to headset world space via
/// <c>NavGridUtilities.CellToLocalTilePos</c> + <c>TileManager.AlignPathRootUnderRigForEvaCell</c>.
/// </summary>
public static class NavPathfinder
{
    /// <summary>
    /// 8-connected neighborhood offsets — first four cardinal, last four diagonal.
    /// Index ≥ 4 is used by <see cref="FindPath"/> to apply the √2 diagonal-step penalty.
    /// </summary>
    private static readonly Vector2Int[] Dirs =
    {
        new Vector2Int( 1,  0),
        new Vector2Int(-1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int( 0, -1),
        new Vector2Int( 1,  1),
        new Vector2Int( 1, -1),
        new Vector2Int(-1,  1),
        new Vector2Int(-1, -1),
    };

    private const float Sqrt2 = 1.41421356f;

    /// <summary>
    /// A* over a pre-filtered walkable set. Only cells in <paramref name="walkableSet"/> are
    /// considered, and per-cell weights from <see cref="TerrainAnalyzer.GetWeight"/> are folded
    /// into edge costs so steeper / bumpier terrain costs more to cross. Heuristic is Euclidean
    /// distance, edge cost is <c>(1 + neighborWeight) · {1, √2}</c> for cardinal vs. diagonal.
    /// Returns the cell list start → goal (inclusive) on success, or empty when start/goal are
    /// not walkable or no path exists. An empty result is a real answer — start and goal are on
    /// disconnected walkable regions — and the caller is expected to render it as "no path"
    /// rather than fabricating one.
    /// </summary>
    public static List<Vector2Int> FindPath(
        HashSet<Vector2Int> walkableSet,
        TerrainAnalyzer terrain,
        Vector2Int start,
        Vector2Int goal)
    {
        if (walkableSet == null || walkableSet.Count == 0)
            return new List<Vector2Int>();

        if (!walkableSet.Contains(start) || !walkableSet.Contains(goal))
            return new List<Vector2Int>();

        if (start == goal)
            return new List<Vector2Int> { start };

        var openSet = new SortedSet<(float f, int tieBreak, int px, int py)>();
        var gScore = new Dictionary<Vector2Int, float>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        int tie = 0;

        gScore[start] = 0f;
        openSet.Add((Heuristic(start, goal), tie++, start.x, start.y));

        while (openSet.Count > 0)
        {
            var current = openSet.Min;
            openSet.Remove(current);
            Vector2Int pos = new Vector2Int(current.px, current.py);

            if (pos == goal)
                return ReconstructPath(cameFrom, pos);

            float currentG = gScore[pos];

            for (int i = 0; i < Dirs.Length; i++)
            {
                Vector2Int neighbor = pos + Dirs[i];

                if (!walkableSet.Contains(neighbor))
                    continue;

                float nWeight = terrain != null ? terrain.GetWeight(neighbor) : 0f;
                if (nWeight < 0f) nWeight = 0f;

                bool diagonal = i >= 4;
                float stepCost = (1f + nWeight) * (diagonal ? Sqrt2 : 1f);
                float tentativeG = currentG + stepCost;

                if (gScore.TryGetValue(neighbor, out float existingG) && tentativeG >= existingG)
                    continue;

                gScore[neighbor] = tentativeG;
                cameFrom[neighbor] = pos;
                openSet.Add((tentativeG + Heuristic(neighbor, goal), tie++, neighbor.x, neighbor.y));
            }
        }

        return new List<Vector2Int>();
    }

    /// <summary>
    /// A* heuristic: Euclidean distance between two grid cells. Admissible because the cheapest
    /// possible step cost is 1 (cardinal) or √2 (diagonal) before terrain weighting, and Euclidean
    /// distance never overestimates that lower bound. Match with the diagonal step cost in
    /// <see cref="FindPath"/> keeps the search tight without sacrificing optimality.
    /// </summary>
    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Walks the <paramref name="cameFrom"/> back-pointer chain from goal to start, then reverses
    /// the list so the caller gets cells in traversal order. <see cref="FindPath"/> calls this
    /// the moment the open-set min equals the goal, returning the result directly to
    /// <see cref="TileManager.ComputePath"/>.
    /// </summary>
    private static List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        var path = new List<Vector2Int> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}
