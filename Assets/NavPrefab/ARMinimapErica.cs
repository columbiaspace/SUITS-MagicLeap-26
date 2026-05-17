using System.Collections.Generic;
using TssApi;
using UnityEngine;
using UnityEngine.UI;

// Minimap pin placement:
//   TSS posx/posy (meters) → TssCoordsToNormalized → 0..1 fraction on the map image
//   → TssCoordsToMapPixels → anchoredPosition on the RectTransform.
//   Map bounds define which TSS region the image covers; adjust them in the Inspector
//   to match whatever area rock-yard.tiff was exported from.

public class ARMinimapErica : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("TSS")]
    [SerializeField] private TssUnityApiService tssApi;
    [Tooltip("Key inside the imu bucket — must match what TSS sends (e.g. eva1)")]
    [SerializeField] private string evaId = "eva1";

    [Header("Minimap")]
    public RectTransform minimapRect;
    public RectTransform playerIcon;

    [Header("Map Bounds (TSS coordinate ranges the map image covers)")]
    [Tooltip("Leftmost TSS X coordinate shown on the map image")]
    public float mapMinX = -5765f;
    [Tooltip("Rightmost TSS X coordinate shown on the map image")]
    public float mapMaxX = -5545f;
    [Tooltip("Bottom TSS Y coordinate shown on the map image")]
    public float mapMinY = -10075f;
    [Tooltip("Top TSS Y coordinate shown on the map image")]
    public float mapMaxY = -9940f;

    [Header("Trail (traveled path)")]
    [Tooltip("Color of the line drawn as the EVA moves.")]
    public Color trailColor = new Color(1f, 0.55f, 0f, 1f);
    public float trailLineWidth = 2.5f;
    [Tooltip("Minimum TSS distance (m) the EVA must move before a new segment is recorded.")]
    public float recordDistance = 0.5f;
    [Tooltip("Maximum trail points kept; oldest are pruned when exceeded.")]
    public int maxTrailPoints = 500;

    [Header("A* Planned Path")]
    [Tooltip("Compute and draw an A* path from the blue to green waypoint at Start.")]
    [SerializeField] private bool showPlannedPath = true;
    [Tooltip("Color of the A* path line.")]
    public Color plannedPathColor = new Color(0.2f, 1f, 0.8f, 1f);
    public float plannedPathLineWidth = 2.5f;
    [Tooltip("TSS position used as the A* path start (blue waypoint).")]
    public Vector2 pathStart = new Vector2(-5670f, -10060f);
    [Tooltip("TSS position used as the A* path goal (green waypoint).")]
    public Vector2 pathGoal  = new Vector2(-5635f, -9960f);

    [Header("EVA → goal path (live)")]
    [Tooltip("Every evaToLtvPathIntervalSeconds, recompute A* from current EVA position to pathGoal (green waypoint).")]
    [SerializeField] private bool showEvaToLtvPath = true;
    [SerializeField] private float evaToLtvPathIntervalSeconds = 2f;
    public Color evaToLtvPathColor = Color.yellow;
    [SerializeField] private float evaToLtvPathLineWidth = 3.5f;

    [Header("Waypoints")]
    [SerializeField] private bool showWaypoints = true;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = true;
    [SerializeField] private float logIntervalSeconds = 1f;

    [Header("Weight Debug Overlay")]
    [Tooltip("Paints a green→red heatmap of terrain weights directly on the minimap. " +
             "Teal squares = path cells. Disable in production.")]
    [SerializeField] private bool showTerrainOverlay = false;
    [Tooltip("Opacity of the heatmap layer (0=invisible, 1=opaque).")]
    [SerializeField] [Range(0f, 1f)] private float overlayAlpha = 0.55f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    // Trail
    private readonly List<Vector2>    _trailPoints   = new List<Vector2>();
    private readonly List<GameObject> _trailLines    = new List<GameObject>();
    private RectTransform _trailContainer;

    // Planned A* path
    private readonly List<Vector2>    _pathPoints    = new List<Vector2>();
    private readonly List<GameObject> _pathSegments  = new List<GameObject>();
    private RectTransform _pathContainer;

    // Live EVA → LTV A* path (refreshed on a timer)
    private readonly List<Vector2>    _ltvPathPoints   = new List<Vector2>();
    private readonly List<GameObject> _ltvPathSegments = new List<GameObject>();
    private RectTransform _ltvPathContainer;
    private float _evaToLtvPathTimer;
    private bool _evaGoalPathHasDrawn;
    private bool _evaGoalPathUsedTerrainAstar;

    private Vector2 _lastMinimapSize;
    private Vector2 _lastRecordedTssPos;
    private bool    _trailInitialized;
    private bool    _terrainPathSucceeded;   // true once A* found a terrain-based path
    private bool    _terrainPathGaveUp;      // true once terrain was ready but found no path (stop retrying)
    private float   _logTimer;
    private GameObject _overlayGO;          // weight heatmap Image on the minimap

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Always prefer the persistent singleton. Scene-embedded TssUnityApiService
        // GameObjects auto-destroy their script when a singleton from an earlier scene
        // (e.g. Starter) already exists, which leaves any Inspector-wired tssApi
        // references pointing at a destroyed component that never receives updates,
        // so the player icon and trail stop moving.
        if (TssUnityApiService.Instance != null) tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();
        if (tssApi == null)
            Debug.LogError("[ARMinimap] No TssUnityApiService found — assign it in the Inspector.");
    }

    private void Start()
    {
        _trailContainer = CreateSegmentContainer("TrailContainer");
        _pathContainer  = CreateSegmentContainer("PathContainer");
        _ltvPathContainer = CreateSegmentContainer("LtvPathContainer");

        if (showWaypoints) SpawnWaypoints();

        if (showPlannedPath) ComputeAndDrawAStarPath();
        if (showEvaToLtvPath) UpdateEvaToLtvPath(force: true);

        EnsureMinimapLayerOrder();

        _lastMinimapSize = minimapRect != null ? minimapRect.rect.size : Vector2.zero;
    }

    private void Update()
    {
        Dictionary<string, object> imuEva = GetImuBucket();
        UpdatePlayerIcon(imuEva);
        RecordTrail(imuEva);
        RefreshSegmentsIfResized();
        LogDebug(imuEva);
        TickEvaToLtvPath(imuEva);

        // If terrain A* hasn't succeeded yet (TerrainAnalyzer may have been loading),
        // keep trying every frame until it does.
        if (showPlannedPath && !_terrainPathSucceeded)
            TryUpgradeToTerrainPath();

        // Start() often runs before TSS or TerrainAnalyzer are ready — keep trying until
        // the first yellow path is actually drawn, then refresh once terrain A* is available.
        if (showEvaToLtvPath && !_evaGoalPathHasDrawn)
            UpdateEvaToLtvPath(force: false, imuEva);
        else if (showEvaToLtvPath && _evaGoalPathHasDrawn && !_evaGoalPathUsedTerrainAstar
                 && TerrainAnalyzer.Instance != null && TerrainAnalyzer.Instance.IsReady)
            UpdateEvaToLtvPath(force: false, imuEva);
    }

    // Called every frame until TerrainAnalyzer is ready and produces a valid path.
    private void TryUpgradeToTerrainPath()
    {
        if (_terrainPathGaveUp) return;

        TerrainAnalyzer terrain = TerrainAnalyzer.Instance;
        if (terrain == null || !terrain.IsReady) return;

        // Terrain just became ready — replace the straight-line fallback.
        ClearPlannedPath();
        ComputeAndDrawAStarPath();

        // If terrain was available but A* still found no path, stop retrying every frame.
        if (!_terrainPathSucceeded)
        {
            _terrainPathGaveUp = true;
            Debug.LogWarning("[ARMinimap] TerrainAnalyzer was ready but A* found no path — " +
                             "keeping straight-line fallback. Check that the keepout image is " +
                             "assigned and that start/goal TSS positions fall within the map bounds.");
        }
    }

    // -------------------------------------------------------------------------
    // Player icon
    // -------------------------------------------------------------------------

    private void UpdatePlayerIcon(Dictionary<string, object> imuEva)
    {
        if (playerIcon == null || minimapRect == null) return;

        float x       = (float)ToDouble(imuEva, "posx");
        float y       = (float)ToDouble(imuEva, "posy");
        float heading = (float)ToDouble(imuEva, "heading");

        playerIcon.anchoredPosition = TssCoordsToMapPixels(x, y);
        playerIcon.localEulerAngles = new Vector3(0f, 0f, -heading);
    }

    // -------------------------------------------------------------------------
    // Coordinate helpers
    // -------------------------------------------------------------------------

    // Returns the position as a 0..1 fraction within the configured map bounds.
    // (0,0) = bottom-left of the image, (1,1) = top-right.
    private Vector2 TssCoordsToNormalized(float tssX, float tssY)
    {
        return new Vector2(
            Mathf.Clamp01((tssX - mapMinX) / (mapMaxX - mapMinX)),
            Mathf.Clamp01((tssY - mapMinY) / (mapMaxY - mapMinY))
        );
    }

    // Returns an anchoredPosition relative to the center of minimapRect.
    private Vector2 TssCoordsToMapPixels(float tssX, float tssY)
    {
        Vector2 n = TssCoordsToNormalized(tssX, tssY);
        return new Vector2(
            (n.x - 0.5f) * minimapRect.sizeDelta.x,
            (n.y - 0.5f) * minimapRect.sizeDelta.y
        );
    }

    // -------------------------------------------------------------------------
    // Trail (EVA traveled path)
    // -------------------------------------------------------------------------

    private void RecordTrail(Dictionary<string, object> imuEva)
    {
        if (!TryGetEvaTss(imuEva, out Vector2 tssPos)) return;

        if (!_trailInitialized)
        {
            _lastRecordedTssPos = tssPos;
            _trailInitialized   = true;
            return;
        }

        if (Vector2.Distance(tssPos, _lastRecordedTssPos) < recordDistance) return;

        AddTrailPoint(TssCoordsToNormalized(tssPos.x, tssPos.y));
        _lastRecordedTssPos = tssPos;
    }

    private void AddTrailPoint(Vector2 norm)
    {
        if (_trailContainer == null) return;

        if (_trailPoints.Count > 0)
            _trailLines.Add(CreateSegment(_trailContainer, _trailPoints[_trailPoints.Count - 1], norm, trailColor, trailLineWidth));

        _trailPoints.Add(norm);

        while (_trailPoints.Count > maxTrailPoints)
        {
            _trailPoints.RemoveAt(0);
            if (_trailLines.Count > 0) { Destroy(_trailLines[0]); _trailLines.RemoveAt(0); }
        }
    }

    public void ClearTrail()
    {
        foreach (GameObject seg in _trailLines) if (seg != null) Destroy(seg);
        _trailLines.Clear();
        _trailPoints.Clear();
        _trailInitialized = false;
    }

    // -------------------------------------------------------------------------
    // A* planned path
    // -------------------------------------------------------------------------

    private void ComputeAndDrawAStarPath()
    {
        if (_pathContainer == null || minimapRect == null) return;

        List<Vector2> normPoints = ComputeNormPath(pathStart, pathGoal, "planned", logWeights: true,
                                                   out List<Vector2Int> gridPath);
        if (normPoints == null)
        {
            normPoints = new List<Vector2>
            {
                TssCoordsToNormalized(pathStart.x, pathStart.y),
                TssCoordsToNormalized(pathGoal.x,  pathGoal.y)
            };
            Debug.Log("[ARMinimap] Planned path: straight-line fallback " +
                      "(TerrainAnalyzer unavailable or found no path).");
        }
        else
        {
            _terrainPathSucceeded = true;
            if (showTerrainOverlay)
                BuildWeightOverlay(gridPath, TerrainAnalyzer.Instance);
        }

        DrawNormPath(_pathContainer, normPoints, plannedPathColor, plannedPathLineWidth,
                     _pathPoints, _pathSegments);
    }

    // -------------------------------------------------------------------------
    // Live EVA → LTV path (refreshed every N seconds)
    // -------------------------------------------------------------------------

    private void TickEvaToLtvPath(Dictionary<string, object> imuEva)
    {
        if (!showEvaToLtvPath) return;

        _evaToLtvPathTimer += Time.deltaTime;
        if (_evaToLtvPathTimer < evaToLtvPathIntervalSeconds) return;

        _evaToLtvPathTimer = 0f;
        UpdateEvaToLtvPath(force: false, imuEva);
    }

    private void UpdateEvaToLtvPath(bool force, Dictionary<string, object> imuEva = null)
    {
        if (!showEvaToLtvPath || _ltvPathContainer == null || minimapRect == null) return;

        if (!TryGetEvaTss(imuEva, out Vector2 evaTss))
        {
            if (force)
                Debug.Log("[ARMinimap] EVA→goal path: waiting for TSS position inside map bounds.");
            return;
        }

        ClearLtvPath();

        bool usedTerrain = true;
        List<Vector2> normPoints = ComputeNormPath(evaTss, pathGoal, "EVA→goal", logWeights: false,
                                                   out _);
        if (normPoints == null)
        {
            usedTerrain = false;
            normPoints = new List<Vector2>
            {
                TssCoordsToNormalized(evaTss.x, evaTss.y),
                TssCoordsToNormalized(pathGoal.x, pathGoal.y)
            };
        }

        DrawNormPath(_ltvPathContainer, normPoints, evaToLtvPathColor, evaToLtvPathLineWidth,
                     _ltvPathPoints, _ltvPathSegments);
        EnsureMinimapLayerOrder();

        _evaGoalPathHasDrawn = true;
        _evaGoalPathUsedTerrainAstar = usedTerrain;

        Debug.Log($"[ARMinimap] EVA→goal path drawn ({normPoints.Count - 1} segments, " +
                  $"{(usedTerrain ? "terrain A*" : "straight-line fallback")}): " +
                  $"TSS ({evaTss.x:F1}, {evaTss.y:F1}) → green ({pathGoal.x:F1}, {pathGoal.y:F1}).");
    }

    // EVA→goal line renders above trail/planned path but below the player icon.
    private void EnsureMinimapLayerOrder()
    {
        if (minimapRect == null) return;
        if (_ltvPathContainer != null) _ltvPathContainer.SetAsLastSibling();
        if (playerIcon != null) playerIcon.SetAsLastSibling();
    }

    // True when the IMU bucket has coordinates inside (or near) the configured map bounds.
    private bool TryGetEvaTss(Dictionary<string, object> imuEva, out Vector2 tssPos)
    {
        if (imuEva != null)
        {
            tssPos = new Vector2((float)ToDouble(imuEva, "posx"), (float)ToDouble(imuEva, "posy"));
        }
        else
        {
            tssPos = GetEvaTssPosition();
        }

        const float margin = 100f;
        return tssPos.x >= mapMinX - margin && tssPos.x <= mapMaxX + margin
            && tssPos.y >= mapMinY - margin && tssPos.y <= mapMaxY + margin;
    }

    public void ClearLtvPath()
    {
        foreach (GameObject seg in _ltvPathSegments) if (seg != null) Destroy(seg);
        _ltvPathSegments.Clear();
        _ltvPathPoints.Clear();
    }

    // Returns terrain-following A* path as normalized minimap positions, or null on failure.
    private List<Vector2> ComputeNormPath(Vector2 startTss, Vector2 goalTss, string label, bool logWeights,
                                          out List<Vector2Int> gridCells)
    {
        gridCells = null;
        TerrainAnalyzer terrain = TerrainAnalyzer.Instance;
        if (terrain == null || !terrain.IsReady) return null;

        HashSet<Vector2Int> walkable = terrain.WalkableSet;
        if (walkable.Count == 0) return null;

        Vector2Int rawStart = terrain.PosToGrid(startTss.x, startTss.y);
        Vector2Int rawGoal  = terrain.PosToGrid(goalTss.x,  goalTss.y);
        Vector2Int startCell = NavGridUtilities.SnapToWalkable(rawStart, walkable);
        Vector2Int goalCell  = NavGridUtilities.SnapToWalkable(rawGoal,  walkable);

        if (verboseDebug)
        {
            Debug.Log($"[ARMinimap] A* ({label}): TSS({startTss}) → snapped{startCell}  |  " +
                      $"TSS({goalTss}) → snapped{goalCell}");
        }

        List<Vector2Int> path = NavPathfinder.FindPath(walkable, terrain, startCell, goalCell);
        if (path == null || path.Count < 2) return null;

        gridCells = path;

        var normPoints = new List<Vector2>(path.Count);
        foreach (Vector2Int cell in path)
        {
            Vector2 tss = terrain.GridToTssPos(cell);
            normPoints.Add(TssCoordsToNormalized(tss.x, tss.y));
        }

        if (verboseDebug)
            Debug.Log($"[ARMinimap] A* ({label}): {path.Count} cells, {path.Count - 1} segments.");
        if (logWeights) LogPathWeights(path, terrain);

        return normPoints;
    }

    private void DrawNormPath(RectTransform container, List<Vector2> normPoints, Color color,
                                     float lineWidth, List<Vector2> pointStore, List<GameObject> segStore)
    {
        if (container == null || normPoints == null || normPoints.Count < 2) return;

        for (int i = 0; i < normPoints.Count - 1; i++)
        {
            pointStore.Add(normPoints[i]);
            segStore.Add(CreateSegment(container, normPoints[i], normPoints[i + 1], color, lineWidth));
        }
        pointStore.Add(normPoints[normPoints.Count - 1]);
    }

    // Logs each path cell's terrain weight and highlights the heaviest detour points.
    private void LogPathWeights(List<Vector2Int> path, TerrainAnalyzer terrain)
    {
        float totalExtraCost = 0f;
        float maxWeight = 0f;
        int   maxWeightIdx = 0;

        var sb = new System.Text.StringBuilder(
            $"[ARMinimap] Path weight breakdown ({path.Count} cells) ─────────────────\n");

        for (int i = 0; i < path.Count; i++)
        {
            float w = Mathf.Max(0f, terrain.GetWeight(path[i]));
            totalExtraCost += w;
            if (w > maxWeight) { maxWeight = w; maxWeightIdx = i; }

            // Only print cells with non-trivial weight to keep the log readable.
            if (w > 0.05f)
            {
                Vector2 tss = terrain.GridToTssPos(path[i]);
                string bar = new string('█', Mathf.RoundToInt(w * 20));
                sb.Append($"  [{i:D3}] cell{path[i],12}  w={w:F3}  {bar}  TSS({tss.x:F0},{tss.y:F0})\n");
            }
        }

        Vector2 heaviest = terrain.GridToTssPos(path[maxWeightIdx]);
        sb.Append($"  ── Heaviest cell: [{maxWeightIdx:D3}] w={maxWeight:F3}  TSS({heaviest.x:F0},{heaviest.y:F0})\n");
        sb.Append($"  ── Total extra terrain cost vs. straight line: {totalExtraCost:F2}");
        Debug.Log(sb.ToString());
    }

    // Builds a Texture2D weight heatmap and displays it as a semi-transparent Image
    // stretched over the entire minimap rect. Each pixel = one grid cell.
    // Green = easy (weight≈0), yellow = moderate, red/orange = high, bright-red = blocked.
    // Teal pixels = the computed A* path cells.
    private void BuildWeightOverlay(List<Vector2Int> pathCells, TerrainAnalyzer terrain)
    {
        if (minimapRect == null) return;

        terrain.GetGridBounds(out Vector2Int minCell, out Vector2Int maxCell);
        int texW = maxCell.x - minCell.x + 1;
        int texH = maxCell.y - minCell.y + 1;
        if (texW <= 0 || texH <= 0) return;

        float threshold = terrain.ImpassableThreshold;
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp
        };

        // Fill transparent by default (cells outside the image stay clear).
        Color[] pixels = new Color[texW * texH];

        // Walkable cells: green (cost≈0) → orange (cost near threshold).
        foreach (Vector2Int cell in terrain.AllCells)
        {
            float w  = Mathf.Max(0f, terrain.GetWeight(cell));
            int   px = cell.x - minCell.x;
            int   py = cell.y - minCell.y;
            if (px < 0 || px >= texW || py < 0 || py >= texH) continue;

            float t = Mathf.Clamp01(w / threshold);
            pixels[py * texW + px] = Color.Lerp(
                new Color(0.1f, 0.85f, 0.1f, overlayAlpha * 0.5f),   // green  = cheap
                new Color(1f,   0.55f, 0f,   overlayAlpha),           // orange = expensive
                t);
        }

        // Blocked cells (red keepout pixels): always solid red.
        foreach (Vector2Int cell in terrain.BlockedCells)
        {
            int px = cell.x - minCell.x;
            int py = cell.y - minCell.y;
            if (px >= 0 && px < texW && py >= 0 && py < texH)
                pixels[py * texW + px] = new Color(1f, 0.1f, 0.1f, overlayAlpha);
        }

        // Overlay the A* path cells in teal.
        HashSet<Vector2Int> pathSet = new HashSet<Vector2Int>(pathCells);
        foreach (Vector2Int cell in pathSet)
        {
            int px = cell.x - minCell.x;
            int py = cell.y - minCell.y;
            if (px >= 0 && px < texW && py >= 0 && py < texH)
                pixels[py * texW + px] = new Color(0.2f, 1f, 0.85f, Mathf.Min(1f, overlayAlpha + 0.3f));
        }

        tex.SetPixels(pixels);
        tex.Apply();

        // Destroy any previous overlay.
        if (_overlayGO != null) Destroy(_overlayGO);

        _overlayGO = new GameObject("WeightOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _overlayGO.transform.SetParent(minimapRect, false);

        // Place it just above the background but below everything else.
        _overlayGO.transform.SetSiblingIndex(0);

        RectTransform rt = _overlayGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = _overlayGO.GetComponent<Image>();
        img.sprite = Sprite.Create(tex,
            new Rect(0, 0, texW, texH),
            new Vector2(0.5f, 0.5f));
        img.type = Image.Type.Simple;
        img.preserveAspect = false;

        Debug.Log($"[ARMinimap] Weight overlay built: {texW}×{texH} px, " +
                  $"{pathCells.Count} path cells teal, blocked shown in red.");
    }

    public void ClearPlannedPath()
    {
        foreach (GameObject seg in _pathSegments) if (seg != null) Destroy(seg);
        _pathSegments.Clear();
        _pathPoints.Clear();
    }

    // -------------------------------------------------------------------------
    // Shared segment infrastructure
    // -------------------------------------------------------------------------

    // Creates a stretch-fill container parented to minimapRect.
    // Not added to MinimapExpandZoom's scale list, so its children are never squished.
    private RectTransform CreateSegmentContainer(string containerName)
    {
        if (minimapRect == null) return null;
        GameObject go = new GameObject(containerName, typeof(RectTransform));
        go.transform.SetParent(minimapRect, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        return rt;
    }

    // Creates a thin colored Image rect spanning normA → normB inside the given container.
    private GameObject CreateSegment(RectTransform container, Vector2 normA, Vector2 normB, Color color, float width)
    {
        GameObject seg = new GameObject("Seg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        seg.transform.SetParent(container, false);
        UpdateSegmentGeometry(container, seg.GetComponent<RectTransform>(), normA, normB, width);
        seg.GetComponent<Image>().color = color;
        return seg;
    }

    // Positions, sizes, and rotates a segment rect to visually span from normA to normB.
    // Uses a fractional anchor at the midpoint so position stays proportionally correct
    // when the minimap rect resizes, and recomputes pixel length from the current rect size.
    private static void UpdateSegmentGeometry(RectTransform container, RectTransform rt,
                                              Vector2 normA, Vector2 normB, float lineWidth)
    {
        Vector2 mid  = (normA + normB) * 0.5f;
        rt.anchorMin = mid;
        rt.anchorMax = mid;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        float w   = container.rect.width;
        float h   = container.rect.height;
        float dx  = (normB.x - normA.x) * w;
        float dy  = (normB.y - normA.y) * h;
        float len = Mathf.Max(Mathf.Sqrt(dx * dx + dy * dy), 1f);

        rt.sizeDelta        = new Vector2(len, lineWidth);
        rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
    }

    // Recomputes all segment lengths when the minimap is resized (e.g. expand/collapse animation).
    private void RefreshSegmentsIfResized()
    {
        if (minimapRect == null) return;
        Vector2 curSize = minimapRect.rect.size;
        if (Vector2.Distance(curSize, _lastMinimapSize) <= 0.5f) return;

        RefreshSegmentList(_trailContainer,   _trailLines,      _trailPoints,    trailLineWidth);
        RefreshSegmentList(_pathContainer,    _pathSegments,    _pathPoints,     plannedPathLineWidth);
        RefreshSegmentList(_ltvPathContainer, _ltvPathSegments, _ltvPathPoints,  evaToLtvPathLineWidth);
        _lastMinimapSize = curSize;
    }

    private static void RefreshSegmentList(RectTransform container, List<GameObject> segs,
                                           List<Vector2> points, float lineWidth)
    {
        if (container == null) return;
        for (int i = 0; i < segs.Count; i++)
        {
            if (segs[i] == null) continue;
            RectTransform rt = segs[i].GetComponent<RectTransform>();
            if (rt != null)
                UpdateSegmentGeometry(container, rt, points[i], points[i + 1], lineWidth);
        }
    }

    // -------------------------------------------------------------------------
    // Waypoint / pin dots
    // -------------------------------------------------------------------------

    private void SpawnWaypoints()
    {
        if (minimapRect == null) return;
        SpawnWaypointDot(new Vector2(-5670f, -10060f), Color.blue,   "WP_Blue");
        SpawnWaypointDot(new Vector2(-5635f, -9960f),  Color.green,  "WP_Green");
        SpawnWaypointDot(new Vector2(-5515f, -9995f),  Color.yellow, "WP_Yellow");
    }

    private void SpawnWaypointDot(Vector2 tssPos, Color color, string dotName)
    {
        AddMapPin(tssPos.x, tssPos.y, color, 8f, dotName);
    }

    /// <summary>
    /// Spawns a colored dot on the minimap at the given TSS position and returns the
    /// GameObject so the caller can track or remove it later. Uses fractional anchors
    /// so the dot stays in the correct relative position when the minimap resizes.
    /// </summary>
    public GameObject AddMapPin(float tssX, float tssY, Color color, float size = 8f, string dotName = "Pin")
    {
        if (minimapRect == null) return null;
        Vector2 norm = TssCoordsToNormalized(tssX, tssY);

        GameObject dot = new GameObject(dotName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dot.transform.SetParent(minimapRect, false);
        dot.transform.SetAsFirstSibling();

        RectTransform rt = dot.GetComponent<RectTransform>();
        rt.anchorMin        = norm;
        rt.anchorMax        = norm;
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;

        dot.GetComponent<Image>().color = color;
        return dot;
    }

    /// <summary>
    /// Returns the current TSS (posx, posy) for the configured EVA ID, or Vector2.zero
    /// if the TSS data is not yet available. Used by external scripts (e.g. PinchPingSpawner)
    /// that need the EVA position without duplicating the IMU-bucket parsing logic.
    /// </summary>
    public Vector2 GetEvaTssPosition()
    {
        Dictionary<string, object> imuEva = GetImuBucket();
        return new Vector2(
            (float)ToDouble(imuEva, "posx"),
            (float)ToDouble(imuEva, "posy")
        );
    }

    // -------------------------------------------------------------------------
    // Debug logging
    // -------------------------------------------------------------------------

    private void LogDebug(Dictionary<string, object> imuEva)
    {
        if (!verboseDebug) return;
        _logTimer += Time.deltaTime;
        if (_logTimer < logIntervalSeconds) return;
        _logTimer = 0f;

        float x       = (float)ToDouble(imuEva, "posx");
        float y       = (float)ToDouble(imuEva, "posy");
        float heading = (float)ToDouble(imuEva, "heading");
        Vector2 n     = TssCoordsToNormalized(x, y);
        Vector2 px    = minimapRect != null ? TssCoordsToMapPixels(x, y) : Vector2.zero;

        string bucket = imuEva == null
            ? $"NULL (wrong evaId or TSS not connected)"
            : $"OK [{string.Join(", ", new List<string>(imuEva.Keys))}]";

        Debug.Log(
            $"[ARMinimap] imu[\"{evaId}\"]: {bucket}\n" +
            $"  TSS ({x:F1}, {y:F1})  heading {heading:F1}°\n" +
            $"  normalized ({n.x:F3}, {n.y:F3})  anchoredPos ({px.x:F1}, {px.y:F1})\n" +
            $"  trail pts:{_trailPoints.Count}  path segs:{_pathSegments.Count}  ltv segs:{_ltvPathSegments.Count}"
        );
    }

    // -------------------------------------------------------------------------
    // TSS data helpers
    // -------------------------------------------------------------------------

    private Dictionary<string, object> GetImuBucket()
    {
        if (tssApi == null) return null;
        Dictionary<string, object> eva = tssApi.GetEva();

        if (eva == null || !eva.TryGetValue("imu", out object imuObj)) return null;
        Dictionary<string, object> imu = imuObj as Dictionary<string, object>;

        if (imu == null || !imu.TryGetValue(evaId, out object bucket)) return null;
        return bucket as Dictionary<string, object>;
    }

    private static double ToDouble(Dictionary<string, object> dict, string key)
    {
        if (dict == null || !dict.TryGetValue(key, out object val) || val == null) return 0d;
        if (val is double d) return d;
        if (val is float  f) return f;
        if (val is int    i) return i;
        if (val is long   l) return l;
        if (val is string s && double.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double p)) return p;
        try   { return System.Convert.ToDouble(val, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0d; }
    }
}
