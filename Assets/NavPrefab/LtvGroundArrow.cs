using System.Collections.Generic;
using TssApi;
using UnityEngine;

/// <summary>
/// World-space floor arrow near the user's feet. When voice nav is active, points along the
/// cached A* path (tangent to the yellow minimap route). Otherwise can point at a fixed LTV target.
/// </summary>
public class LtvGroundArrow : MonoBehaviour
{
    private enum ArrowMode { Hidden, FollowVoicePath, PointAtLtv }

    [Header("Navigation")]
    [SerializeField] private ARMinimapErica minimap;

    [Header("TSS")]
    [SerializeField] private TssUnityApiService tssApi;
    [Tooltip("Key inside the imu bucket — must match what TSS sends (e.g. eva1)")]
    [SerializeField] private string evaId = "eva1";

    [Header("Voice path")]
    [Tooltip("Meters along the TSS polyline ahead of EVA used to pick the segment bearing.")]
    [SerializeField] private float lookAheadMeters = 2.5f;

    [Header("LTV target (TSS metres) — used when no voice path is active")]
    [Tooltip("Default: LTV Task Board Alpha per NASA SUITS rock-yard coordinates.")]
    [SerializeField] private Vector2 ltvTaskBoardTss = new Vector2(-5635f, -9960f);
    [Tooltip("If enabled, use TSS GetLtvLocation (last_known_x/y) instead of the fixed task board.")]
    [SerializeField] private bool useTssLastKnownLocation;
    [Tooltip("When no voice path, point at LTV instead of hiding the arrow.")]
    [SerializeField] private bool pointAtLtvWhenNoVoicePath;

    [Header("Placement")]
    [Tooltip("Camera / headset transform. Defaults to Camera.main when unset.")]
    [SerializeField] private Transform followTransform;
    [Tooltip("Meters in front of the follow transform (horizontal plane).")]
    [SerializeField] private float forwardDistance = 0.75f;
    [Tooltip("Meters below the follow transform to approximate floor height.")]
    [SerializeField] private float heightBelowFollow = 1.1f;

    [Header("Orientation")]
    [Tooltip("Mesh that spins toward the path / LTV. Defaults to this object, or the first child MeshRenderer.")]
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
    [SerializeField] private float debugLtvX = -5635f;
    [SerializeField] private float debugLtvY = -9960f;
    [Tooltip("How often (seconds) to log bearing info. 0 = off.")]
    [SerializeField] private float logIntervalSeconds = 1f;

    private float _logTimer;
    private Transform _pivot;
    private MeshRenderer _meshRenderer;
    private ArrowMode _mode = ArrowMode.Hidden;
    private ArrowMode _lastAnnouncedMode = (ArrowMode)(-1);
    private bool _loggedMissingMinimap;
    private bool _loggedMissingEvaPose;
    private bool _loggedLookAheadFailure;
    private bool _loggedEmptyVoicePath;

