using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using TssApi;

/// <summary>Runs early so optional TerrainAnalyzer bootstrap runs before other Awakes.</summary>
[DefaultExecutionOrder(-600)]
public class TileManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrOrigin;
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private GameObject tilePrefab;

    [Header("XR / nav grid ↔ headset")]
    [Tooltip("When TSS updates EVA/LTV, re-place the path root so the snapped EVA cell is under the rig. Walking in the room does not move EVA in sim, so the path stays world-fixed until the next telemetry update.")]
    [SerializeField] private bool alignInitialPathToRig = true;
    [Tooltip("Vertical offset from rig feet for the nav plane (meters).")]
    [SerializeField] private float pathTilesFloorYOffset = 0.02f;

    [Header("Terrain (if no TerrainAnalyzer in scene)")]
    [Tooltip("Assign the lunar nav mesh (e.g. from lunar.obj). Used only when no TerrainAnalyzer exists.")]
    [SerializeField] private Mesh navTerrainMesh;
    [SerializeField] private bool createTerrainAnalyzerIfMissing = true;

    [Header("Path Color")]
    [SerializeField] private Color pathColor = new Color(0.2f, 0.5f, 1.0f, 0.9f);

    [Header("IMU Position")]
    [SerializeField] private string evaId = "eva1";

    [Header("Nav grid debug (OnGUI)")]
    [SerializeField] private bool showNavGridOverlay = true;
    [SerializeField] private int navGridMarginCells = 12;
    [SerializeField] private float navGridMaxDisplayPx = 480f;

    public const float TILE_SIZE = 0.6096f;

    private Dictionary<Vector2Int, GameObject> _tileObjects = new Dictionary<Vector2Int, GameObject>();
    private HashSet<Vector2Int> _pathCells = new HashSet<Vector2Int>();

    Transform _pathTilesRoot;

    private Vector2Int _lastImuGrid;
    private Vector2Int _lastLtvGrid;
    private bool _initialized;

    private Material _unlitMaterial;
    private string _debugStatus = "TileManager: Initializing...";

    private Texture2D _navDebugTex;
    private bool _navDebugHaveSnapshot;
    private Vector2Int _navDbgStart, _navDbgGoal, _navDbgImuRaw, _navDbgLtvRaw;
    private Vector2Int _navDbgCellMin;
    private int _navDbgW, _navDbgH;
    private Vector2Int _navDbgLastImu = new Vector2Int(int.MinValue, 0);
    private Vector2Int _navDbgLastLtv = new Vector2Int(int.MinValue, 0);
    private int _navDbgLastPathCount = -1;

    void Awake()
    {
        if (!createTerrainAnalyzerIfMissing || navTerrainMesh == null)
        {
            return;
        }

        if (FindFirstObjectByType<TerrainAnalyzer>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        GameObject go = new GameObject("TerrainAnalyzer");
        TerrainAnalyzer ta = go.AddComponent<TerrainAnalyzer>();
        ta.SetTerrainMesh(navTerrainMesh);
    }

    void Start()
    {
        _unlitMaterial = new Material(Shader.Find("Unlit/Color"));
        _pathTilesRoot = new GameObject("NavPathTilesRoot").transform;
        _pathTilesRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    void Update()
    {
        TerrainAnalyzer terrain = TerrainAnalyzer.Instance
            ?? FindFirstObjectByType<TerrainAnalyzer>(FindObjectsInactive.Include);

        if (terrain == null || !terrain.IsReady)
        {
            _debugStatus = terrain == null
                ? "TileManager: No TerrainAnalyzer — assign Nav Terrain Mesh on TileManager or add a TerrainAnalyzer to the scene"
                : "TileManager: TerrainAnalyzer not ready yet (mesh missing or grid still building)...";
            return;
        }

        Vector2Int? imuGrid = GetImuGridPosition(terrain);
        Vector2Int? ltvGrid = GetLtvGridPosition(terrain);

        if (!imuGrid.HasValue || !ltvGrid.HasValue)
        {
            _debugStatus = BuildNavWaitStatus(TssUnityApiService.Instance);
            _navDebugHaveSnapshot = false;
            return;
        }

        bool changed = !_initialized
            || imuGrid.Value != _lastImuGrid
            || ltvGrid.Value != _lastLtvGrid;
        if (changed)
        {
            _lastImuGrid = imuGrid.Value;
            _lastLtvGrid = ltvGrid.Value;
            _initialized = true;

            ComputePath(terrain, imuGrid.Value, ltvGrid.Value);
        }

        MaybeRebuildNavDebugTexture(terrain, imuGrid.Value, ltvGrid.Value);
    }

    void OnDestroy()
    {
        if (_navDebugTex != null)
        {
            Destroy(_navDebugTex);
            _navDebugTex = null;
        }
    }

    void OnGUI()
    {
        GUIStyle small = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Normal,
            normal = { textColor = Color.white },
            wordWrap = true
        };

        if (showNavGridOverlay && _navDebugHaveSnapshot && _navDebugTex != null)
        {
            float scale = Mathf.Min(
                navGridMaxDisplayPx / _navDebugTex.width,
                navGridMaxDisplayPx / _navDebugTex.height);
            scale = Mathf.Clamp(scale, 2f, 12f);
            float dw = _navDebugTex.width * scale;
            float dh = _navDebugTex.height * scale;
            Rect panel = new Rect(Screen.width - dw - 20f, 10f, dw, dh);

            GUI.Box(new Rect(panel.x - 6f, panel.y - 6f, panel.width + 12f, panel.height + 12f), GUIContent.none);
            GUI.DrawTexture(panel, _navDebugTex, ScaleMode.StretchToFill, false);

            string legend =
                $"1 px = 1 cell. Bounds ({_navDbgCellMin.x},{_navDbgCellMin.y}) → ({_navDbgCellMin.x + _navDbgW - 1},{_navDbgCellMin.y + _navDbgH - 1})\n" +
                $"Start (snapped) {_navDbgStart}  |  IMU raw {_navDbgImuRaw}\n" +
                $"Goal (snapped) {_navDbgGoal}  |  LTV raw {_navDbgLtvRaw}\n" +
                "Green=start  Red=goal  Blue=path  Walkable=floor  Dark red=blocked  Gray=no mesh  Cyan/Magenta=raw only if off snapped cell";
            GUI.Label(new Rect(panel.x, panel.yMax + 8f, Mathf.Min(panel.width + 40f, Screen.width - 40f), 96f), legend, small);
        }

        GUI.Label(new Rect(12f, Screen.height - 52f, Screen.width - 24f, 44f), _debugStatus, small);
    }

    void MaybeRebuildNavDebugTexture(TerrainAnalyzer terrain, Vector2Int imuRaw, Vector2Int ltvRaw)
    {
        if (!showNavGridOverlay)
        {
            return;
        }

        HashSet<Vector2Int> ws = terrain.WalkableSet;
        if (ws == null || ws.Count == 0)
        {
            return;
        }

        Vector2Int startSn = SnapToWalkable(imuRaw, ws);
        Vector2Int goalSn = SnapToWalkable(ltvRaw, ws);

        int pathCount = _pathCells.Count;
        if (imuRaw == _navDbgLastImu && ltvRaw == _navDbgLastLtv && pathCount == _navDbgLastPathCount)
        {
            return;
        }

        _navDbgLastImu = imuRaw;
        _navDbgLastLtv = ltvRaw;
        _navDbgLastPathCount = pathCount;
        RebuildNavDebugTexture(terrain, imuRaw, ltvRaw, startSn, goalSn);
    }

    void RebuildNavDebugTexture(
        TerrainAnalyzer terrain,
        Vector2Int imuRaw,
        Vector2Int ltvRaw,
        Vector2Int startSn,
        Vector2Int goalSn)
    {
        int m = Mathf.Max(0, navGridMarginCells);
        int minX = Mathf.Min(startSn.x, goalSn.x, imuRaw.x, ltvRaw.x) - m;
        int maxX = Mathf.Max(startSn.x, goalSn.x, imuRaw.x, ltvRaw.x) + m;
        int minY = Mathf.Min(startSn.y, goalSn.y, imuRaw.y, ltvRaw.y) - m;
        int maxY = Mathf.Max(startSn.y, goalSn.y, imuRaw.y, ltvRaw.y) + m;

        int w = maxX - minX + 1;
        int h = maxY - minY + 1;

        if (_navDebugTex == null || _navDebugTex.width != w || _navDebugTex.height != h)
        {
            if (_navDebugTex != null)
            {
                Destroy(_navDebugTex);
            }

            _navDebugTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        _navDbgCellMin = new Vector2Int(minX, minY);
        _navDbgW = w;
        _navDbgH = h;
        _navDbgStart = startSn;
        _navDbgGoal = goalSn;
        _navDbgImuRaw = imuRaw;
        _navDbgLtvRaw = ltvRaw;
        _navDebugHaveSnapshot = true;

        Color[] px = new Color[w * h];
        for (int row = 0; row < h; row++)
        {
            int gy = maxY - row;
            for (int col = 0; col < w; col++)
            {
                int gx = minX + col;
                Vector2Int g = new Vector2Int(gx, gy);

                Color c;
                if (!terrain.HasData(g))
                {
                    c = new Color(0.12f, 0.12f, 0.14f);
                }
                else if (terrain.IsWalkable(g))
                {
                    c = new Color(0.22f, 0.42f, 0.26f);
                }
                else
                {
                    c = new Color(0.38f, 0.2f, 0.2f);
                }

                if (_pathCells.Contains(g))
                {
                    c = Color.Lerp(c, pathColor, 0.58f);
                }

                if (g == startSn)
                {
                    c = Color.Lerp(c, new Color(0.2f, 0.95f, 0.35f), 0.82f);
                }

                if (g == goalSn)
                {
                    c = Color.Lerp(c, new Color(0.95f, 0.25f, 0.22f), 0.82f);
                }

                if (g == imuRaw && imuRaw != startSn)
                {
                    c = Color.Lerp(c, Color.cyan, 0.65f);
                }

                if (g == ltvRaw && ltvRaw != goalSn)
                {
                    c = Color.Lerp(c, Color.magenta, 0.65f);
                }

                px[row * w + col] = c;
            }
        }

        _navDebugTex.SetPixels(px);
        _navDebugTex.Apply(false);
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
        if (location == null
            || !location.TryGetValue("last_known_x", out object lx)
            || !location.TryGetValue("last_known_y", out object ly))
            return null;

        float dustX = Convert.ToSingle(lx);
        float dustY = Convert.ToSingle(ly);
        return terrain.DustToGrid(dustX, dustY);
    }

    string BuildNavWaitStatus(TssUnityApiService tss)
    {
        if (tss == null)
        {
            return "TileManager: nav wait — no TssUnityApiService in scene";
        }

        Dictionary<string, object> d = tss.GetPollDiagnostics();
        string host = DiagStr(d, "tss_host");
        string port = DiagStr(d, "tss_port");
        bool udpInit = DiagBool(d, "udp_initialized");

        if (!udpInit)
        {
            string initErr = DiagStr(d, "udp_init_error");
            return $"TileManager: TSS UDP not initialized → {host}:{port}\nInit error: {(string.IsNullOrEmpty(initErr) ? "(none captured)" : initErr)}";
        }

        bool evaUdp = DiagBool(d, "eva_udp_ok");
        bool ltvUdp = DiagBool(d, "ltv_udp_ok");
        string evaLine = evaUdp
            ? $"EVA UDP ok — {ImuSchemaHint(tss)}"
            : $"EVA UDP fail — {DiagStr(d, "eva_udp_error")}";
        string ltvLine = ltvUdp
            ? $"LTV UDP ok — {LtvSchemaHint(tss)}"
            : $"LTV UDP fail — {DiagStr(d, "ltv_udp_error")}";

        return $"TileManager: waiting for nav positions ({host}:{port})\n{evaLine}\n{ltvLine}";
    }

    string ImuSchemaHint(TssUnityApiService tss)
    {
        Dictionary<string, object> imu = evaId == "eva2"
            ? GetNestedDict(GetNestedDict(tss.GetEva(), "imu"), "eva2")
            : tss.GetImuEva1();

        if (imu == null || imu.Count == 0)
        {
            return "schema: imu data empty (EVA JSON may be missing imu.* block)";
        }

        if (!imu.ContainsKey("posx") || !imu.ContainsKey("posy"))
        {
            return $"schema: need imu.{evaId}.posx & posy (present: {string.Join(", ", imu.Keys)})";
        }

        return "imu keys ok";
    }

    string LtvSchemaHint(TssUnityApiService tss)
    {
        Dictionary<string, object> loc = tss.GetLtvLocation();
        if (loc == null || loc.Count == 0)
        {
            return "schema: location empty (LTV JSON may be missing location block)";
        }

        if (!loc.ContainsKey("last_known_x") || !loc.ContainsKey("last_known_y"))
        {
            return $"schema: need location.last_known_x & last_known_y (present: {string.Join(", ", loc.Keys)})";
        }

        return "ltv location keys ok";
    }

    static string DiagStr(Dictionary<string, object> d, string key)
    {
        if (d == null || !d.TryGetValue(key, out object o) || o == null)
        {
            return string.Empty;
        }

        return o.ToString();
    }

    static bool DiagBool(Dictionary<string, object> d, string key)
    {
        if (d == null || !d.TryGetValue(key, out object o))
        {
            return false;
        }

        if (o is bool b)
        {
            return b;
        }

        return o != null && bool.TryParse(o.ToString(), out bool p) && p;
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

        bool startAdjusted = false;
        bool goalAdjusted = false;

        List<Vector2Int> path = NavPathfinder.FindPath(walkableSet, terrain, start, goal);

        if (path.Count == 0)
        {
            HashSet<Vector2Int> goalRegion = NavPathfinder.WalkableComponentContaining(walkableSet, goal);
            if (goalRegion.Count > 0)
            {
                Vector2Int start2 = ClosestInSet(imuGrid, goalRegion);
                if (start2 != start)
                {
                    start = start2;
                    startAdjusted = true;
                    path = NavPathfinder.FindPath(walkableSet, terrain, start, goal);
                }
            }
        }

        if (path.Count == 0)
        {
            HashSet<Vector2Int> startRegion = NavPathfinder.WalkableComponentContaining(walkableSet, start);
            if (startRegion.Count > 0)
            {
                Vector2Int goal2 = ClosestInSet(ltvGrid, startRegion);
                if (goal2 != goal)
                {
                    goal = goal2;
                    goalAdjusted = true;
                    path = NavPathfinder.FindPath(walkableSet, terrain, start, goal);
                }
            }
        }

        if (path.Count > 0)
        {
            string tag = (startAdjusted || goalAdjusted)
                ? $" (adjusted start→{start} goal→{goal} to same walkable island)"
                : string.Empty;
            _debugStatus = $"Path: {path.Count} tiles from {start} to {goal}{tag}";
            Debug.Log($"TileManager: Path found — {path.Count} steps.{tag} Grid cells: {FormatPathCells(path)}");
            ApplyPath(new HashSet<Vector2Int>(path), start);
        }
        else
        {
            _debugStatus = $"No path from snapped EVA/LTV (TerrainAnalyzer may need looser thresholds or DUST/OBJ mapping fix)";
            Debug.LogWarning(
                $"{_debugStatus} Raw imu→{imuGrid} ltv→{ltvGrid}; walkable cells={walkableSet.Count}.");
            ApplyPath(new HashSet<Vector2Int>(), start);
        }
    }

    static Vector2Int ClosestInSet(Vector2Int p, HashSet<Vector2Int> set)
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

    Vector2Int SnapToWalkable(Vector2Int cell, HashSet<Vector2Int> walkableSet)
    {
        if (walkableSet.Contains(cell))
        {
            return cell;
        }

        return ClosestInSet(cell, walkableSet);
    }

    static Vector3 CellToLocalTilePos(Vector2Int cell)
    {
        return new Vector3(cell.x * TILE_SIZE, 0.05f, cell.y * TILE_SIZE);
    }

    void AlignPathRootUnderRigForEvaCell(Vector2Int evaGridCell)
    {
        Transform rig = xrOrigin != null ? xrOrigin : Camera.main != null ? Camera.main.transform : null;
        if (rig == null)
        {
            return;
        }

        Vector3 localEva = CellToLocalTilePos(evaGridCell);
        float yRef = rig.position.y + pathTilesFloorYOffset;
        Vector3 p = new Vector3(rig.position.x - localEva.x, yRef - localEva.y, rig.position.z - localEva.z);
        _pathTilesRoot.SetPositionAndRotation(p, Quaternion.identity);
    }

    void ApplyPath(HashSet<Vector2Int> newPathCells, Vector2Int snappedPathStartEvaCell)
    {
        if (newPathCells.Count == 0)
        {
            if (_pathTilesRoot != null)
            {
                _pathTilesRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            foreach (Vector2Int cell in _pathCells)
            {
                if (_tileObjects.TryGetValue(cell, out GameObject old))
                {
                    Destroy(old);
                    _tileObjects.Remove(cell);
                }
            }

            _pathCells = newPathCells;
            ARMinimapErica minimap0 = FindObjectOfType<ARMinimapErica>();
            if (minimap0 != null) minimap0.DrawPathOnMinimap(newPathCells);
            return;
        }

        foreach (Vector2Int cell in _pathCells)
        {
            if (!newPathCells.Contains(cell) && _tileObjects.TryGetValue(cell, out GameObject old))
            {
                Destroy(old);
                _tileObjects.Remove(cell);
            }
        }

        bool useRoot = alignInitialPathToRig && _pathTilesRoot != null;

        foreach (Vector2Int cell in newPathCells)
        {
            if (_tileObjects.ContainsKey(cell)) continue;

            GameObject tile = Instantiate(tilePrefab);
            tile.transform.localScale = Vector3.one * 1.1f;

            if (useRoot)
            {
                tile.transform.SetParent(_pathTilesRoot, worldPositionStays: false);
                tile.transform.localPosition = CellToLocalTilePos(cell);
                tile.transform.localRotation = Quaternion.identity;
            }
            else
            {
                tile.transform.SetPositionAndRotation(CellToLocalTilePos(cell), Quaternion.identity);
            }

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

        if (useRoot)
        {
            AlignPathRootUnderRigForEvaCell(snappedPathStartEvaCell);
        }

        ARMinimapErica minimap = FindObjectOfType<ARMinimapErica>();
        if (minimap != null) minimap.DrawPathOnMinimap(newPathCells);
    }

    static Dictionary<string, object> GetNestedDict(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object found) && found is Dictionary<string, object> dict)
            return dict;
        return new Dictionary<string, object>();
    }

    static string FormatPathCells(List<Vector2Int> path)
    {
        if (path == null || path.Count == 0)
        {
            return "(empty)";
        }

        var parts = new string[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int c = path[i];
            parts[i] = $"({c.x},{c.y})";
        }

        return string.Join(" → ", parts);
    }
}
