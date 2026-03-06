using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using TssApi;

public class TileManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrOrigin;
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private GameObject tilePrefab;

    [Header("Path Color")]
    [SerializeField] private Color pathColor = new Color(0.2f, 0.5f, 1.0f, 0.9f);

    [Header("IMU Position")]
    [SerializeField] private string evaId = "eva1";

    public const float TILE_SIZE = 0.6096f;

    private Dictionary<Vector2Int, GameObject> _tileObjects = new Dictionary<Vector2Int, GameObject>();
    private HashSet<Vector2Int> _pathCells = new HashSet<Vector2Int>();

    private Vector2Int _lastImuGrid;
    private Vector2Int _lastLtvGrid;
    private bool _initialized;

    private Material _unlitMaterial;
    private string _debugStatus = "TileManager: Initializing...";

    void Start()
    {
        _unlitMaterial = new Material(Shader.Find("Unlit/Color"));
    }

    void Update()
    {
        TerrainAnalyzer terrain = TerrainAnalyzer.Instance;
        if (terrain == null || !terrain.IsReady)
        {
            _debugStatus = terrain == null
                ? "TileManager: TerrainAnalyzer.Instance is NULL"
                : "TileManager: TerrainAnalyzer not ready yet...";
            return;
        }

        Vector2Int? imuGrid = GetImuGridPosition(terrain);
        Vector2Int? ltvGrid = GetLtvGridPosition(terrain);

        if (!imuGrid.HasValue || !ltvGrid.HasValue)
        {
            _debugStatus = "TileManager: Waiting for TSS position data...";
            return;
        }

        bool changed = !_initialized
            || imuGrid.Value != _lastImuGrid
            || ltvGrid.Value != _lastLtvGrid;
        if (!changed) return;

        _lastImuGrid = imuGrid.Value;
        _lastLtvGrid = ltvGrid.Value;
        _initialized = true;

        ComputePath(terrain, imuGrid.Value, ltvGrid.Value);
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 22;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.white;

        GUI.Label(new Rect(10, 10, 800, 200), _debugStatus, style);
    }

    Vector2Int? GetImuGridPosition(TerrainAnalyzer terrain)
    {
        TssUnityApiService tss = TssUnityApiService.Instance;
        if (tss == null) return null;

        Dictionary<string, object> imu = evaId == "eva2"
            ? GetNestedDict(GetNestedDict(tss.GetEva(), "imu"), "eva2")
            : tss.GetImuEva1();

        if (imu == null || !imu.ContainsKey("posx") || !imu.ContainsKey("posy"))
            return null;

        float dustX = Convert.ToSingle(imu["posx"]);
        float dustY = Convert.ToSingle(imu["posy"]);
        return terrain.DustToGrid(dustX, dustY);
    }

    Vector2Int? GetLtvGridPosition(TerrainAnalyzer terrain)
    {
        TssUnityApiService tss = TssUnityApiService.Instance;
        if (tss == null) return null;

        Dictionary<string, object> location = tss.GetLtvLocation();

        if (location == null || !location.ContainsKey("posx") || !location.ContainsKey("posy"))
            return null;

        float dustX = Convert.ToSingle(location["posx"]);
        float dustY = Convert.ToSingle(location["posy"]);
        return terrain.DustToGrid(dustX, dustY);
    }

    void ComputePath(TerrainAnalyzer terrain, Vector2Int imuGrid, Vector2Int ltvGrid)
    {
        HashSet<Vector2Int> walkableSet = terrain.WalkableSet;

        if (walkableSet.Count < 2)
        {
            _debugStatus = $"Not enough walkable cells ({walkableSet.Count})";
            Debug.LogWarning(_debugStatus);
            return;
        }

        Vector2Int start = SnapToWalkable(imuGrid, walkableSet);
        Vector2Int goal = SnapToWalkable(ltvGrid, walkableSet);

        _debugStatus = $"A* running: {start} -> {goal} ({walkableSet.Count} walkable)";
        Debug.Log($"TileManager: A* from {start} to {goal}...");

        var path = NavPathfinder.FindPath(walkableSet, terrain, start, goal);

        if (path.Count > 0)
        {
            _debugStatus = $"Path: {path.Count} tiles from {start} to {goal}";
            Debug.Log($"TileManager: Path found — {path.Count} steps.");
            ApplyPath(new HashSet<Vector2Int>(path));
        }
        else
        {
            _debugStatus = $"No path found from {start} to {goal}";
            Debug.LogWarning(_debugStatus);
        }
    }

    Vector2Int SnapToWalkable(Vector2Int cell, HashSet<Vector2Int> walkableSet)
    {
        if (walkableSet.Contains(cell)) return cell;

        Vector2Int closest = cell;
        float bestDist = float.MaxValue;
        foreach (Vector2Int w in walkableSet)
        {
            float dist = (w - cell).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                closest = w;
            }
        }
        return closest;
    }

    void ApplyPath(HashSet<Vector2Int> newPathCells)
    {
        foreach (Vector2Int cell in _pathCells)
        {
            if (!newPathCells.Contains(cell) && _tileObjects.TryGetValue(cell, out GameObject old))
            {
                Destroy(old);
                _tileObjects.Remove(cell);
            }
        }

        foreach (Vector2Int cell in newPathCells)
        {
            if (_tileObjects.ContainsKey(cell)) continue;

            Vector3 pos = new Vector3(cell.x * TILE_SIZE, 0.05f, cell.y * TILE_SIZE);
            GameObject tile = Instantiate(tilePrefab, pos, Quaternion.identity);
            tile.transform.localScale = Vector3.one * 1.1f;

            Renderer rend = tile.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(_unlitMaterial);
                mat.color = pathColor;
                rend.material = mat;
            }

            _tileObjects[cell] = tile;
        }

        _pathCells = newPathCells;
    }

    static Dictionary<string, object> GetNestedDict(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object found) && found is Dictionary<string, object> dict)
            return dict;
        return new Dictionary<string, object>();
    }
}
