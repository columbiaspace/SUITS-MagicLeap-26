using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using TssApi;

/// <summary>
/// Per-frame nav-path orchestrator. Pulls EVA + LTV positions (live TSS or dummy override),
/// runs them through <see cref="TerrainAnalyzer.PosToGrid"/>, asks <see cref="NavPathfinder"/>
/// for a route, and renders the result as a chain of <see cref="tilePrefab"/> instances under
/// a single root that is anchored beneath the XR rig so the start tile lands at the user's feet.
///
/// Execution order -600 ensures <see cref="Awake"/> can bootstrap a missing
/// <see cref="TerrainAnalyzer"/> before its own -500 hook runs.
/// </summary>
[DefaultExecutionOrder(-600)]
public class TileManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrOrigin;
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private GameObject tilePrefab;

    [Header("XR / nav grid ↔ headset")]
    [Tooltip("Re-place the path root so the snapped EVA cell sits under the rig on the first successful path.")]
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

    [Header("EVA override (dummy / testing)")]
    [Tooltip("If true, ignore TSS EVA IMU and use the dummy (x, y) below as the path start. Treated as raw TSS posx/posy and routed through TerrainAnalyzer.PosToGrid identically to live data.")]
    [SerializeField] private bool useDummyEva = false;
    [SerializeField] private Vector2 dummyEvaPosition = new Vector2(-5500f, 8100f);

    [Header("LTV override (dummy / testing)")]
    [Tooltip("If true, ignore TSS LTV location and use the dummy (x, y) below as the goal. Treated as raw TSS last_known_x / last_known_y and routed through TerrainAnalyzer.PosToGrid identically to live data.")]
    [SerializeField] private bool useDummyLtv = true;
    [SerializeField] private Vector2 dummyLtvPosition = new Vector2(-5635f, -8200f);

    private readonly Dictionary<Vector2Int, GameObject> _tileObjects = new Dictionary<Vector2Int, GameObject>();
    private HashSet<Vector2Int> _pathCells = new HashSet<Vector2Int>();
    private Transform _pathTilesRoot;
    private Vector2Int _lastImuGrid;
    private Vector2Int _lastLtvGrid;
    private bool _initialized;
    private bool _navRootLocked;
    private Material _unlitMaterial;

    /// <summary>
    /// Bootstrap a <see cref="TerrainAnalyzer"/> when the scene didn't include one. Only fires
    /// when <see cref="navTerrainMesh"/> is assigned and there's no existing analyzer instance.
    /// </summary>
    void Awake()
    {
        if (!createTerrainAnalyzerIfMissing || navTerrainMesh == null) return;
        if (FindFirstObjectByType<TerrainAnalyzer>(FindObjectsInactive.Include) != null) return;

        GameObject go = new GameObject("TerrainAnalyzer");
        TerrainAnalyzer ta = go.AddComponent<TerrainAnalyzer>();
        ta.SetTerrainMesh(navTerrainMesh);
    }

    /// <summary>
    /// Build the shared unlit material every tile clones and create the empty
    /// <c>NavPathTilesRoot</c> that holds path tiles. Translating that root in
    /// <see cref="AlignPathRootUnderRigForEvaCell"/> is what anchors the path under the user's feet.
    /// </summary>
    void Start()
    {
        _unlitMaterial = new Material(Shader.Find("Unlit/Color"));
        _pathTilesRoot = new GameObject("NavPathTilesRoot").transform;
        _pathTilesRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    /// <summary>
    /// Per-frame driver: resolve the active <see cref="TerrainAnalyzer"/>, sample EVA + LTV
    /// grid cells, and re-run pathfinding only when at least one of them changes (TILE_SIZE
    /// quantization filters jitter).
    /// </summary>
    void Update()
    {
        TerrainAnalyzer terrain = TerrainAnalyzer.Instance
            ?? FindFirstObjectByType<TerrainAnalyzer>(FindObjectsInactive.Include);
        if (terrain == null || !terrain.IsReady) return;

        Vector2Int? imuGrid = GetImuGridPosition(terrain);
        Vector2Int? ltvGrid = GetLtvGridPosition(terrain);
        if (!imuGrid.HasValue || !ltvGrid.HasValue) return;

        bool changed = !_initialized
            || imuGrid.Value != _lastImuGrid
            || ltvGrid.Value != _lastLtvGrid;
        if (!changed) return;

        _lastImuGrid = imuGrid.Value;
        _lastLtvGrid = ltvGrid.Value;
        _initialized = true;

        ComputePath(terrain, imuGrid.Value, ltvGrid.Value);
    }

    /// <summary>
    /// EVA → grid cell. Reads dummy override fields or live TSS IMU (eva1 by default; eva2
    /// nests under <c>imu.eva2</c>). Both branches funnel through
    /// <see cref="TerrainAnalyzer.PosToGrid"/> so calibration applies identically.
    /// </summary>
    Vector2Int? GetImuGridPosition(TerrainAnalyzer terrain)
    {
        float posX, posY;

        if (useDummyEva)
        {
            posX = dummyEvaPosition.x;
            posY = dummyEvaPosition.y;
        }
        else
        {
            TssUnityApiService tss = TssUnityApiService.Instance;
            if (tss == null) return null;

            Dictionary<string, object> imu = evaId == "eva2"
                ? GetNestedDict(GetNestedDict(tss.GetEva(), "imu"), "eva2")
                : tss.GetImuEva1();

            if (imu == null || !imu.ContainsKey("posx") || !imu.ContainsKey("posy")) return null;

            posX = Convert.ToSingle(imu["posx"]);
            posY = Convert.ToSingle(imu["posy"]);
        }

        return terrain.PosToGrid(posX, posY);
    }

    /// <summary>
    /// LTV → grid cell. Mirror of <see cref="GetImuGridPosition"/>: dummy override or
    /// <c>location.last_known_x / last_known_y</c> from TSS, then
    /// <see cref="TerrainAnalyzer.PosToGrid"/>.
    /// </summary>
    Vector2Int? GetLtvGridPosition(TerrainAnalyzer terrain)
    {
        float posX, posY;

        if (useDummyLtv)
        {
            posX = dummyLtvPosition.x;
            posY = dummyLtvPosition.y;
        }
        else
        {
            TssUnityApiService tss = TssUnityApiService.Instance;
            if (tss == null) return null;

            Dictionary<string, object> location = tss.GetLtvLocation();
            if (location == null
                || !location.TryGetValue("last_known_x", out object lx)
                || !location.TryGetValue("last_known_y", out object ly))
                return null;

            posX = Convert.ToSingle(lx);
            posY = Convert.ToSingle(ly);
        }

        return terrain.PosToGrid(posX, posY);
    }

    /// <summary>
    /// Snap both endpoints onto the walkable set and run A*. The resulting cell set (possibly
    /// empty) is handed off to <see cref="ApplyPath"/>. An empty path is a real answer — it
    /// means EVA and LTV are on disconnected walkable regions and there is genuinely no route
    /// — and is rendered as "no tiles" rather than silently relocating an endpoint.
    /// </summary>
    void ComputePath(TerrainAnalyzer terrain, Vector2Int imuGrid, Vector2Int ltvGrid)
    {
        HashSet<Vector2Int> walkableSet = terrain.WalkableSet;
        if (walkableSet.Count < 2) return;

        Vector2Int start = NavGridUtilities.SnapToWalkable(imuGrid, walkableSet);
        Vector2Int goal = NavGridUtilities.SnapToWalkable(ltvGrid, walkableSet);

        List<Vector2Int> path = NavPathfinder.FindPath(walkableSet, terrain, start, goal);
        ApplyPath(path.Count > 0 ? new HashSet<Vector2Int>(path) : new HashSet<Vector2Int>(), start);
    }

    /// <summary>
    /// Translate <c>NavPathTilesRoot</c> so the snapped EVA cell lands directly under
    /// <see cref="xrOrigin"/> (with a small floor offset). Pure translation — no rotation —
    /// so lunar mesh axes line up 1:1 with world axes. Refuses to fall back to
    /// <see cref="Camera.main"/> because that's at eye height and would park the start tile
    /// inside the user's head.
    /// </summary>
    void AlignPathRootUnderRigForEvaCell(Vector2Int evaGridCell)
    {
        if (xrOrigin == null) return;

        Vector3 localEva = NavGridUtilities.CellToLocalTilePos(evaGridCell);
        float yRef = xrOrigin.position.y + pathTilesFloorYOffset;
        Vector3 p = new Vector3(
            xrOrigin.position.x - localEva.x,
            yRef - localEva.y,
            xrOrigin.position.z - localEva.z);
        _pathTilesRoot.SetPositionAndRotation(p, Quaternion.identity);
    }

    /// <summary>
    /// Diff the new path cell set against <see cref="_pathCells"/>: destroy stale tiles,
    /// instantiate new ones (parented to <c>NavPathTilesRoot</c>, colored with
    /// <see cref="pathColor"/>), and on the first successful path translate the root so the
    /// start cell lands beneath <see cref="xrOrigin"/>. Mirrors the cell set to
    /// <see cref="ARMinimapErica"/> so the 2D minimap stays in sync.
    /// </summary>
    void ApplyPath(HashSet<Vector2Int> newPathCells, Vector2Int snappedPathStartEvaCell)
    {
        if (newPathCells.Count == 0)
        {
            if (_pathTilesRoot != null)
            {
                _pathTilesRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
            _navRootLocked = false;

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

            if (useRoot)
            {
                tile.transform.SetParent(_pathTilesRoot, worldPositionStays: false);
                tile.transform.localPosition = NavGridUtilities.CellToLocalTilePos(cell);
                tile.transform.localRotation = Quaternion.identity;
            }
            else
            {
                tile.transform.SetPositionAndRotation(NavGridUtilities.CellToLocalTilePos(cell), Quaternion.identity);
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

        if (useRoot && !_navRootLocked)
        {
            AlignPathRootUnderRigForEvaCell(snappedPathStartEvaCell);
            _navRootLocked = true;
        }

        ARMinimapErica minimap = FindObjectOfType<ARMinimapErica>();
        if (minimap != null) minimap.DrawPathOnMinimap(newPathCells);
    }

    /// <summary>
    /// Safe descend-one-level helper for the nested object dicts TSS hands back (used to reach
    /// <c>imu.eva2</c> from the EVA top-level dict). Returns an empty dict instead of
    /// null/throwing so callers can do a single key lookup without extra guards.
    /// </summary>
    static Dictionary<string, object> GetNestedDict(Dictionary<string, object> source, string key)
    {
        if (source != null && source.TryGetValue(key, out object found) && found is Dictionary<string, object> dict)
            return dict;
        return new Dictionary<string, object>();
    }
}
