using System.Collections.Generic;
using TssApi;
using UnityEngine;

/// <summary>
/// World-space floor compass: a red arrow near the user's feet that points toward the LTV
/// (last known position from TSS) relative to EVA body heading.
/// </summary>
public class LtvGroundArrow : MonoBehaviour
{
    [Header("TSS")]
    [SerializeField] private TssUnityApiService tssApi;
    [Tooltip("Key inside the imu bucket — must match what TSS sends (e.g. eva1)")]
    [SerializeField] private string evaId = "eva1";

    [Header("Placement")]
    [Tooltip("Camera / headset transform. Defaults to Camera.main when unset.")]
    [SerializeField] private Transform followTransform;
    [Tooltip("Meters in front of the follow transform (horizontal plane).")]
    [SerializeField] private float forwardDistance = 0.75f;
    [Tooltip("Meters below the follow transform to approximate floor height.")]
    [SerializeField] private float heightBelowFollow = 1.1f;

    [Header("Orientation")]
    [Tooltip("Mesh that spins toward LTV. Defaults to this object, or the first child MeshRenderer.")]
    [SerializeField] private Transform arrowVisual;
    [Tooltip("When true, EVA IMU heading from TSS. When false, yaw of followTransform.")]
    [SerializeField] private bool useTssHeading = true;
    [Tooltip("Extra Y rotation if the quad mesh / texture forward axis needs tuning.")]
    [SerializeField] private float meshYawOffsetDegrees = 0f;

    [Header("Debug")]
    [SerializeField] private bool useTssData = true;
    [SerializeField] private float debugEvaX;
    [SerializeField] private float debugEvaY;
    [SerializeField] private float debugHeading;
    [SerializeField] private float debugLtvX = -5839.3f;
    [SerializeField] private float debugLtvY = -10460.6f;
    [Tooltip("How often (seconds) to log bearing info. 0 = off.")]
    [SerializeField] private float logIntervalSeconds = 1f;

    private float _logTimer;
    private Transform _pivot;

    private void Awake()
    {
        _pivot = transform;

        if (arrowVisual == null)
        {
            MeshRenderer childMesh = GetComponentInChildren<MeshRenderer>();
            arrowVisual = childMesh != null ? childMesh.transform : transform;
        }

        if (tssApi == null) tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();

        if (tssApi == null)
            Debug.LogError("[LtvGroundArrow] No TssUnityApiService found — assign it in the Inspector.", this);

        if (followTransform == null && Camera.main != null)
            followTransform = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (followTransform == null)
        {
            if (Camera.main != null) followTransform = Camera.main.transform;
            if (followTransform == null) return;
        }

        UpdatePlacement();
        UpdateRotation();
    }

