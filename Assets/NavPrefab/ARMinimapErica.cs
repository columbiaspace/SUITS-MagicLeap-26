using System.Collections.Generic;
using TssApi;
using UnityEngine;
using UnityEngine.UI;

// How pin placement works:
//
//  The minimap image is a RectTransform (minimapRect) centered at (0,0) in UI space.
//  anchoredPosition (0,0) = center of the image.
//  anchoredPosition (-halfWidth, -halfHeight) = bottom-left corner.
//
//  TSS gives posx/posy in meters (real-world coords).
//  Map bounds (mapMinX…mapMaxX, mapMinY…mapMaxY) define which TSS region the image covers.
//  TssCoordsToNormalized maps those coords to 0..1, then TssCoordsToMapPixels scales to UI pixels.
//
//  If the pin is stuck at center: TSS posx/posy are 0 (check STEP debug output).
//  If the pin moves in the wrong direction: TSS axes may differ from UI axes —
//    try negating x or y in the mapPos assignment below.

public class ARMinimapErica : MonoBehaviour
{
    [Header("TSS")]
    [SerializeField] private TssUnityApiService tssApi;
    [Tooltip("Key inside the imu bucket — must match what TSS sends")]
    [SerializeField] private string evaId = "eva1";

    [Header("Minimap")]
    public RectTransform minimapRect;
    public RectTransform playerIcon;
    public RectTransform pathContainer;   // kept for external callers; not used by trail
    public GameObject    trailDotPrefab;  // kept for backward compat; not used by trail

    [Header("Map Bounds (TSS coordinate ranges the map image covers)")]
    [Tooltip("Leftmost TSS X coordinate shown on the map image")]
    public float mapMinX = -5765f;
    [Tooltip("Rightmost TSS X coordinate shown on the map image")]
    public float mapMaxX = -5545f;
    [Tooltip("Bottom TSS Y coordinate shown on the map image")]
    public float mapMinY = -10075f;
    [Tooltip("Top TSS Y coordinate shown on the map image")]
    public float mapMaxY = -9940f;

    [Tooltip("Minimum TSS distance (meters) the EVA must travel before a new trail point is recorded.")]
    public float recordDistance = 0.5f;

    [Header("Trail")]
    [Tooltip("Color of the traveled-path line.")]
    public Color trailColor = new Color(1f, 0.55f, 0f, 1f);
    [Tooltip("Width of the trail line in UI pixels.")]
    public float trailLineWidth = 2.5f;
    [Tooltip("Maximum number of trail points kept; oldest are pruned when exceeded.")]
    public int maxTrailPoints = 500;

    [Header("Waypoints")]
    [Tooltip("Spawn the three fixed reference waypoints (blue/green/yellow) at Start.")]
    [SerializeField] private bool showWaypoints = true;

    [Header("Debug")]
    [Tooltip("Log pin placement details every second so you can see if TSS data is arriving.")]
    [SerializeField] private bool verboseDebug = true;
    [SerializeField] private float logIntervalSeconds = 1f;

    // Trail state — normalized (0..1) positions and their connecting segment objects
    private readonly List<Vector2>    _trailPoints   = new List<Vector2>();
    private readonly List<GameObject> _trailLines    = new List<GameObject>();
    private RectTransform _trailContainer;
    private Vector2       _lastMinimapSize;

    private Vector2 _lastRecordedTssPos;
    private bool    _trailInitialized = false;
    private float   _logTimer         = 0f;

    private void Awake()
    {
        if (tssApi == null) tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();

        if (tssApi == null)
            Debug.LogError("[ARMinimap] No TssUnityApiService found — assign it in the Inspector.");
    }

    private void Start()
    {
        CreateTrailContainer();
        if (showWaypoints) SpawnWaypoints();

        // Trail renders above waypoints but below the player icon
        if (_trailContainer != null && playerIcon != null)
            _trailContainer.SetSiblingIndex(playerIcon.GetSiblingIndex());
    }

