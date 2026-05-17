using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PinchPingSpawner : MonoBehaviour
{
    [Header("Ping Prefab (3D)")]
    [SerializeField] private GameObject pingPrefab;

    [Header("Pinch Detection")]
    [SerializeField] private float pinchThreshold = 0.8f;
    [SerializeField] private float pingCooldown   = 0.3f;
    [SerializeField] private bool  useRightHand   = true;

    [Header("Minimap")]
    [Tooltip("Assign the ARMinimapErica component so pings appear on the 2D map.")]
    [SerializeField] private ARMinimapErica minimap;
    [Tooltip("Size of the pink dot on the minimap (pixels).")]
    [SerializeField] private float pinDotSize = 10f;

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

    private InputAction _pinchPositionAction;
    private InputAction _pinchValueAction;
    private bool  _wasPinching;
    private float _lastPingTime = -999f;

    private void Awake()
    {
        string hand = useRightHand ? "{RightHand}" : "{LeftHand}";

        _pinchPositionAction = new InputAction(
            name: "PinchPosition",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{hand}/pinchPose/position"
        );
        _pinchValueAction = new InputAction(
            name: "PinchValue",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{hand}/pinchValue"
        );

        if (minimap == null)
            minimap = FindObjectOfType<ARMinimapErica>();
    }

    private void OnEnable()
    {
        _pinchPositionAction.Enable();
        _pinchValueAction.Enable();
    }

    private void OnDisable()
    {
        _pinchPositionAction.Disable();
        _pinchValueAction.Disable();
    }

    private void OnDestroy()
    {
        _pinchPositionAction.Dispose();
        _pinchValueAction.Dispose();
    }

    private void Update()
    {
        float   pinchValue    = _pinchValueAction.ReadValue<float>();
        Vector3 pinchPosition = _pinchPositionAction.ReadValue<Vector3>();

        bool isPinching = pinchValue >= pinchThreshold;

        if (isPinching && !_wasPinching && Time.time > _lastPingTime + pingCooldown)
        {
            SpawnPing(pinchPosition);
            _lastPingTime = Time.time;
        }

        _wasPinching = isPinching;
    }

    private void SpawnPing(Vector3 pinchWorldPosition)
    {
        Vector3 groundPosition = minimap != null
            ? minimap.SnapToGround(pinchWorldPosition)
            : pinchWorldPosition;

        Vector2 tssPos = Vector2.zero;
        bool haveTss = minimap != null && minimap.TryWorldPositionToTss(pinchWorldPosition, out tssPos);
        if (!haveTss && minimap != null)
        {
            Debug.LogWarning("[PinchPing] Could not map pinch to TSS — is EVA IMU data available?", this);
            tssPos = minimap.GetEvaTssPosition();
        }

        GameObject arPing = null;
        if (pingPrefab != null)
            arPing = Instantiate(pingPrefab, groundPosition, Quaternion.identity);

        GameObject mapDot = null;
        if (minimap != null)
            mapDot = minimap.AddMapPin(tssPos.x, tssPos.y, PingColor, pinDotSize,
                                       $"Ping_{_pings.Count + 1}");

        _pings.Add(new PingRecord
        {
            Index     = _pings.Count + 1,
            TssX      = tssPos.x,
            TssY      = tssPos.y,
            TimeStamp = Time.time,
            ArPing    = arPing,
            MapDot    = mapDot,
        });

        LogPings(groundPosition, haveTss);
    }

    private void LogPings(Vector3 groundPosition, bool mappedFromPinch)
    {
        PingRecord latest = _pings[_pings.Count - 1];
        Vector2 evaTss = minimap != null ? minimap.GetEvaTssPosition() : Vector2.zero;
        float dist = Vector2.Distance(new Vector2(latest.TssX, latest.TssY), evaTss);

        var sb = new System.Text.StringBuilder(
            $"[PinchPing] Ping #{latest.Index}  ground {groundPosition}  " +
            $"TSS ({latest.TssX:F1}, {latest.TssY:F1})  " +
            $"{(mappedFromPinch ? $"~{dist:F1}m from EVA" : "EVA fallback")}\n" +
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
