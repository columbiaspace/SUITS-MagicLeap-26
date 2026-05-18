using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pinch drops a marker at your current EVA position on the minimap (TSS posx/posy).
/// In AR, a 3D ping appears on the ground in front of you as visual feedback that you placed it.
/// </summary>
public class PinchPingSpawner : MonoBehaviour
{
    [Header("Ping Prefab (3D)")]
    [SerializeField] private GameObject pingPrefab;

    [Header("Pinch Detection")]
    [SerializeField] private float pinchThreshold = 0.8f;
    [SerializeField] private float pingCooldown   = 0.3f;
    [SerializeField] private bool  useRightHand   = true;

    [Header("Minimap (TSS)")]
    [Tooltip("Assign the ARMinimapErica component so pings appear on the 2D map at EVA position.")]
    [SerializeField] private ARMinimapErica minimap;
    [Tooltip("Size of the pink dot on the minimap (pixels).")]
    [SerializeField] private float pinDotSize = 10f;

    [Header("AR feedback (ground in front of you)")]
    [Tooltip("Headset / camera used to place the 3D ping. Defaults to Camera.main.")]
    [SerializeField] private Transform followTransform;
    [Tooltip("Metres in front of you on the ground for the 3D ping.")]
    [SerializeField] private float arPinForwardDistance = 0.75f;

    private static readonly Color PingColor = new Color(1f, 0.4f, 0.7f, 1f);

    private struct PingRecord
    {
        public int        Index;
        public float      TssX, TssY;
        public float      TimeStamp;
        public GameObject ArPing;
        public GameObject MapDot;
    }

    private readonly List<PingRecord> _pings = new List<PingRecord>();

    private InputAction _pinchValueAction;
    private bool  _wasPinching;
    private float _lastPingTime = -999f;

    private void Awake()
    {
        string hand = useRightHand ? "{RightHand}" : "{LeftHand}";

        _pinchValueAction = new InputAction(
            name: "PinchValue",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{hand}/pinchValue"
        );

        if (minimap == null)
            minimap = FindObjectOfType<ARMinimapErica>();

        if (followTransform == null && Camera.main != null)
            followTransform = Camera.main.transform;
    }

    private void OnEnable() => _pinchValueAction.Enable();
    private void OnDisable() => _pinchValueAction.Disable();
    private void OnDestroy() => _pinchValueAction.Dispose();

    private void Update()
    {
        bool isPinching = _pinchValueAction.ReadValue<float>() >= pinchThreshold;

        if (isPinching && !_wasPinching && Time.time > _lastPingTime + pingCooldown)
        {
            SpawnPing();
            _lastPingTime = Time.time;
        }

        _wasPinching = isPinching;
    }

    private void SpawnPing()
    {
        if (minimap == null)
        {
            Debug.LogError("[PinchPing] No ARMinimapErica assigned.", this);
            return;
        }

        if (pingPrefab == null)
            Debug.LogWarning("[PinchPing] pingPrefab is not assigned — no 3D AR ping will appear.", this);

        Vector3 arGround = GetArFeedbackGroundPosition();

        Vector2 evaTss = minimap.GetEvaTssPosition();
        bool haveEvaTss = minimap.HasEvaTssPosition();

        GameObject arPing = null;
        if (pingPrefab != null)
            arPing = Instantiate(pingPrefab, arGround, Quaternion.identity);

        GameObject mapDot = null;
        if (haveEvaTss)
        {
            mapDot = minimap.AddMapPin(evaTss.x, evaTss.y, PingColor, pinDotSize,
                $"Ping_{_pings.Count + 1}");
            // Draw above the player icon (AddMapPin defaults to bottom of stack).
            if (mapDot != null)
                mapDot.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogWarning(
                "[PinchPing] No EVA TSS position (posx/posy) — AR ping only, no minimap dot.",
                this);
        }

        _pings.Add(new PingRecord
        {
            Index     = _pings.Count + 1,
            TssX      = evaTss.x,
            TssY      = evaTss.y,
            TimeStamp = Time.time,
            ArPing    = arPing,
            MapDot    = mapDot,
        });

        LogPings(arGround, haveEvaTss);
    }

    private Vector3 GetArFeedbackGroundPosition()
    {
        Transform origin = followTransform;
        if (origin == null && Camera.main != null)
            origin = Camera.main.transform;
        if (origin == null)
            return Vector3.zero;

        Vector3 forward = Vector3.ProjectOnPlane(origin.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = origin.forward;
        forward.Normalize();

        Vector3 ahead = origin.position + forward * arPinForwardDistance;
        return minimap != null ? minimap.SnapToGround(ahead) : ahead;
    }

    private void LogPings(Vector3 arGround, bool haveEvaTss)
    {
        PingRecord latest = _pings[_pings.Count - 1];
        var sb = new System.Text.StringBuilder(
            $"[PinchPing] Ping #{latest.Index}  AR ground {arGround}  " +
            $"map TSS ({latest.TssX:F1}, {latest.TssY:F1}) {(haveEvaTss ? "(EVA)" : "(no TSS)")}\n" +
            $"[PinchPing] All pings ({_pings.Count}):\n");

        foreach (PingRecord p in _pings)
            sb.Append($"  #{p.Index}  TSS ({p.TssX:F1}, {p.TssY:F1})  @ {p.TimeStamp:F1} s\n");

        Debug.Log(sb.ToString(), this);
    }

    public void UndoLastPing()
    {
        if (_pings.Count == 0) return;

        PingRecord last = _pings[_pings.Count - 1];
        if (last.ArPing != null) Destroy(last.ArPing);
        if (last.MapDot != null) Destroy(last.MapDot);
        _pings.RemoveAt(_pings.Count - 1);

        Debug.Log($"[PinchPing] Removed ping #{last.Index}. Remaining: {_pings.Count}", this);
    }

    public void ClearAllPings()
    {
        foreach (PingRecord p in _pings)
        {
            if (p.ArPing != null) Destroy(p.ArPing);
            if (p.MapDot != null) Destroy(p.MapDot);
        }
        _pings.Clear();
        Debug.Log("[PinchPing] All pings cleared.", this);
    }
}