    private void Awake()
    {
        _pivot = transform;

        if (arrowVisual == null)
        {
            MeshRenderer childMesh = GetComponentInChildren<MeshRenderer>();
            arrowVisual = childMesh != null ? childMesh.transform : transform;
        }

        _meshRenderer = arrowVisual.GetComponent<MeshRenderer>();
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (minimap == null) minimap = FindObjectOfType<ARMinimapErica>();
        if (minimap == null && !_loggedMissingMinimap)
        {
            _loggedMissingMinimap = true;
            Debug.LogWarning("[LtvGroundArrow] ARMinimapErica not assigned — voice-path mode needs it.", this);
        }
        else if (minimap != null)
        {
            Debug.Log($"[LtvGroundArrow] Minimap linked ({minimap.name}). Voice path arrow enabled when nav is active.", this);
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

        _mode = ResolveMode();
        AnnounceModeIfChanged();

        if (_mode == ArrowMode.Hidden)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        UpdatePlacement();
        UpdateRotation();
    }

    private void AnnounceModeIfChanged()
    {
        if (_mode == _lastAnnouncedMode) return;
        _lastAnnouncedMode = _mode;
        _loggedLookAheadFailure = false;
        _loggedMissingEvaPose = false;
        _loggedEmptyVoicePath = false;

        switch (_mode)
        {
            case ArrowMode.Hidden:
                Debug.Log(
                    "[LtvGroundArrow] Hidden — no active voice path. " +
                    (pointAtLtvWhenNoVoicePath
                        ? "(Would use PointAtLtv when enabled without voice path.)"
                        : "Enable Point At Ltv When No Voice Path to show LTV compass when idle."),
                    this);
                break;
            case ArrowMode.FollowVoicePath:
                int n = minimap != null ? minimap.VoiceNavPathTss.Count : 0;
                Debug.Log($"[LtvGroundArrow] FollowVoicePath — using cached TSS polyline ({n} points, look-ahead {lookAheadMeters:F1}m).", this);
                break;
            case ArrowMode.PointAtLtv:
                Debug.Log(
                    $"[LtvGroundArrow] PointAtLtv — bearing to LTV " +
                    $"({(useTssLastKnownLocation ? "TSS last known" : $"task board {ltvTaskBoardTss}")}).",
                    this);
                break;
        }
    }

    private ArrowMode ResolveMode()
    {
        if (minimap != null && minimap.VoiceNavPathActive)
        {
            IReadOnlyList<Vector2> path = minimap.VoiceNavPathTss;
            if (path != null && path.Count >= 2)
                return ArrowMode.FollowVoicePath;

            if (!_loggedEmptyVoicePath)
            {
                _loggedEmptyVoicePath = true;
                Debug.LogWarning(
                    $"[LtvGroundArrow] VoiceNavPathActive but TSS path has {path?.Count ?? 0} points — cannot follow route.",
                    this);
            }
        }

        if (pointAtLtvWhenNoVoicePath)
            return ArrowMode.PointAtLtv;

        return ArrowMode.Hidden;
    }

    private void SetVisible(bool visible)
    {
        if (_meshRenderer != null)
            _meshRenderer.enabled = visible;
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
        float evaX, evaY, heading;
        if (!TryReadEvaPose(out evaX, out evaY, out heading))
        {
            if (!_loggedMissingEvaPose)
            {
                _loggedMissingEvaPose = true;
                Debug.LogWarning("[LtvGroundArrow] No EVA pose from TSS — arrow rotation paused.", this);
            }
            return;
        }

        _loggedMissingEvaPose = false;

        if (!useTssHeading)
            heading = followTransform.eulerAngles.y;

        float bearing;
        string bearingSource;
        if (_mode == ArrowMode.FollowVoicePath)
        {
            IReadOnlyList<Vector2> path = minimap.VoiceNavPathTss;
            if (!TryGetLookAheadSegment(path, evaX, evaY, lookAheadMeters, out Vector2 segA, out Vector2 segB))
            {
                if (!_loggedLookAheadFailure)
                {
                    _loggedLookAheadFailure = true;
                    Debug.LogWarning(
                        $"[LtvGroundArrow] Could not pick look-ahead segment on path ({path?.Count ?? 0} pts). " +
                        "Arrow keeps last rotation.",
                        this);
                }
                return;
            }

            _loggedLookAheadFailure = false;
            bearing = BearingDegrees(segA.x, segA.y, segB.x, segB.y);
            bearingSource = $"path seg ({segA.x:F0},{segA.y:F0})→({segB.x:F0},{segB.y:F0})";
        }
        else
        {
            if (!TryReadLtvCoords(out float ltvX, out float ltvY))
                return;

            bearing = BearingDegrees(evaX, evaY, ltvX, ltvY);
            bearingSource = $"LTV ({ltvX:F0},{ltvY:F0})";
        }

        float relative = NormalizeAngle(bearing - heading);
        float yaw = relative + meshYawOffsetDegrees;

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
            string modeLabel = _mode == ArrowMode.FollowVoicePath ? "voice path" : "LTV";
            float distToGoal = 0f;
            if (_mode == ArrowMode.FollowVoicePath && minimap != null && minimap.VoiceNavPathTss.Count > 0)
            {
                Vector2 goal = minimap.VoiceNavPathTss[minimap.VoiceNavPathTss.Count - 1];
                distToGoal = Vector2.Distance(new Vector2(evaX, evaY), goal);
            }

            Debug.Log(
                $"[LtvGroundArrow] mode={modeLabel} eva=({evaX:F1},{evaY:F1}) heading={heading:F1}°  " +
                $"bearing={bearing:F1}° ({bearingSource}) rel={relative:F1}° yaw={yaw:F1}°  " +
                (distToGoal > 0f ? $"distToGoal≈{distToGoal:F1}m" : ""),
                this);
        }
    }

