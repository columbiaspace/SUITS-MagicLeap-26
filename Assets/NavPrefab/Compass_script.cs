using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;

public class Compass_script : MonoBehaviour
{
    [Header("TSS")]
    [SerializeField] private TssUnityApiService tssApi;
    [Tooltip("Key inside the imu bucket — must match what TSS sends (e.g. ev1)")]
    [SerializeField] private string evaId = "eva1";

    [Header("Compass")]
    public RectTransform NorthArrow;

    [Header("Debug")]
    [Tooltip("When true, shows live TSS heading. Disable to use the override below.")]
    [SerializeField] private bool useTssHeading = true;
    [SerializeField] private float debugHeading = 0f;
    [Tooltip("How often (seconds) to print the heading to the Console. 0 = every frame.")]
    [SerializeField] private float logIntervalSeconds = 1f;

    private float _currentHeading = 0f;
    private float _logTimer = 0f;

    private void Awake()
    {
        // Always prefer the persistent singleton — see ARMinimapErica.Awake comment.
        if (TssUnityApiService.Instance != null) tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();

        if (tssApi == null)
            Debug.LogError("[Compass] No TssUnityApiService found — assign it in the Inspector.");
    }

    private void Update()
    {
        if (useTssHeading && tssApi != null)
            _currentHeading = ReadTssHeading();
        else
            _currentHeading = debugHeading;

        // TSS heading: 0 = North, 90 = East, 180 = South, 270 = West.
        // Negate so the arrow rotates opposite to the heading
        // (arrow points toward north as the player turns away from it).
        if (NorthArrow != null)
            NorthArrow.localEulerAngles = new Vector3(0f, 0f, -_currentHeading);

        _logTimer += Time.deltaTime;
        if (_logTimer >= logIntervalSeconds)
        {
            _logTimer = 0f;
            Debug.Log($"[Compass] heading={_currentHeading:F1}°  arrow_z={-_currentHeading:F1}°  source={(useTssHeading ? "TSS" : "debug")}");
        }
    }

    private float ReadTssHeading()
    {
        // GetEva() → ["imu"] → [evaId] → ["heading"]
        Dictionary<string, object> eva = tssApi.GetEva();

        Dictionary<string, object> imu = null;
        if (eva != null && eva.TryGetValue("imu", out object imuObj))
            imu = imuObj as Dictionary<string, object>;

        Dictionary<string, object> imuEva = null;
        if (imu != null && imu.TryGetValue(evaId, out object bucketObj))
            imuEva = bucketObj as Dictionary<string, object>;

        if (imuEva == null)
        {
            Debug.LogWarning($"[Compass] imu[\"{evaId}\"] not found — check evaId matches TSS. imu keys: {Keys(imu)}");
            return _currentHeading; // hold last known value
        }

        if (!imuEva.TryGetValue("heading", out object raw) || raw == null)
        {
            Debug.LogWarning("[Compass] heading field missing from imu bucket.");
            return _currentHeading;
        }

        return (float)ToDouble(raw);
    }

    private static string Keys(Dictionary<string, object> dict)
    {
        if (dict == null || dict.Count == 0) return "(empty)";
        var k = new List<string>(dict.Keys);
        k.Sort();
        return "[" + string.Join(", ", k) + "]";
    }

    private static double ToDouble(object val)
    {
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
