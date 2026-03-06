using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TerrainAnalyzer : MonoBehaviour
{
    [Header("Terrain Source")]
    [SerializeField] private Mesh terrainMesh;

    [Header("Grid Settings")]
    [Tooltip("Must match TileManager.TILE_SIZE")]
    [SerializeField] private float tileSize = 0.6096f;

    [Header("Coordinate Mapping (OBJ → DUST)")]
    [Tooltip("Center of the rock yard in DUST X")]
    [SerializeField] private float dustCenterX = -5655f;
    [Tooltip("Center of the rock yard in DUST Y")]
    [SerializeField] private float dustCenterY = -10007.5f;
    [Tooltip("DUST X units per OBJ X unit")]
    [SerializeField] private float dustScaleX = 4.027f;
    [Tooltip("DUST Y units per OBJ Z unit")]
    [SerializeField] private float dustScaleZ = 2.38f;

    [Header("Walkability Thresholds")]
    [Tooltip("Slope angle (degrees) beyond which terrain is fully unwalkable")]
    [SerializeField] private float maxSlopeDegrees = 30f;
    [Tooltip("Height range (meters) within a single tile beyond which it's fully unwalkable")]
    [SerializeField] private float maxHeightRange = 0.3f;
    [Tooltip("Blend factor for slope vs height: 0 = height only, 1 = slope only")]
    [Range(0f, 1f)]
    [SerializeField] private float slopeBlendFactor = 0.6f;

    public static TerrainAnalyzer Instance { get; private set; }

    public bool IsReady { get; private set; }

    private Dictionary<Vector2Int, float> _walkabilityGrid = new Dictionary<Vector2Int, float>();
    private Dictionary<Vector2Int, float> _heightGrid = new Dictionary<Vector2Int, float>();

    private float _objMinX, _objMaxX, _objMinZ, _objMaxZ;

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
        BuildGrid();
    }

    public float GetWeight(Vector2Int gridCell)
    {
        if (_walkabilityGrid.TryGetValue(gridCell, out float weight))
            return weight;
        return -1f;
    }

    public float GetHeight(Vector2Int gridCell)
    {
        if (_heightGrid.TryGetValue(gridCell, out float h))
            return h;
        return 0f;
    }

    public bool HasData(Vector2Int gridCell)
    {
        return _walkabilityGrid.ContainsKey(gridCell);
    }

    public IEnumerable<Vector2Int> AllCells => _walkabilityGrid.Keys;

    /// <summary>
    /// Converts a DUST IMU position to the OBJ local grid coordinate used by this analyzer.
    /// </summary>
    public Vector2Int DustToGrid(float dustX, float dustY)
    {
        float objX = (dustX - dustCenterX) / dustScaleX;
        float objZ = (dustY - dustCenterY) / dustScaleZ;
        return ObjToGrid(objX, objZ);
    }

    /// <summary>
    /// Converts OBJ-space X/Z to a grid cell index.
    /// </summary>
    public Vector2Int ObjToGrid(float objX, float objZ)
    {
        int gx = Mathf.FloorToInt(objX / tileSize);
        int gz = Mathf.FloorToInt(objZ / tileSize);
        return new Vector2Int(gx, gz);
    }

    private void BuildGrid()
    {
        if (terrainMesh == null)
        {
            Debug.LogError("TerrainAnalyzer: No terrain mesh assigned.");
            return;
        }

        Vector3[] verts = terrainMesh.vertices;
        int[] tris = terrainMesh.triangles;

        if (verts.Length == 0 || tris.Length == 0)
        {
            Debug.LogError("TerrainAnalyzer: Terrain mesh has no geometry.");
            return;
        }

        ComputeBounds(verts);

        var cellSlopes = new Dictionary<Vector2Int, float>();
        var cellMinY = new Dictionary<Vector2Int, float>();
        var cellMaxY = new Dictionary<Vector2Int, float>();

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];

            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            float slopeDeg = Vector3.Angle(normal, Vector3.up);

            Vector3 centroid = (a + b + c) / 3f;
            Vector2Int cell = ObjToGrid(centroid.x, centroid.z);

            if (!cellSlopes.ContainsKey(cell) || slopeDeg > cellSlopes[cell])
                cellSlopes[cell] = slopeDeg;

            float triMinY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float triMaxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

            if (!cellMinY.ContainsKey(cell) || triMinY < cellMinY[cell])
                cellMinY[cell] = triMinY;
            if (!cellMaxY.ContainsKey(cell) || triMaxY > cellMaxY[cell])
                cellMaxY[cell] = triMaxY;
        }

        foreach (Vector2Int cell in cellSlopes.Keys)
        {
            float slope = cellSlopes[cell];
            float heightRange = cellMaxY[cell] - cellMinY[cell];

            float slopeWeight = Mathf.Clamp01(slope / maxSlopeDegrees);
            float heightWeight = Mathf.Clamp01(heightRange / maxHeightRange);

            float weight = slopeWeight * slopeBlendFactor + heightWeight * (1f - slopeBlendFactor);
            _walkabilityGrid[cell] = Mathf.Clamp01(weight);

            _heightGrid[cell] = (cellMinY[cell] + cellMaxY[cell]) * 0.5f;
        }

        IsReady = true;
        Debug.Log($"TerrainAnalyzer: Grid built with {_walkabilityGrid.Count} cells. " +
                  $"OBJ bounds X[{_objMinX:F2},{_objMaxX:F2}] Z[{_objMinZ:F2},{_objMaxZ:F2}]");
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
