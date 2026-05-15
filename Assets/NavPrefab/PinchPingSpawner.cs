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

    // Pink colour used for all ping dots on the map.
    private static readonly Color PingColor = new Color(1f, 0.4f, 0.7f, 1f);

    // -------------------------------------------------------------------------
    // Ping record — tracks every ping that has been fired.
    // -------------------------------------------------------------------------

    private struct PingRecord
    {
        public int        Index;
        public float      TssX, TssY;
        public float      TimeStamp;
        public GameObject MapDot;
    }

    private readonly List<PingRecord> _pings = new List<PingRecord>();

    // -------------------------------------------------------------------------
    // Input actions
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Update
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Ping spawning
    // -------------------------------------------------------------------------

    private void SpawnPing(Vector3 worldPosition)
    {
        // Spawn the 3D ping prefab at the hand position.
        if (pingPrefab != null)
            Instantiate(pingPrefab, worldPosition, Quaternion.identity);

        // Read the current EVA1 TSS position from the minimap service.
        Vector2 tssPos = minimap != null ? minimap.GetEvaTssPosition() : Vector2.zero;

        // Add a pink dot on the 2D minimap.
        GameObject mapDot = null;
        if (minimap != null)
            mapDot = minimap.AddMapPin(tssPos.x, tssPos.y, PingColor, pinDotSize,
                                       $"Ping_{_pings.Count + 1}");

        // Record this ping.
        _pings.Add(new PingRecord
        {
            Index     = _pings.Count + 1,
            TssX      = tssPos.x,
            TssY      = tssPos.y,
            TimeStamp = Time.time,
            MapDot    = mapDot,
        });

        LogPings();
    }

    // -------------------------------------------------------------------------
    // Logging
    // -------------------------------------------------------------------------

    private void LogPings()
    {
        PingRecord latest = _pings[_pings.Count - 1];
        var sb = new System.Text.StringBuilder(
            $"[PinchPing] Ping #{latest.Index} at TSS ({latest.TssX:F1}, {latest.TssY:F1})\n" +
            $"[PinchPing] All pings ({_pings.Count}):\n");

        foreach (PingRecord p in _pings)
            sb.Append($"  #{p.Index}  TSS ({p.TssX:F1}, {p.TssY:F1})  @ {p.TimeStamp:F1} s\n");

        Debug.Log(sb.ToString());
    }

    // -------------------------------------------------------------------------
    // Public helpers
    // -------------------------------------------------------------------------

    /// <summary>Removes the most recent ping from the map and the log.</summary>
    public void UndoLastPing()
    {
        if (_pings.Count == 0) return;

        PingRecord last = _pings[_pings.Count - 1];
        if (last.MapDot != null) Destroy(last.MapDot);
        _pings.RemoveAt(_pings.Count - 1);

        Debug.Log($"[PinchPing] Removed ping #{last.Index}. Remaining: {_pings.Count}");
    }

    /// <summary>Removes all pings from the map and clears the log.</summary>
    public void ClearAllPings()
    {
        foreach (PingRecord p in _pings)
            if (p.MapDot != null) Destroy(p.MapDot);
        _pings.Clear();
        Debug.Log("[PinchPing] All pings cleared.");
    }
}