    private void UpdatePlacement()
    {
        Vector3 forward = Vector3.ProjectOnPlane(followTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = followTransform.forward;

        forward.Normalize();
        Vector3 pos = followTransform.position + forward * forwardDistance;
        pos.y = followTransform.position.y - heightBelowFollow;
        _pivot.position = pos;
    }

    private void UpdateRotation()
    {
        float evaX, evaY, heading, ltvX, ltvY;
        if (!TryReadPose(out evaX, out evaY, out heading, out ltvX, out ltvY))
            return;

        if (!useTssHeading)
            heading = followTransform.eulerAngles.y;

        float bearing = BearingDegrees(evaX, evaY, ltvX, ltvY);
        float relative = NormalizeAngle(bearing - heading);
        float yaw = relative + meshYawOffsetDegrees;

        // Quad lies flat on the ground; local +Z (texture "up") is rotated by yaw.
        Quaternion arrowRot = Quaternion.Euler(90f, yaw, 0f);
        if (arrowVisual == _pivot)
            _pivot.rotation = arrowRot;
        else
        {
            _pivot.rotation = Quaternion.identity;
            arrowVisual.rotation = arrowRot;
        }

        _logTimer += Time.deltaTime;
        if (logIntervalSeconds > 0f && _logTimer >= logIntervalSeconds)
        {
            _logTimer = 0f;
            float dist = Mathf.Sqrt((ltvX - evaX) * (ltvX - evaX) + (ltvY - evaY) * (ltvY - evaY));
            Debug.Log(
                $"[LtvGroundArrow] eva=({evaX:F1},{evaY:F1}) heading={heading:F1}°  " +
                $"ltv=({ltvX:F1},{ltvY:F1})  bearing={bearing:F1}°  rel={relative:F1}°  dist={dist:F1}m",
                this);
        }
    }

    private bool TryReadPose(out float evaX, out float evaY, out float heading, out float ltvX, out float ltvY)
    {
        evaX = evaY = heading = ltvX = ltvY = 0f;

        if (!useTssData)
        {
            evaX = debugEvaX;
            evaY = debugEvaY;
            heading = debugHeading;
            ltvX = debugLtvX;
            ltvY = debugLtvY;
            return true;
        }

        if (tssApi == null) return false;

        Dictionary<string, object> imuEva = GetImuEvaBucket();
        if (imuEva == null) return false;

        evaX = (float)ToDouble(imuEva, "posx");
        evaY = (float)ToDouble(imuEva, "posy");
        heading = (float)ToDouble(imuEva, "heading");

        if (!TryReadLtvCoords(out ltvX, out ltvY))
            return false;

        return true;
    }

    private Dictionary<string, object> GetImuEvaBucket()
    {
        Dictionary<string, object> eva = tssApi.GetEva();
        if (eva == null || !eva.TryGetValue("imu", out object imuObj))
            return null;

        var imu = imuObj as Dictionary<string, object>;
        if (imu == null || !imu.TryGetValue(evaId, out object bucketObj))
            return null;

        return bucketObj as Dictionary<string, object>;
    }

    private bool TryReadLtvCoords(out float x, out float y)
    {
        x = y = 0f;
        Dictionary<string, object> location = tssApi.GetLtvLocation();
        if (location == null || location.Count == 0)
        {
            Debug.LogWarning("[LtvGroundArrow] LTV location empty — is TSS online?", this);
            return false;
        }

        if (TryGetCoord(location, "last_known_x", "last_known_y", out x, out y)) return true;
        if (TryGetCoord(location, "actual_x", "actual_y", out x, out y)) return true;
        if (TryGetCoord(location, "posx", "posy", out x, out y)) return true;
        if (TryGetCoord(location, "x", "y", out x, out y)) return true;

        Debug.LogWarning(
            $"[LtvGroundArrow] No known LTV coordinate keys in location. Keys: {Keys(location)}",
            this);
        return false;
    }

    private static bool TryGetCoord(Dictionary<string, object> dict, string keyX, string keyY, out float x, out float y)
    {
        x = y = 0f;
        if (dict == null) return false;
        if (!dict.TryGetValue(keyX, out object rawX) || rawX == null) return false;
        if (!dict.TryGetValue(keyY, out object rawY) || rawY == null) return false;
        x = (float)ToDouble(rawX);
        y = (float)ToDouble(rawY);
        return true;
    }

    /// <summary>
    /// Degrees clockwise from North (TSS +Y) toward East (+X).
    /// </summary>
    private static float BearingDegrees(float fromX, float fromY, float toX, float toY)
    {
        float dx = toX - fromX;
        float dy = toY - fromY;
        return NormalizeAngle(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        if (degrees < -180f) degrees += 360f;
        return degrees;
    }

    private static string Keys(Dictionary<string, object> dict)
    {
        if (dict == null || dict.Count == 0) return "(empty)";
        var k = new List<string>(dict.Keys);
        k.Sort();
        return "[" + string.Join(", ", k) + "]";
    }

    private static double ToDouble(Dictionary<string, object> dict, string key)
    {
        if (dict == null || !dict.TryGetValue(key, out object val) || val == null) return 0d;
        return ToDouble(val);
    }

    private static double ToDouble(object val)
    {
        if (val is double d) return d;
        if (val is float f) return f;
        if (val is int i) return i;
        if (val is long l) return l;
        if (val is string s && double.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double p)) return p;
        try { return System.Convert.ToDouble(val, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return 0d; }
    }
}
