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
//  We multiply by worldToMapScale to convert meters → pixels on the minimap.
//
//  Example: posx=10, worldToMapScale=8 → anchoredPosition.x = 80 px from center.
//
//  If the pin is stuck at center: TSS posx/posy are 0 (check STEP debug output).
//  If the pin flies off the edge: worldToMapScale is too large — reduce it.
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

    // Converts absolute TSS coords to anchoredPosition pixels on the minimap.
    //
    // How it works:
    //   1. Normalize: (tssX - mapMinX) / (mapMaxX - mapMinX) → 0..1 across the map width
    //   2. Shift to centered: subtract 0.5 → -0.5..+0.5
    //   3. Scale to pixels: multiply by minimap pixel width
    //
    // Result: left edge of map → -halfWidth px, right edge → +halfWidth px, center → 0 px
    private Vector2 TssCoordsToMapPixels(float tssX, float tssY)
    {
        float halfW = minimapRect.sizeDelta.x / 2f;
        float halfH = minimapRect.sizeDelta.y / 2f;

        float nx = (tssX - mapMinX) / (mapMaxX - mapMinX);  // 0..1
        float ny = (tssY - mapMinY) / (mapMaxY - mapMinY);  // 0..1

        float px = (nx - 0.5f) * minimapRect.sizeDelta.x;
        float py = (ny - 0.5f) * minimapRect.sizeDelta.y;

        return new Vector2(
            Mathf.Clamp(px, -halfW, halfW),
            Mathf.Clamp(py, -halfH, halfH)
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
        float nx = (x - mapMinX) / (mapMaxX - mapMinX);
        float ny = (y - mapMinY) / (mapMaxY - mapMinY);

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
