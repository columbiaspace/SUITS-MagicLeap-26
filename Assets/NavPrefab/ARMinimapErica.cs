using System.Collections.Generic;
using TssApi;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// Minimap pin placement:
//   TSS IMU posx/posy → EvaTssCoordinateAdjust → rock-yard TSS (meters) → TssCoordsToNormalized
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

    [Header("Nav waypoints (TSS metres)")]
    [Tooltip("Blue waypoint — EVA base / return target.")]
    [FormerlySerializedAs("pathStart")]
    public Vector2 baseTss = new Vector2(-5670f, -10060f);
    [Tooltip("Green waypoint — LTV1.")]
    [FormerlySerializedAs("pathGoal")]
    public Vector2 ltv1Tss = new Vector2(-5635f, -9960f);
    [Tooltip("Purple waypoint — LTV2.")]
    public Vector2 ltv2Tss = new Vector2(-5615f, -9995f);

    [Header("EVA voice nav path")]
    [Tooltip("A* path from current EVA position; drawn by NavVoiceCoordinator voice commands.")]
    public Color voiceNavPathColor = Color.yellow;
    [SerializeField] private float voiceNavPathLineWidth = 3.5f;

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

    // Voice-triggered EVA path
    private readonly List<Vector2>    _voiceNavPoints   = new List<Vector2>();
    private readonly List<Vector2>    _voiceNavPathTss  = new List<Vector2>();
    private readonly List<GameObject> _voiceNavSegments = new List<GameObject>();
    private RectTransform _voiceNavContainer;
    private bool _voiceNavPathActive;
    private VoiceNavGoal _voiceNavGoal = VoiceNavGoal.None;
    private bool _voiceNavUsedTerrainAstar;

    private enum VoiceNavGoal { None, Ltv1, Ltv2, Base }

    private Vector2 _lastMinimapSize;
    private Vector2 _lastRecordedTssPos;
    private bool    _trailInitialized;
    private float   _logTimer;

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
        _voiceNavContainer = CreateSegmentContainer("VoiceNavPathContainer");

        if (showWaypoints) SpawnWaypoints();

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

        // Voice path may have been drawn with a straight-line fallback before TerrainAnalyzer was ready.
        if (!_voiceNavPathActive && _voiceNavGoal != VoiceNavGoal.None)
        {
            RetryActiveVoiceNavPath(imuEva);
        }

        if (_voiceNavPathActive && !_voiceNavUsedTerrainAstar
            && TerrainAnalyzer.Instance != null && TerrainAnalyzer.Instance.IsReady)
        {
            RetryActiveVoiceNavPath(imuEva);
        }
    }

    private void RetryActiveVoiceNavPath(Dictionary<string, object> imuEva)
    {
        switch (_voiceNavGoal)
        {
            case VoiceNavGoal.Ltv1:
                DrawVoiceNavPath(ltv1Tss, retryOnly: true, imuEva, "EVA→LTV1");
                break;
            case VoiceNavGoal.Ltv2:
                DrawVoiceNavPath(ltv2Tss, retryOnly: true, imuEva, "EVA→LTV2");
                break;
            case VoiceNavGoal.Base:
                DrawVoiceNavPath(baseTss, retryOnly: true, imuEva, "EVA→base");
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Player icon
    // -------------------------------------------------------------------------

    private void UpdatePlayerIcon(Dictionary<string, object> imuEva)
    {
        if (playerIcon == null || minimapRect == null) return;

        if (imuEva == null) return;

        Vector2 pos   = ImuToNavTss(imuEva);
        float heading = ReadImuHeading();

        playerIcon.anchoredPosition = TssCoordsToMapPixels(pos.x, pos.y);
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
    // Voice-triggered EVA path
    // -------------------------------------------------------------------------

    /// <summary>A* path from current EVA position to LTV1 (green waypoint).</summary>
    public bool VoiceGoToLtv1()
    {
        _voiceNavGoal = VoiceNavGoal.Ltv1;
        if (!DrawVoiceNavPath(ltv1Tss, retryOnly: false, imuEva: null, label: "EVA→LTV1"))
            return true;
        return true;
    }

    /// <summary>A* path from current EVA position to LTV2 (yellow waypoint).</summary>
    public bool VoiceGoToLtv2()
    {
        _voiceNavGoal = VoiceNavGoal.Ltv2;
        if (!DrawVoiceNavPath(ltv2Tss, retryOnly: false, imuEva: null, label: "EVA→LTV2"))
            return true;
        return true;
    }

    /// <summary>A* path from current EVA position to base (blue waypoint).</summary>
    public bool VoiceReturnToBase()
    {
        _voiceNavGoal = VoiceNavGoal.Base;
        if (!DrawVoiceNavPath(baseTss, retryOnly: false, imuEva: null, label: "EVA→base"))
            return true;
        return true;
    }

    /// <summary>True while a voice-triggered path is drawn (yellow line on minimap).</summary>
    public bool VoiceNavPathActive => _voiceNavPathActive;

    /// <summary>TSS metre waypoints for the active voice path (for ground navigation arrow).</summary>
    public IReadOnlyList<Vector2> VoiceNavPathTss => _voiceNavPathTss;

    /// <summary>Removes the yellow voice navigation path.</summary>
    public void ClearVoiceNavPath()
    {
        ClearVoiceNavSegments();
        _voiceNavPathTss.Clear();
        _voiceNavPathActive = false;
        _voiceNavGoal = VoiceNavGoal.None;
        _voiceNavUsedTerrainAstar = false;
        Debug.Log("[ARMinimap] Voice nav path cleared.");
    }

    private bool DrawVoiceNavPath(Vector2 goalTss, bool retryOnly, Dictionary<string, object> imuEva, string label)
    {
        if (_voiceNavContainer == null || minimapRect == null)
        {
            Debug.LogWarning("[ARMinimap] Voice nav path container not ready.");
            return false;
        }

        if (!TryGetEvaTss(imuEva, out Vector2 evaTss))
        {
            if (!retryOnly)
            {
                Debug.Log("[ARMinimap] Voice nav path: waiting for TSS position inside map bounds.");
            }

            return false;
        }

        ClearVoiceNavSegments();

        float tssDistance = Vector2.Distance(evaTss, goalTss);
        if (tssDistance < 2f)
        {
            Debug.Log($"[ARMinimap] Voice nav ({label}): EVA already at destination (TSS distance {tssDistance:F1}m).");
            _voiceNavPathTss.Clear();
            _voiceNavPathActive = false;
            _voiceNavGoal = VoiceNavGoal.None;
            _voiceNavUsedTerrainAstar = false;
            return true;
        }

        bool usedTerrain = true;
        List<Vector2> normPoints = ComputeNormPath(evaTss, goalTss, label, out List<Vector2Int> gridCells);
        if (normPoints == null)
        {
            usedTerrain = false;
            normPoints = new List<Vector2>
            {
                TssCoordsToNormalized(evaTss.x, evaTss.y),
                TssCoordsToNormalized(goalTss.x, goalTss.y)
            };
        }

        CacheVoiceNavPathTss(evaTss, goalTss, gridCells);

        DrawNormPath(_voiceNavContainer, normPoints, voiceNavPathColor, voiceNavPathLineWidth,
                     _voiceNavPoints, _voiceNavSegments);
        EnsureMinimapLayerOrder();

        _voiceNavPathActive = true;
        _voiceNavUsedTerrainAstar = usedTerrain;

        Debug.Log($"[ARMinimap] Voice nav path drawn ({normPoints.Count - 1} segments, " +
                  $"{(usedTerrain ? "terrain A*" : "straight-line fallback")}): " +
                  $"TSS ({evaTss.x:F1}, {evaTss.y:F1}) → {GetWaypointLabel(goalTss)} ({goalTss.x:F1}, {goalTss.y:F1}).");
        return true;
    }

    private string GetWaypointLabel(Vector2 goalTss)
    {
        if (goalTss == ltv1Tss) return "LTV1";
        if (goalTss == ltv2Tss) return "LTV2";
        if (goalTss == baseTss) return "base";
        return "waypoint";
    }

    // Voice path renders above trail but below the player icon.
    private void EnsureMinimapLayerOrder()
    {
        if (minimapRect == null) return;
        if (_voiceNavContainer != null) _voiceNavContainer.SetAsLastSibling();
        if (playerIcon != null) playerIcon.SetAsLastSibling();
    }

    // True when the IMU bucket has coordinates inside (or near) the configured map bounds.
    private bool TryGetEvaTss(Dictionary<string, object> imuEva, out Vector2 tssPos)
    {
        if (imuEva != null)
            tssPos = ImuToNavTss(imuEva);
        else
            tssPos = GetEvaTssPosition();

        const float margin = 100f;
        return tssPos.x >= mapMinX - margin && tssPos.x <= mapMaxX + margin
            && tssPos.y >= mapMinY - margin && tssPos.y <= mapMaxY + margin;
    }

    private void ClearVoiceNavSegments()
    {
        foreach (GameObject seg in _voiceNavSegments) if (seg != null) Destroy(seg);
        _voiceNavSegments.Clear();
        _voiceNavPoints.Clear();
    }

    private void CacheVoiceNavPathTss(Vector2 evaTss, Vector2 goalTss, List<Vector2Int> gridCells)
    {
        _voiceNavPathTss.Clear();

        TerrainAnalyzer terrain = TerrainAnalyzer.Instance;
        if (gridCells != null && gridCells.Count >= 2 && terrain != null)
        {
            foreach (Vector2Int cell in gridCells)
                _voiceNavPathTss.Add(terrain.GridToTssPos(cell));
            return;
        }

        _voiceNavPathTss.Add(evaTss);
        _voiceNavPathTss.Add(goalTss);
        if (verboseDebug)
        {
            Debug.Log(
                $"[ARMinimap] Voice path TSS cache: {_voiceNavPathTss.Count} points " +
                $"({(gridCells != null && gridCells.Count >= 2 ? "A* grid" : "straight 2-point")}) " +
                $"for ground arrow.");
        }
    }

    // Returns terrain-following A* path as normalized minimap positions, or null on failure.
    private List<Vector2> ComputeNormPath(Vector2 startTss, Vector2 goalTss, string label,
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

        RefreshSegmentList(_trailContainer,    _trailLines,       _trailPoints,    trailLineWidth);
        RefreshSegmentList(_voiceNavContainer, _voiceNavSegments, _voiceNavPoints, voiceNavPathLineWidth);
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
        SpawnWaypointDot(baseTss, Color.blue,   "WP_Base");
        SpawnWaypointDot(ltv1Tss, Color.green,  "WP_LTV1");
        SpawnWaypointDot(ltv2Tss, new Color(0.6f, 0.2f, 0.9f, 1f), "WP_LTV2");
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

    // -------------------------------------------------------------------------
    // Debug logging
    // -------------------------------------------------------------------------

    private void LogDebug(Dictionary<string, object> imuEva)
    {
        if (!verboseDebug) return;
        _logTimer += Time.deltaTime;
        if (_logTimer < logIntervalSeconds) return;
        _logTimer = 0f;

        float rawX = imuEva != null ? (float)ToDouble(imuEva, "posx") : 0f;
        float rawY = imuEva != null ? (float)ToDouble(imuEva, "posy") : 0f;
        Vector2 pos   = ImuToNavTss(imuEva);
        float heading = ReadImuHeading();
        Vector2 n     = TssCoordsToNormalized(pos.x, pos.y);
        Vector2 px    = minimapRect != null ? TssCoordsToMapPixels(pos.x, pos.y) : Vector2.zero;

        string bucket = imuEva == null
            ? $"NULL (wrong evaId or TSS not connected)"
            : $"OK [{string.Join(", ", new List<string>(imuEva.Keys))}]";

        string coordLog = imuEva != null
            ? EvaTssCoordinateAdjust.FormatPositionLog(rawX, rawY)
            : "IMU unavailable";

        Debug.Log(
            $"[ARMinimap] imu[\"{evaId}\"]: {bucket}\n" +
            $"  {coordLog}\n" +
            $"  heading {heading:F1}°\n" +
            $"  normalized ({n.x:F3}, {n.y:F3})  anchoredPos ({px.x:F1}, {px.y:F1})\n" +
            $"  trail pts:{_trailPoints.Count}  voice nav segs:{_voiceNavSegments.Count}"
        );
    }

    // -------------------------------------------------------------------------
    // TSS data helpers
    // -------------------------------------------------------------------------

    private float ReadImuHeading()
    {
        if (tssApi != null && tssApi.TryGetImuHeading(evaId, out float heading))
            return heading;

        return 0f;
    }

    /// <summary>
    /// Current rock-yard TSS position for the configured EVA ID (IMU + offset), or zero when unavailable.
    /// </summary>
    public Vector2 GetEvaTssPosition()
    {
        return ImuToNavTss(GetImuBucket());
    }

    /// <summary>Raw IMU posx/posy from TSS, adjusted into rock-yard TSS coordinates.</summary>
    private static Vector2 ImuToNavTss(Dictionary<string, object> imuEva)
    {
        if (imuEva == null) return Vector2.zero;
        return EvaTssCoordinateAdjust.Apply(
            (float)ToDouble(imuEva, "posx"),
            (float)ToDouble(imuEva, "posy"));
    }

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
