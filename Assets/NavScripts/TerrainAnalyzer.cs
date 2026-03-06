using System.Collections.Generic;
using UnityEngine;

public class TerrainAnalyzer : MonoBehaviour
{
    [Header("Terrain Source")]
    [SerializeField] private Mesh terrainMesh;

    [Header("Grid Settings")]
    [SerializeField] private float tileSize = 0.6096f;

    [Header("Coordinate Mapping (OBJ → DUST)")]
    [SerializeField] private float dustCenterX = -5655f;
    [SerializeField] private float dustCenterY = -10007.5f;
    [SerializeField] private float dustScaleX = 4.027f;
    [SerializeField] private float dustScaleZ = 2.38f;

    [Header("Walkability Thresholds")]
    [SerializeField] private float maxSlopeDegrees = 25f;
    [SerializeField] private float maxHeightRange = 0.2f;
    [Range(0f, 1f)]
    [SerializeField] private float slopeBlendFactor = 0.6f;
    [SerializeField] private float impassableThreshold = 0.6f;

    public static TerrainAnalyzer Instance { get; private set; }
    public bool IsReady { get; private set; }

    private Dictionary<Vector2Int, float> _walkabilityGrid = new Dictionary<Vector2Int, float>();
    private Dictionary<Vector2Int, float> _heightGrid = new Dictionary<Vector2Int, float>();
    private HashSet<Vector2Int> _walkableSet = new HashSet<Vector2Int>();

    private float _objMinX, _objMaxX, _objMinZ, _objMaxZ;

    private struct CellTerrain
    {
        public float maxSlope;
        public float minY;
        public float maxY;
    }

    public IEnumerable<Vector2Int> AllCells => _walkabilityGrid.Keys;
    public HashSet<Vector2Int> WalkableSet => _walkableSet;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        maxSlopeDegrees = 25f;
        maxHeightRange = 0.2f;
        slopeBlendFactor = 0.6f;
        impassableThreshold = 0.6f;
        BuildGrid();
    }

    // --- Public queries ---

    public float GetWeight(Vector2Int gridCell)
    {
        return _walkabilityGrid.TryGetValue(gridCell, out float weight) ? weight : -1f;
    }

    public float GetHeight(Vector2Int gridCell)
    {
        return _heightGrid.TryGetValue(gridCell, out float h) ? h : 0f;
    }

    public bool HasData(Vector2Int gridCell) => _walkabilityGrid.ContainsKey(gridCell);

    public bool IsWalkable(Vector2Int gridCell) => _walkableSet.Contains(gridCell);

    // --- Coordinate conversions ---

    public Vector2Int DustToGrid(float dustX, float dustY)
    {
        float objX = (dustX - dustCenterX) / dustScaleX;
        float objZ = (dustY - dustCenterY) / dustScaleZ;
        return ObjToGrid(objX, objZ);
    }

    public Vector2Int ObjToGrid(float objX, float objZ)
    {
        int gx = Mathf.FloorToInt(objX / tileSize);
        int gz = Mathf.FloorToInt(objZ / tileSize);
        return new Vector2Int(gx, gz);
    }

    // --- Grid construction pipeline ---

    private void BuildGrid()
    {
        if (!ValidateMesh(out Vector3[] verts, out int[] tris))
            return;

        ComputeBounds(verts);

        var cellTerrain = AccumulateTriangleSlopesAndHeights(verts, tris);
        ClassifyCells(cellTerrain);

        IsReady = true;
        Debug.Log($"TerrainAnalyzer: Grid built — {_walkabilityGrid.Count} total cells, " +
                  $"{_walkableSet.Count} walkable (threshold {impassableThreshold}). " +
                  $"OBJ bounds X[{_objMinX:F2},{_objMaxX:F2}] Z[{_objMinZ:F2},{_objMaxZ:F2}]");
    }

    private bool ValidateMesh(out Vector3[] verts, out int[] tris)
    {
        verts = null;
        tris = null;

        if (terrainMesh == null)
        {
            Debug.LogError("TerrainAnalyzer: No terrain mesh assigned.");
            return false;
        }

        verts = terrainMesh.vertices;
        tris = terrainMesh.triangles;

        if (verts.Length == 0 || tris.Length == 0)
        {
            Debug.LogError("TerrainAnalyzer: Terrain mesh has no geometry.");
            return false;
        }

        return true;
    }

    /// For each grid cell that overlaps a triangle, record the steepest
    /// slope angle and the min/max vertex height seen in that cell.
    private Dictionary<Vector2Int, CellTerrain> AccumulateTriangleSlopesAndHeights(
        Vector3[] verts, int[] tris)
    {
        var cells = new Dictionary<Vector2Int, CellTerrain>();

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];

            float slopeDeg = TriangleSlopeDegrees(a, b, c);
            Vector2Int cell = TriangleCentroidCell(a, b, c);
            float triMinY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float triMaxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

            if (cells.TryGetValue(cell, out CellTerrain existing))
            {
                existing.maxSlope = Mathf.Max(existing.maxSlope, slopeDeg);
                existing.minY = Mathf.Min(existing.minY, triMinY);
                existing.maxY = Mathf.Max(existing.maxY, triMaxY);
                cells[cell] = existing;
            }
            else
            {
                cells[cell] = new CellTerrain
                {
                    maxSlope = slopeDeg,
                    minY = triMinY,
                    maxY = triMaxY
                };
            }
        }

        return cells;
    }

    /// Angle in degrees between the triangle face normal and world-up.
    private static float TriangleSlopeDegrees(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        return Vector3.Angle(normal, Vector3.up);
    }

    /// Grid cell that contains the triangle's centroid.
    private Vector2Int TriangleCentroidCell(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 centroid = (a + b + c) / 3f;
        return ObjToGrid(centroid.x, centroid.z);
    }

    /// Converts raw slope/height data into a 0-1 walkability weight per cell,
    /// records average height, and marks cells below the impassable threshold
    /// as walkable.
    private void ClassifyCells(Dictionary<Vector2Int, CellTerrain> cellTerrain)
    {
        foreach (var kvp in cellTerrain)
        {
            Vector2Int cell = kvp.Key;
            CellTerrain terrain = kvp.Value;

            float weight = BlendedDifficultyWeight(terrain.maxSlope, terrain.maxY - terrain.minY);
            _walkabilityGrid[cell] = weight;
            _heightGrid[cell] = (terrain.minY + terrain.maxY) * 0.5f;

            if (weight < impassableThreshold)
                _walkableSet.Add(cell);
        }
    }

    /// Produces a 0-1 traversal difficulty score by blending normalised slope
    /// steepness with normalised height variation within the cell.
    private float BlendedDifficultyWeight(float slopeDeg, float heightRange)
    {
        float slopeWeight = Mathf.Clamp01(slopeDeg / maxSlopeDegrees);
        float heightWeight = Mathf.Clamp01(heightRange / maxHeightRange);
        return Mathf.Clamp01(slopeWeight * slopeBlendFactor + heightWeight * (1f - slopeBlendFactor));
    }

    private void ComputeBounds(Vector3[] verts)
    {
        _objMinX = float.MaxValue;
        _objMaxX = float.MinValue;
        _objMinZ = float.MaxValue;
        _objMaxZ = float.MinValue;

        for (int i = 0; i < verts.Length; i++)
        {
            if (verts[i].x < _objMinX) _objMinX = verts[i].x;
            if (verts[i].x > _objMaxX) _objMaxX = verts[i].x;
            if (verts[i].z < _objMinZ) _objMinZ = verts[i].z;
            if (verts[i].z > _objMaxZ) _objMaxZ = verts[i].z;
        }
    }
}
