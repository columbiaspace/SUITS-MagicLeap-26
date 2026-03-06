using System.Collections.Generic;
using UnityEngine;

public static class NavPathfinder
{
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
    /// A* over a pre-filtered walkable set. Only cells in walkableSet are
    /// considered; weights from TerrainAnalyzer influence edge costs.
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

    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

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