    private void Update()
    {
        Dictionary<string, object> imuEva = GetImuBucket();
        UpdatePlayerIcon(imuEva);
        RecordTrail(imuEva);
        RefreshTrailIfResized();
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

    // Maps TSS coords to 0..1 fractions within the configured map bounds.
    // (0,0) = bottom-left corner of the map image, (1,1) = top-right corner.
    private Vector2 TssCoordsToNormalized(float tssX, float tssY)
    {
        return new Vector2(
            Mathf.Clamp01((tssX - mapMinX) / (mapMaxX - mapMinX)),
            Mathf.Clamp01((tssY - mapMinY) / (mapMaxY - mapMinY))
        );
    }

    // Converts absolute TSS coords to anchoredPosition pixels on the minimap.
    // Left edge → -halfWidth px, right edge → +halfWidth px, center → 0 px.
    private Vector2 TssCoordsToMapPixels(float tssX, float tssY)
    {
        Vector2 n = TssCoordsToNormalized(tssX, tssY);
        return new Vector2(
            (n.x - 0.5f) * minimapRect.sizeDelta.x,
            (n.y - 0.5f) * minimapRect.sizeDelta.y
        );
    }

    // -------------------------------------------------------------------------
    // Trail recording
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

    // -------------------------------------------------------------------------
    // Trail container + line-segment management
    // -------------------------------------------------------------------------

    private void CreateTrailContainer()
    {
        if (minimapRect == null) return;

        GameObject go = new GameObject("TrailContainer", typeof(RectTransform));
        go.transform.SetParent(minimapRect, false);

        // Stretch to fill minimapRect exactly; no scale modification by MinimapExpandZoom
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);

        _trailContainer  = rt;
        _lastMinimapSize = minimapRect.rect.size;
    }

    // Adds a new normalized position to the trail and draws a line segment from the previous point.
    private void AddTrailPoint(Vector2 norm)
    {
        if (_trailContainer == null) return;

        if (_trailPoints.Count > 0)
            _trailLines.Add(CreateLineSegment(_trailPoints[_trailPoints.Count - 1], norm));

        _trailPoints.Add(norm);

        // Prune oldest point + its outgoing segment when the trail exceeds the limit
        while (_trailPoints.Count > maxTrailPoints)
        {
            _trailPoints.RemoveAt(0);
            if (_trailLines.Count > 0)
            {
                Destroy(_trailLines[0]);
                _trailLines.RemoveAt(0);
            }
        }
    }