    private bool TryReadEvaPose(out float evaX, out float evaY, out float heading)
    {
        evaX = evaY = heading = 0f;

        if (!useTssData)
        {
            evaX = debugEvaX;
            evaY = debugEvaY;
            heading = debugHeading;
            return true;
        }

        if (tssApi == null) return false;

        Dictionary<string, object> imuEva = GetImuEvaBucket();
        if (imuEva == null) return false;

        evaX = (float)ToDouble(imuEva, "posx");
        evaY = (float)ToDouble(imuEva, "posy");
        heading = (float)ToDouble(imuEva, "heading");
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
        if (!useTssLastKnownLocation)
        {
            x = ltvTaskBoardTss.x;
            y = ltvTaskBoardTss.y;
            return true;
        }

        x = y = 0f;
        if (tssApi == null)
        {
            Debug.LogWarning("[LtvGroundArrow] useTssLastKnownLocation requires TssUnityApiService.", this);
            return false;
        }

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

    /// <summary>
    /// Picks a path segment lookAheadMeters ahead of EVA along the TSS polyline.
    /// </summary>
    private static bool TryGetLookAheadSegment(
        IReadOnlyList<Vector2> path, float evaX, float evaY, float lookAheadMeters,
        out Vector2 segStart, out Vector2 segEnd)
    {
        segStart = segEnd = default;
        int count = path.Count;
        if (count < 2) return false;

        int closestSeg = 0;
        float closestDistSq = float.MaxValue;
        float closestT = 0f;

        for (int i = 0; i < count - 1; i++)
        {
            Vector2 a = path[i];
            Vector2 b = path[i + 1];
            float t = ClosestPointOnSegmentT(evaX, evaY, a.x, a.y, b.x, b.y);
            float px = Mathf.Lerp(a.x, b.x, t);
            float py = Mathf.Lerp(a.y, b.y, t);
            float dx = evaX - px;
            float dy = evaY - py;
            float d2 = dx * dx + dy * dy;
            if (d2 < closestDistSq)
            {
                closestDistSq = d2;
                closestSeg = i;
                closestT = t;
            }
        }

        Vector2 cur = new Vector2(
            Mathf.Lerp(path[closestSeg].x, path[closestSeg + 1].x, closestT),
            Mathf.Lerp(path[closestSeg].y, path[closestSeg + 1].y, closestT));

        float remain = Mathf.Max(lookAheadMeters, 0f);
        int seg = closestSeg;

        while (seg < count - 1)
        {
            Vector2 end = path[seg + 1];
            float legLen = Vector2.Distance(cur, end);
            if (remain <= legLen || seg == count - 2)
            {
                segStart = cur;
                segEnd = end;
                return legLen > 0.01f || seg < count - 1;
            }

            remain -= legLen;
            cur = end;
            seg++;
        }

        segStart = path[count - 2];
        segEnd = path[count - 1];
        return true;
    }

    private static float ClosestPointOnSegmentT(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax;
        float dy = by - ay;
        float lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-8f) return 0f;
        return Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / lenSq);
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
