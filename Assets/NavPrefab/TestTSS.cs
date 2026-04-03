using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;
using UnityEngine.UI;

public class TestTSS : MonoBehaviour
{
    [Header("TSS")]
    [SerializeField] private TssUnityApiService tssApi;

    [Header("EVA")]
    [SerializeField] private string evaId = "eva1";

    [Header("Display")]
    [SerializeField] private Text displayText;
    [SerializeField] private float updateIntervalSeconds = 1f;

    [Header("Debug")]
    [Tooltip("Print a full diagnostic block every poll so you can see exactly where the chain breaks.")]
    [SerializeField] private bool verboseDebug = true;

    private Coroutine _pollRoutine;

    private void Awake()
    {
        if (tssApi == null) tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();

        if (tssApi == null)
            Debug.LogError("[TestTSS] STEP 0 FAIL — No TssUnityApiService found in the scene. Assign it in the Inspector.");
        else
            Debug.Log("[TestTSS] STEP 0 OK — tssApi found: " + tssApi.gameObject.name);
    }

    private void OnEnable()
    {
        if (_pollRoutine == null)
            _pollRoutine = StartCoroutine(PollEvaPoseRoutine());
    }

    private void OnDisable()
    {
        if (_pollRoutine != null)
        {
            StopCoroutine(_pollRoutine);
            _pollRoutine = null;
        }
    }

    private IEnumerator PollEvaPoseRoutine()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.05f, updateIntervalSeconds));
        while (true)
        {
            RefreshDisplay();
            yield return wait;
        }
    }

    private void RefreshDisplay()
    {
        if (tssApi == null) return;

        // ── STEP 1: Is the UDP connection alive? ─────────────────────────────
        // GetHealth() reads TssUnityApiService._sourceOnline, which is set to true
        // only when BOTH the EVA and LTV UDP requests succeeded in the last poll.
        Dictionary<string, object> health = tssApi.GetHealth();
        bool sourceOnline = health != null
                            && health.TryGetValue("source_online", out object onlineObj)
                            && onlineObj is bool b && b;

        // ── STEP 2: GetEva() — the full EVA packet ───────────────────────────
        // TssUnityApiService.GetEva() returns a shallow copy of the _eva cache
        // that was last filled by the UDP poll.  Top-level keys:
        //   status | telemetry | dcu | error | imu | uia
        Dictionary<string, object> eva = tssApi.GetEva();

        // ── STEP 3: Pull the imu section ─────────────────────────────────────
        // eva["imu"] is a dict whose keys are EVA ids sent by the TSS server.
        Dictionary<string, object> imu = null;
        if (eva != null && eva.TryGetValue("imu", out object imuObj))
            imu = imuObj as Dictionary<string, object>;

        // ── STEP 4: Pull this EVA's bucket ───────────────────────────────────
        // The key must exactly match what the TSS server puts in the JSON,
        // "eva1".  Check the imu-keys line below to confirm.
        Dictionary<string, object> imuEva = null;
        if (imu != null && imu.TryGetValue(evaId, out object bucketObj))
            imuEva = bucketObj as Dictionary<string, object>;

        // ── STEP 5: Read the three pose fields ───────────────────────────────
        double posX    = GetDouble(imuEva, "posx");
        double posY    = GetDouble(imuEva, "posy");
        double heading = GetDouble(imuEva, "heading");

        // ── Diagnostic block ─────────────────────────────────────────────────
        if (verboseDebug)
        {
            // Each line tells you which step passed or what's missing.
            // If a step shows (null) or MISSING, everything below it will be 0.
            Debug.Log(
                "[TestTSS] ─────────────────────────────────\n" +
                $"  STEP 1 — source_online      : {(sourceOnline ? "YES ✓" : "NO ✗  (UDP not reaching TSS server)")}\n" +
                $"  STEP 2 — GetEva() keys      : {Keys(eva)}\n" +
                $"  STEP 3 — imu keys           : {Keys(imu)}\n" +
                $"  STEP 4 — imu[\"{evaId}\"] keys : {Keys(imuEva)}\n" +
                $"  STEP 5 — posx               : {Raw(imuEva, "posx")}\n" +
                $"           posy               : {Raw(imuEva, "posy")}\n" +
                $"           heading            : {Raw(imuEva, "heading")}\n" +
                $"  RESULT — posx={posX:F3}  posy={posY:F3}  heading={heading:F2}°"
            );
        }

        // ── Display ──────────────────────────────────────────────────────────
        string line = $"EVA {evaId}: posx={posX:F3}  posy={posY:F3}  heading={heading:F2}°";
        if (displayText != null) displayText.text = line;
        Debug.Log("[TestTSS] " + line);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Shows the raw stored value and its C# type so you know if it needs casting.
    private static string Raw(Dictionary<string, object> dict, string key)
    {
        if (dict == null)                              return "MISSING (bucket is null — check STEP 4 key)";
        if (!dict.TryGetValue(key, out object v))      return $"MISSING (key \"{key}\" not in dict)";
        if (v == null)                                 return "null";
        return $"{v}  ({v.GetType().Name})";
    }

    // Sorted key list — lets you see the exact keys the server is sending.
    private static string Keys(Dictionary<string, object> dict)
    {
        if (dict == null)    return "(null)";
        if (dict.Count == 0) return "(empty)";
        var k = new List<string>(dict.Keys);
        k.Sort();
        return "[" + string.Join(", ", k) + "]";
    }

    // Safe numeric conversion — handles double/float/int/long/string.
    private static double GetDouble(Dictionary<string, object> dict, string key)
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
