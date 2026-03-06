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

    [Header("Grid")]
    [SerializeField] private int spawnRadius = 3;
    [SerializeField] private int maxTiles = 0;

    [Header("Walkability Colors")]
    [SerializeField] private Color walkableColor = new Color(0.1f, 0.85f, 0.2f, 0.6f);
    [SerializeField] private Color cautionColor = new Color(1f, 0.85f, 0f, 0.6f);
    [SerializeField] private Color unwalkableColor = new Color(0.9f, 0.15f, 0.1f, 0.6f);
    [SerializeField] private Color pathColor = new Color(0.2f, 0.5f, 1.0f, 0.9f);
    [SerializeField] private float walkableThreshold = 0.1f;
    [SerializeField] private float unwalkableThreshold = 0.25f;
    [SerializeField] private float skipThreshold = 1.1f;

    [Header("Debug Path (desktop testing)")]
    [Tooltip("Automatically compute and display a test path after tiles spawn")]
    [SerializeField] private bool showDebugPath = true;
    [SerializeField] private float pathImpassableThreshold = 0.3f;

    [Header("IMU Position")]
    [SerializeField] private bool useImuPosition = true;
    [SerializeField] private string evaId = "eva1";

    public const float TILE_SIZE = 0.6096f;

    private HashSet<Vector2Int> _spawnedLocalTiles = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, GameObject> _tileObjects = new Dictionary<Vector2Int, GameObject>();
    private Queue<Vector2Int> _spawnOrder = new Queue<Vector2Int>();

    private Vector2Int _lastLocalGrid;
    private Vector2Int _lastTerrainCenter;
    private bool _initialized;
    private bool _useAnchors;

    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock _mpb;
    private Material _unlitMaterial;

    private HashSet<Vector2Int> _pathCells = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _prevPathCells = new HashSet<Vector2Int>();

    private bool _allSpawned;
    private string _debugStatus = "TileManager: Initializing...";

    void Start()
    {
        _useAnchors = anchorManager != null && anchorManager.enabled
                      && anchorManager.subsystem != null;
        _mpb = new MaterialPropertyBlock();
        _unlitMaterial = new Material(Shader.Find("Unlit/Color"));

        walkableThreshold = 0.25f;
        unwalkableThreshold = 0.55f;
        pathImpassableThreshold = 0.6f;
    }

    void Update()
    {
        if (!_allSpawned)
        {
            TrySpawnAll();
            return;
        }

        if (!_useAnchors)
        {
            enabled = false;
            return;
        }

        if (xrOrigin == null) return;

        Vector2Int localGrid = WorldToGrid(xrOrigin.position);
        Vector2Int terrainCenter = GetTerrainGridCenter();

        bool moved = !_initialized || localGrid != _lastLocalGrid || terrainCenter != _lastTerrainCenter;
        if (!moved) return;

        _lastLocalGrid = localGrid;
        _lastTerrainCenter = terrainCenter;
        _initialized = true;

        SpawnSurroundingTiles(localGrid, terrainCenter);
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 22;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.white;

        GUI.Label(new Rect(10, 10, 800, 200), _debugStatus, style);
    }

    void TrySpawnAll()
    {
        TerrainAnalyzer terrain = TerrainAnalyzer.Instance;
        if (terrain == null || !terrain.IsReady)
        {
            _debugStatus = terrain == null
                ? "TileManager: TerrainAnalyzer.Instance is NULL"
                : "TileManager: TerrainAnalyzer not ready yet...";
            return;
        }

        foreach (Vector2Int cell in terrain.AllCells)
        {
            if (_spawnedLocalTiles.Contains(cell)) continue;

            float weight = terrain.GetWeight(cell);
            if (weight > skipThreshold) continue;

            Vector3 pos = new Vector3(cell.x * TILE_SIZE, 0f, cell.y * TILE_SIZE);
            GameObject tile = Instantiate(tilePrefab, pos, Quaternion.identity);

            Renderer rend = tile.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(_unlitMaterial);
                mat.color = WeightToColor(weight);
                rend.material = mat;
            }

            _tileObjects[cell] = tile;
            _spawnedLocalTiles.Add(cell);
        }

        _allSpawned = true;
        _debugStatus = $"Spawned {_spawnedLocalTiles.Count} tiles. Computing path...";
        Debug.Log($"TileManager: Spawned {_spawnedLocalTiles.Count} tiles.");

        if (showDebugPath)
            ComputeDebugPath(terrain);
    }

    void ComputeDebugPath(TerrainAnalyzer terrain)
    {
        var walkable = new List<Vector2Int>();
        foreach (Vector2Int cell in terrain.AllCells)
        {
            float w = terrain.GetWeight(cell);
            if (w >= 0f && w < pathImpassableThreshold)
                walkable.Add(cell);
        }

        if (walkable.Count < 2)
        {
            _debugStatus = $"Not enough walkable cells ({walkable.Count})";
            Debug.LogWarning(_debugStatus);
            return;
        }

        walkable.Sort((a, b) =>
        {
            int cmp = a.x.CompareTo(b.x);
            return cmp != 0 ? cmp : a.y.CompareTo(b.y);
        });

        Vector2Int start = walkable[walkable.Count / 4];
        Vector2Int goal = walkable[walkable.Count * 3 / 4];

        _debugStatus = $"A* running: {start} -> {goal} ({walkable.Count} walkable)";
        Debug.Log($"TileManager: Computing debug path from {start} to {goal} ({walkable.Count} walkable cells)...");

        var path = NavPathfinder.FindPath(terrain, start, goal, pathImpassableThreshold);

        if (path.Count > 0)
        {
            _debugStatus = $"Path: {path.Count} blue tiles from {start} to {goal}";
            Debug.Log($"TileManager: Debug path found with {path.Count} steps.");
            SetPath(new HashSet<Vector2Int>(path));
        }
        else
        {
            Debug.LogWarning($"TileManager: No path found from {start} to {goal}. Trying shorter path...");
            Vector2Int mid = walkable[walkable.Count / 2];
            path = NavPathfinder.FindPath(terrain, start, mid, pathImpassableThreshold);
            if (path.Count > 0)
            {
                _debugStatus = $"Short path: {path.Count} blue tiles to {mid}";
                Debug.Log($"TileManager: Shorter debug path found with {path.Count} steps (to {mid}).");
                SetPath(new HashSet<Vector2Int>(path));
            }
            else
            {
                _debugStatus = "FAILED: No path found at all.";
                Debug.LogWarning("TileManager: Could not find any debug path.");
            }
        }
    }

    Vector2Int WorldToGrid(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x / TILE_SIZE);
        int z = Mathf.FloorToInt(position.z / TILE_SIZE);
        return new Vector2Int(x, z);
    }

    Vector2Int GetTerrainGridCenter()
    {
        TerrainAnalyzer terrain = TerrainAnalyzer.Instance;
        if (terrain == null || !terrain.IsReady || !useImuPosition)
            return Vector2Int.zero;

        TssUnityApiService tss = TssUnityApiService.Instance;
        if (tss == null)
            return Vector2Int.zero;

        Dictionary<string, object> imu = evaId == "eva2"
            ? GetNestedDict(GetNestedDict(tss.GetEva(), "imu"), "eva2")
            : tss.GetImuEva1();

        if (imu == null || !imu.ContainsKey("posx") || !imu.ContainsKey("posy"))
            return Vector2Int.zero;

        float dustX = Convert.ToSingle(imu["posx"]);
        float dustY = Convert.ToSingle(imu["posy"]);
        return terrain.DustToGrid(dustX, dustY);
    }

    void SpawnSurroundingTiles(Vector2Int localCenter, Vector2Int terrainCenter)
    {
        for (int x = -spawnRadius; x <= spawnRadius; x++)
        {
            for (int z = -spawnRadius; z <= spawnRadius; z++)
            {
                Vector2Int localCell = localCenter + new Vector2Int(x, z);
                Vector2Int terrainCell = terrainCenter + new Vector2Int(x, z);
                TrySpawnTile(localCell, terrainCell);
            }
        }
    }

    void TrySpawnTile(Vector2Int localGrid, Vector2Int terrainGrid)
    {
        if (_spawnedLocalTiles.Contains(localGrid)) return;

        float weight = GetTerrainWeight(terrainGrid);
        if (weight > skipThreshold) return;

        Vector3 spawnPosition = new Vector3(
            localGrid.x * TILE_SIZE,
            0f,
            localGrid.y * TILE_SIZE
        );

        GameObject tile;

        if (_useAnchors)
        {
            ARAnchor anchor = anchorManager.AddAnchor(new Pose(spawnPosition, Quaternion.identity));
            if (anchor == null) return;
            tile = Instantiate(tilePrefab, anchor.transform);
        }
        else
        {
            tile = Instantiate(tilePrefab, spawnPosition, Quaternion.identity);
        }

        Renderer rend = tile.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(_unlitMaterial);
            mat.color = WeightToColor(weight);
            rend.material = mat;
        }

        _tileObjects[localGrid] = tile;
        _spawnOrder.Enqueue(localGrid);
        _spawnedLocalTiles.Add(localGrid);

        if (maxTiles > 0)
        {
            while (_spawnOrder.Count > maxTiles)
            {
                Vector2Int oldest = _spawnOrder.Dequeue();
                if (_tileObjects.TryGetValue(oldest, out GameObject old))
                {
                    Destroy(old);
                    _tileObjects.Remove(oldest);
                    _spawnedLocalTiles.Remove(oldest);
                }
            }
        }
    }

    float GetTerrainWeight(Vector2Int terrainGrid)
    {
        TerrainAnalyzer terrain = TerrainAnalyzer.Instance;
        if (terrain == null || !terrain.IsReady)
            return 0f;

        float w = terrain.GetWeight(terrainGrid);
        return w < 0f ? 0f : w;
    }

    Color WeightToColor(float weight)
    {
        if (weight < walkableThreshold)
            return walkableColor;
        if (weight > unwalkableThreshold)
            return unwalkableColor;

        float t = Mathf.InverseLerp(walkableThreshold, unwalkableThreshold, weight);
        return Color.Lerp(walkableColor, cautionColor, t);
    }

    public void SetPath(HashSet<Vector2Int> newPath)
    {
        _prevPathCells = _pathCells;
        _pathCells = newPath ?? new HashSet<Vector2Int>();

        foreach (Vector2Int cell in _prevPathCells)
        {
            if (!_pathCells.Contains(cell))
                RecolorTile(cell);
        }

        foreach (Vector2Int cell in _pathCells)
            RecolorTile(cell);
    }

    void RecolorTile(Vector2Int cell)
    {
        if (!_tileObjects.TryGetValue(cell, out GameObject tile)) return;
        if (tile == null) return;

        Renderer rend = tile.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        bool isPath = _pathCells.Contains(cell);
        Color c;

        if (isPath)
        {
            c = pathColor;
            Vector3 p = tile.transform.position;
            tile.transform.position = new Vector3(p.x, 0.05f, p.z);
            tile.transform.localScale = Vector3.one * 1.1f;
        }
        else
        {
            float weight = GetTerrainWeight(cell);
            c = WeightToColor(weight);
            Vector3 p = tile.transform.position;
            tile.transform.position = new Vector3(p.x, 0f, p.z);
            tile.transform.localScale = Vector3.one;
        }

        rend.material.color = c;
    }

    static Dictionary<string, object> GetNestedDict(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object found) && found is Dictionary<string, object> dict)
            return dict;
        return new Dictionary<string, object>();
    }
}