    // Creates a thin Image rect that visually connects normA to normB on the minimap.
    private GameObject CreateLineSegment(Vector2 normA, Vector2 normB)
    {
        GameObject seg = new GameObject("TrailSeg",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        seg.transform.SetParent(_trailContainer, false);

        UpdateSegmentGeometry(seg.GetComponent<RectTransform>(), normA, normB);
        seg.GetComponent<Image>().color = trailColor;
        return seg;
    }

    // Positions, sizes, and rotates a line-segment rect to span from normA to normB.
    // Uses fractional anchors for the midpoint so the segment moves with the rect,
    // and recomputes the pixel length from the current rect dimensions.
    private void UpdateSegmentGeometry(RectTransform rt, Vector2 normA, Vector2 normB)
    {
        // Anchor the midpoint so the segment stays proportionally positioned on resize
        Vector2 mid  = (normA + normB) * 0.5f;
        rt.anchorMin = mid;
        rt.anchorMax = mid;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        // Pixel length of this segment at the current rect dimensions
        float w   = _trailContainer.rect.width;
        float h   = _trailContainer.rect.height;
        float dx  = (normB.x - normA.x) * w;
        float dy  = (normB.y - normA.y) * h;
        float len = Mathf.Max(Mathf.Sqrt(dx * dx + dy * dy), 1f);

        rt.sizeDelta        = new Vector2(len, trailLineWidth);
        rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
    }

    // Recomputes segment geometries whenever the minimap rect has been resized
    // (e.g., during the expand/collapse animation driven by MinimapExpandZoom).
    private void RefreshTrailIfResized()
    {
        if (minimapRect == null) return;
        Vector2 curSize = minimapRect.rect.size;
        if (Vector2.Distance(curSize, _lastMinimapSize) > 0.5f)
        {
            for (int i = 0; i < _trailLines.Count; i++)
            {
                if (_trailLines[i] == null) continue;
                RectTransform rt = _trailLines[i].GetComponent<RectTransform>();
                if (rt != null)
                    UpdateSegmentGeometry(rt, _trailPoints[i], _trailPoints[i + 1]);
            }
            _lastMinimapSize = curSize;
        }
    }

    // Removes all trail segments and resets the trail state.
    public void ClearTrail()
    {
        foreach (GameObject seg in _trailLines)
            if (seg != null) Destroy(seg);

        _trailLines.Clear();
        _trailPoints.Clear();
        _trailInitialized = false;
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

    // Places a coloured dot at the given TSS position, parented directly to minimapRect
    // using fractional anchors so the position stays correct when the rect is resized.
    private void SpawnWaypointDot(Vector2 tssPos, Color color, string dotName)
    {
        Vector2 norm = TssCoordsToNormalized(tssPos.x, tssPos.y);

        GameObject dot = new GameObject(dotName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dot.transform.SetParent(minimapRect, false);
        dot.transform.SetAsFirstSibling();  // render beneath trail and player icon

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
        Vector2 mapPos = minimapRect != null ? TssCoordsToMapPixels(x, y) : Vector2.zero;
        Vector2 n  = TssCoordsToNormalized(x, y);

        string bucketStatus = imuEva == null
            ? $"NULL — imu[\"{evaId}\"] not found (wrong evaId or TSS not connected)"
            : $"OK — keys: [{string.Join(", ", new List<string>(imuEva.Keys))}]";

        Debug.Log(
            $"[ARMinimap] ───────────────────────────────\n" +
            $"  imu[\"{evaId}\"] bucket : {bucketStatus}\n" +
            $"  TSS  posx={x:F1}  posy={y:F1}  heading={heading:F1}°\n" +
            $"  normalized  nx={n.x:F3} (0=left, 1=right)  ny={n.y:F3} (0=bottom, 1=top)\n" +
            $"  map  anchoredPos=({mapPos.x:F1}, {mapPos.y:F1}) px\n" +
            $"  minimap size: {(minimapRect != null ? minimapRect.sizeDelta.ToString() : "null")}\n" +
            $"  bounds X:[{mapMinX},{mapMaxX}]  Y:[{mapMinY},{mapMaxY}]\n" +
            $"  trail points: {_trailPoints.Count}  segments: {_trailLines.Count}\n" +
            $"  playerIcon assigned: {(playerIcon != null ? "YES" : "NO — assign in Inspector")}"
        );
    }

    // -------------------------------------------------------------------------
    // TSS data helpers
    // -------------------------------------------------------------------------

    private Dictionary<string, object> GetImuBucket()
    {
        if (tssApi == null) return null;

        Dictionary<string, object> eva = tssApi.GetEva();

        Dictionary<string, object> imu = null;
        if (eva != null && eva.TryGetValue("imu", out object imuObj))
            imu = imuObj as Dictionary<string, object>;

        Dictionary<string, object> imuEva = null;
        if (imu != null && imu.TryGetValue(evaId, out object bucketObj))
            imuEva = bucketObj as Dictionary<string, object>;

        return imuEva;
    }

    private static double ToDouble(Dictionary<string, object> dict, string key)
    {
        if (dict == null || !dict.TryGetValue(key, out object val) || val == null) return 0d;
        if (val is double d)  return d;
        if (val is float  f)  return f;
        if (val is int    i)  return i;
        if (val is long   l)  return l;
        if (val is string s && double.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double p)) return p;
        try   { return System.Convert.ToDouble(val, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0d; }
    }
}
