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

    [Header("Waypoints")]
    [SerializeField] private bool showWaypoints = true;

    [Header("Debug")]
    [SerializeField] private bool verboseDebug = true;
    [SerializeField] private float logIntervalSeconds = 1f;

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

    private Vector2 _lastMinimapSize;
    private Vector2 _lastRecordedTssPos;
    private bool    _trailInitialized;
    private float   _logTimer;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (tssApi == null) tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();
        if (tssApi == null)
            Debug.LogError("[ARMinimap] No TssUnityApiService found — assign it in the Inspector.");
    }

    private void Start()
    {
        _trailContainer = CreateSegmentContainer("TrailContainer");
        _pathContainer  = CreateSegmentContainer("PathContainer");

        if (showWaypoints) SpawnWaypoints();

        // Render order (bottom → top): waypoints → trail → planned path → player icon
        if (playerIcon != null)
        {
            _trailContainer.SetSiblingIndex(playerIcon.GetSiblingIndex());
            _pathContainer.SetSiblingIndex(playerIcon.GetSiblingIndex());
        }

        if (showPlannedPath) ComputeAndDrawAStarPath();

        _lastMinimapSize = minimapRect != null ? minimapRect.rect.size : Vector2.zero;
    }

    private void Update()
    {
        Dictionary<string, object> imuEva = GetImuBucket();
        UpdatePlayerIcon(imuEva);
        RecordTrail(imuEva);
        RefreshSegmentsIfResized();
        LogDebug(imuEva);
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
        Vector2 tssPos = imuEva != null
            ? new Vector2((float)ToDouble(imuEva, "posx"), (float)ToDouble(imuEva, "posy"))
            : Vector2.zero;

        if (tssPos == Vector2.zero) return;

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

        TerrainAnalyzer terrain = TerrainAnalyzer.Instance;
        if (terrain == null || !terrain.IsReady)
        {
            Debug.LogWarning("[ARMinimap] TerrainAnalyzer not ready — A* path skipped. " +
                             "Ensure TerrainAnalyzer is in the scene with a mesh assigned.");
            return;
        }

        HashSet<Vector2Int> walkable = terrain.WalkableSet;

        // Map TSS positions to grid cells, snapping to nearest walkable if needed
        Vector2Int startCell = NavGridUtilities.SnapToWalkable(terrain.PosToGrid(pathStart.x, pathStart.y), walkable);
        Vector2Int goalCell  = NavGridUtilities.SnapToWalkable(terrain.PosToGrid(pathGoal.x,  pathGoal.y),  walkable);

        List<Vector2Int> path = NavPathfinder.FindPath(walkable, terrain, startCell, goalCell);

        if (path == null || path.Count < 2)
        {
            Debug.LogWarning($"[ARMinimap] A* found no path from {pathStart} to {pathGoal}. " +
                             "Check that both points fall within the walkable terrain.");
            return;
        }

        // Draw each step as a line segment on the minimap
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 normA = TssCoordsToNormalized(terrain.GridToTssPos(path[i]).x,     terrain.GridToTssPos(path[i]).y);
            Vector2 normB = TssCoordsToNormalized(terrain.GridToTssPos(path[i + 1]).x, terrain.GridToTssPos(path[i + 1]).y);
            _pathPoints.Add(normA);
            _pathSegments.Add(CreateSegment(_pathContainer, normA, normB, plannedPathColor, plannedPathLineWidth));
        }
        _pathPoints.Add(TssCoordsToNormalized(terrain.GridToTssPos(path[path.Count - 1]).x,
                                              terrain.GridToTssPos(path[path.Count - 1]).y));

        Debug.Log($"[ARMinimap] A* path: {path.Count} cells, {_pathSegments.Count} segments drawn.");
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

        RefreshSegmentList(_trailContainer,  _trailLines,    _trailPoints,  trailLineWidth);
        RefreshSegmentList(_pathContainer,   _pathSegments,  _pathPoints,   plannedPathLineWidth);
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
    // Waypoint dots
    // -------------------------------------------------------------------------

    private void SpawnWaypoints()
    {
        if (minimapRect == null) return;
        SpawnWaypointDot(new Vector2(-5670f, -10060f), Color.blue,   "WP_Blue");
        SpawnWaypointDot(new Vector2(-5635f, -9960f),  Color.green,  "WP_Green");
        SpawnWaypointDot(new Vector2(-5515f, -9995f),  Color.yellow, "WP_Yellow");
    }

    // Uses fractional anchors so the dot stays at the correct map position on resize.
    private void SpawnWaypointDot(Vector2 tssPos, Color color, string dotName)
    {
        Vector2 norm = TssCoordsToNormalized(tssPos.x, tssPos.y);
        GameObject dot = new GameObject(dotName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dot.transform.SetParent(minimapRect, false);
        dot.transform.SetAsFirstSibling();

        RectTransform rt    = dot.GetComponent<RectTransform>();
        rt.anchorMin        = norm;
        rt.anchorMax        = norm;
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(8f, 8f);
        rt.anchoredPosition = Vector2.zero;

        dot.GetComponent<Image>().color = color;
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
            $"  trail pts:{_trailPoints.Count}  path segs:{_pathSegments.Count}"
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
