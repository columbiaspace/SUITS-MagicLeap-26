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
    public RectTransform pathContainer;
    public GameObject trailDotPrefab;

    [Header("Map Bounds (TSS coordinate ranges the map image covers)")]
    [Tooltip("Leftmost TSS X coordinate shown on the map image")]
    public float mapMinX = -5765f;
    [Tooltip("Rightmost TSS X coordinate shown on the map image")]
    public float mapMaxX = -5545f;
    [Tooltip("Bottom TSS Y coordinate shown on the map image")]
    public float mapMinY = -10075f;
    [Tooltip("Top TSS Y coordinate shown on the map image")]
    public float mapMaxY = -9940f;

    [Tooltip("Minimum TSS distance (meters) moved before a trail dot is placed.")]
    public float recordDistance = 0.25f;

    [Header("Waypoints")]
    [Tooltip("Spawn the three fixed reference waypoints (blue/green/yellow) at Start.")]
    [SerializeField] private bool showWaypoints = true;

    [Header("Debug")]
    [Tooltip("Log pin placement details every second so you can see if TSS data is arriving.")]
    [SerializeField] private bool verboseDebug = true;
    [SerializeField] private float logIntervalSeconds = 1f;

    private Vector2 _lastRecordedTssPos;
    private bool _trailInitialized = false;
    private float _logTimer = 0f;

    private void Awake()
    {
        if (tssApi == null) tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();

        if (tssApi == null)
            Debug.LogError("[ARMinimap] No TssUnityApiService found — assign it in the Inspector.");
    }

    private void Start()
    {
        if (showWaypoints) SpawnWaypoints();
    }

    private void Update()
    {
        Dictionary<string, object> imuEva = GetImuBucket();
        UpdatePlayerIcon(imuEva);
        RecordTrail(imuEva);
        LogDebug(imuEva);
    }

    private void UpdatePlayerIcon(Dictionary<string, object> imuEva)
    {
        if (playerIcon == null || minimapRect == null) return;

        float x       = (float)ToDouble(imuEva, "posx");
        float y       = (float)ToDouble(imuEva, "posy");
        float heading = (float)ToDouble(imuEva, "heading");

        Vector2 mapPos = TssCoordsToMapPixels(x, y);
        playerIcon.anchoredPosition = mapPos;
        playerIcon.localEulerAngles = new Vector3(0f, 0f, -heading);
    }

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

    private void RecordTrail(Dictionary<string, object> imuEva)
    {
        Vector2 tssPos = imuEva != null
            ? new Vector2((float)ToDouble(imuEva, "posx"), (float)ToDouble(imuEva, "posy"))
            : Vector2.zero;

        if (tssPos == Vector2.zero) return;

        if (!_trailInitialized)
        {
            _lastRecordedTssPos = tssPos;
            _trailInitialized = true;
            return;
        }

        if (Vector2.Distance(tssPos, _lastRecordedTssPos) > recordDistance)
        {
            Vector2 mapPos = TssCoordsToMapPixels(tssPos.x, tssPos.y);
            if (trailDotPrefab != null && pathContainer != null)
            {
                GameObject dot = Instantiate(trailDotPrefab, pathContainer);
                dot.GetComponent<RectTransform>().anchoredPosition = mapPos;
            }
            _lastRecordedTssPos = tssPos;
        }
    }

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
        float   nx = n.x;
        float   ny = n.y;

        string bucketStatus = imuEva == null
            ? $"NULL — imu[\"{evaId}\"] not found (wrong evaId or TSS not connected)"
            : $"OK — keys: [{string.Join(", ", new List<string>(imuEva.Keys))}]";

        Debug.Log(
            $"[ARMinimap] ───────────────────────────────\n" +
            $"  imu[\"{evaId}\"] bucket : {bucketStatus}\n" +
            $"  TSS  posx={x:F1}  posy={y:F1}  heading={heading:F1}°\n" +
            $"  normalized  nx={nx:F3} (0=left, 1=right)  ny={ny:F3} (0=bottom, 1=top)\n" +
            $"  map  anchoredPos=({mapPos.x:F1}, {mapPos.y:F1}) px\n" +
            $"  minimap size: {(minimapRect != null ? minimapRect.sizeDelta.ToString() : "null")}\n" +
            $"  bounds X:[{mapMinX},{mapMaxX}]  Y:[{mapMinY},{mapMaxY}]\n" +
            $"  playerIcon assigned: {(playerIcon != null ? "YES" : "NO — assign in Inspector")}"
        );
    }

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
        dot.transform.SetAsFirstSibling();  // render behind player icon and trail

        RectTransform rt    = dot.GetComponent<RectTransform>();
        rt.anchorMin        = norm;
        rt.anchorMax        = norm;
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(8f, 8f);
        rt.anchoredPosition = Vector2.zero;

        dot.GetComponent<Image>().color = color;
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
