using System.Collections.Generic;
using TssApi;
using UnityEngine;
using UnityEngine.UI;

public class ARMinimapErica : MonoBehaviour
{
    [Header("TSS")]
    [SerializeField] private TssUnityApiService tssApi;
    [Tooltip("Key inside the imu bucket — must match what TSS sends (e.g. ev1)")]
    [SerializeField] private string evaId = "ev1";

    [Header("Minimap")]
    public RectTransform minimapRect;
    public RectTransform playerIcon;
    public RectTransform pathContainer;
    public GameObject trailDotPrefab;

    [Tooltip("Multiplier from TSS coordinate units to minimap pixels")]
    public float worldToMapScale = 8f;
    [Tooltip("Minimum TSS distance moved before a trail dot is placed")]
    public float recordDistance = 0.25f;

    // Last TSS position used for trail recording
    private Vector2 _lastRecordedTssPos;
    private bool _trailInitialized = false;

    private void Awake()
    {
        if (tssApi == null) tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();

        if (tssApi == null)
            Debug.LogError("[ARMinimap] No TssUnityApiService found — assign it in the Inspector.");
    }

    private void Start()
    {
        // Intentionally not initializing _lastRecordedTssPos here —
        // TSS won't have data yet. It's seeded on the first valid poll in RecordTrail().
    }

    private void Update()
    {
        // Fetch once per frame — GetEva() does a deep copy each call
        Dictionary<string, object> imuEva = GetImuBucket();
        UpdatePlayerIcon(imuEva);
        RecordTrail(imuEva);
    }

    private void UpdatePlayerIcon(Dictionary<string, object> imuEva)
    {
        float x       = (float)ToDouble(imuEva, "posx");
        float y       = (float)ToDouble(imuEva, "posy");
        float heading = (float)ToDouble(imuEva, "heading");

        // Old: Vector3 worldPos = Camera.main.transform.position;
        //      Vector2 mapPos = new Vector2(worldPos.x, worldPos.z) * worldToMapScale;
        Vector2 mapPos = new Vector2(x, y) * worldToMapScale;

        mapPos.x = Mathf.Clamp(mapPos.x, -minimapRect.sizeDelta.x / 2, minimapRect.sizeDelta.x / 2);
        mapPos.y = Mathf.Clamp(mapPos.y, -minimapRect.sizeDelta.y / 2, minimapRect.sizeDelta.y / 2);

        playerIcon.anchoredPosition = mapPos;

        // Old: playerIcon.localEulerAngles = new Vector3(0, 0, -Camera.main.transform.eulerAngles.y);
        playerIcon.localEulerAngles = new Vector3(0f, 0f, -heading);
    }

    private void RecordTrail(Dictionary<string, object> imuEva)
    {
        Vector2 tssPos = imuEva != null
            ? new Vector2((float)ToDouble(imuEva, "posx"), (float)ToDouble(imuEva, "posy"))
            : Vector2.zero;

        // Skip until TSS gives us a real non-zero position
        if (tssPos == Vector2.zero) return;

        // Seed the starting position on the first valid TSS data
        if (!_trailInitialized)
        {
            _lastRecordedTssPos = tssPos;
            _trailInitialized = true;
            return;
        }

        // Old: Vector3 currentPos = Camera.main.transform.position;
        //      if (Vector3.Distance(currentPos, lastRecordedPos) > recordDistance)
        if (Vector2.Distance(tssPos, _lastRecordedTssPos) > recordDistance)
        {
            // Old: Vector2 mapPos = new Vector2(currentPos.x, currentPos.z) * worldToMapScale;
            Vector2 mapPos = tssPos * worldToMapScale;

            GameObject dot = Instantiate(trailDotPrefab, pathContainer);
            dot.GetComponent<RectTransform>().anchoredPosition = mapPos;
            _lastRecordedTssPos = tssPos;
        }
    }

    // Returns the imu.{evaId} bucket from TSS, or null if unavailable.
    private Dictionary<string, object> GetImuBucket()
    {
        if (tssApi == null) return null;

        // GetEva() → ["imu"] → [evaId] → { posx, posy, heading, ... }
        Dictionary<string, object> eva = tssApi.GetEva();

        Dictionary<string, object> imu = null;
        if (eva != null && eva.TryGetValue("imu", out object imuObj))
            imu = imuObj as Dictionary<string, object>;

        Dictionary<string, object> imuEva = null;
        if (imu != null && imu.TryGetValue(evaId, out object bucketObj))
            imuEva = bucketObj as Dictionary<string, object>;

        return imuEva;
    }

    private Vector2 GetTssPosition()
    {
        Dictionary<string, object> imuEva = GetImuBucket();
        if (imuEva == null) return Vector2.zero;
        return new Vector2((float)ToDouble(imuEva, "posx"), (float)ToDouble(imuEva, "posy"));
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
