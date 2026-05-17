using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Click-to-expand minimap with pinch-to-drop pins.
///
/// Attach this script to the minimap surface that the user clicks (typically
/// the <c>MinimapBackground</c> Image, which already has Raycast Target enabled).
///
/// Behaviour:
///   • Clicking the map toggles between the collapsed corner state and an
///     expanded state in front of the user. Position, size, and rotation are
///     interpolated smoothly. While expanded, an optional follow / solver
///     component (e.g. MRTK Follow) is disabled so the panel stays put.
///   • While expanded, pinching (right or left hand) drops a 3D pin prefab
///     at the pinch location, snapped to the map's plane. Pins are only
///     spawned if the projected pinch position falls inside the map rect.
///   • Pins are parented to the minimap rect so they collapse / scale with
///     the map (their world scale is preserved at the moment of parenting).
///
/// Requires the scene's UI raycaster to be wired to the XR ray interactor
/// (the existing Vitals / LTV UI buttons rely on the same setup).
/// </summary>
[DisallowMultipleComponent]
public class MinimapInteraction : MonoBehaviour, IPointerClickHandler
{
    [Header("Minimap targets")]
    [Tooltip("Root world-space minimap RectTransform that will be scaled and moved on expand.")]
    public RectTransform minimapHud;
    [Tooltip("Optional follow / solver component (e.g. MRTK Follow) on the HUD that should be " +
             "disabled while the minimap is expanded so the panel stays in place. Auto-detected " +
             "in Awake() if left empty.")]
    public Behaviour followToDisableWhileExpanded;

    [Header("Animation")]
    [Tooltip("How fast the minimap interpolates toward its target scale / position.")]
    [Range(0.5f, 30f)] public float animationSpeed = 8f;
    [Tooltip("Multiplier applied to the original scale when expanded (1 = no change).")]
    [Range(1f, 6f)] public float expandedScaleMultiplier = 2.8f;
    [Tooltip("Distance in front of the camera the expanded map will float at.")]
    [Range(0.4f, 2f)] public float expandedFrontDistance = 0.85f;
    [Tooltip("Vertical bias applied to the expanded position (positive = above eye level).")]
    [Range(-0.5f, 0.5f)] public float expandedVerticalOffset = -0.05f;

    [Header("Pinch pin")]
    [Tooltip("3D prefab to spawn when the user pinches on top of the expanded map.")]
    public GameObject pingPrefab;
    [Tooltip("True = use the right hand, false = left hand.")]
    public bool useRightHand = true;
    [Tooltip("Pinch closure value at which a pinch is considered active (0..1).")]
    [Range(0.1f, 1f)] public float pinchThreshold = 0.8f;
    [Tooltip("Minimum seconds between consecutive pin drops.")]
    [Range(0f, 2f)] public float pingCooldown = 0.3f;
    [Tooltip("Optional explicit parent for spawned pins. Defaults to the minimap RectTransform " +
             "so pins follow / scale with the map when it collapses.")]
    public Transform pinParent;
    [Tooltip("If true, all spawned pins are destroyed when the minimap collapses.")]
    public bool clearPinsOnCollapse = false;

    /// <summary>True while the minimap is showing (or animating toward) its expanded state.</summary>
    public bool IsExpanded => _isExpanded;

    private bool _isExpanded;
    private Vector3 _collapsedScale = Vector3.one;
    private bool _capturedCollapsedScale;

    private InputAction _pinchPositionAction;
    private InputAction _pinchValueAction;
    private bool _wasPinching;
    private float _lastPingTime = -999f;

    private readonly System.Collections.Generic.List<GameObject> _spawnedPins =
        new System.Collections.Generic.List<GameObject>();

    private void Awake()
    {
        string handToken = useRightHand ? "{RightHand}" : "{LeftHand}";

        _pinchPositionAction = new InputAction(
            name: "MinimapPinchPosition",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{handToken}/pinchPose/position");

        _pinchValueAction = new InputAction(
            name: "MinimapPinchValue",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{handToken}/pinchValue");

        if (followToDisableWhileExpanded == null && minimapHud != null)
        {
            // Best-effort: if no explicit follow assigned, look for one on the HUD root.
            // We avoid hard-typing MRTK so this script has no extra package dependencies.
            foreach (Behaviour behaviour in minimapHud.GetComponents<Behaviour>())
            {
                if (behaviour == null) continue;
                string typeName = behaviour.GetType().Name;
                if (typeName.IndexOf("Follow", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    followToDisableWhileExpanded = behaviour;
                    break;
                }
            }
        }
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
        _pinchPositionAction?.Dispose();
        _pinchValueAction?.Dispose();
    }

    private void Start()
    {
        CaptureCollapsedScale();
    }

    private void CaptureCollapsedScale()
    {
        if (_capturedCollapsedScale || minimapHud == null) return;
        _collapsedScale = minimapHud.localScale;
        _capturedCollapsedScale = true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        ToggleExpand();
    }

    public void ToggleExpand()
    {
        SetExpanded(!_isExpanded);
    }

    public void SetExpanded(bool expanded)
    {
        if (_isExpanded == expanded) return;
        CaptureCollapsedScale();
        _isExpanded = expanded;

        if (followToDisableWhileExpanded != null)
            followToDisableWhileExpanded.enabled = !expanded;

        if (!expanded && clearPinsOnCollapse)
            ClearAllPins();

        // Reset pinch edge detector so an in-progress pinch at the moment of expansion
        // doesn't immediately drop a pin.
        _wasPinching = true;
    }

    private void Update()
    {
        if (minimapHud == null) return;

        AnimateMinimap();
        HandlePinch();
    }

    private void AnimateMinimap()
    {
        Vector3 targetScale = _isExpanded
            ? _collapsedScale * expandedScaleMultiplier
            : _collapsedScale;

        minimapHud.localScale = Vector3.Lerp(
            minimapHud.localScale,
            targetScale,
            Time.deltaTime * animationSpeed);

        if (!_isExpanded) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Transform camT = cam.transform;
        Vector3 targetPos = camT.position
                          + camT.forward * expandedFrontDistance
                          + camT.up * expandedVerticalOffset;
        minimapHud.position = Vector3.Lerp(
            minimapHud.position,
            targetPos,
            Time.deltaTime * animationSpeed);

        Quaternion targetRot = Quaternion.LookRotation(
            minimapHud.position - camT.position,
            camT.up);
        minimapHud.rotation = Quaternion.Slerp(
            minimapHud.rotation,
            targetRot,
            Time.deltaTime * animationSpeed);
    }

    private void HandlePinch()
    {
        if (!_isExpanded)
        {
            _wasPinching = false;
            return;
        }

        float pinchValue = _pinchValueAction.ReadValue<float>();
        Vector3 pinchPosition = _pinchPositionAction.ReadValue<Vector3>();

        bool isPinching = pinchValue >= pinchThreshold;

        if (isPinching && !_wasPinching && Time.time > _lastPingTime + pingCooldown)
        {
            if (TryDropPin(pinchPosition))
                _lastPingTime = Time.time;
        }

        _wasPinching = isPinching;
    }

    private bool TryDropPin(Vector3 pinchWorldPos)
    {
        if (pingPrefab == null || minimapHud == null) return false;

        // Project the pinch onto the minimap canvas plane (canvas normal = transform.forward).
        Plane mapPlane = new Plane(minimapHud.forward, minimapHud.position);
        Vector3 projected = mapPlane.ClosestPointOnPlane(pinchWorldPos);

        // Reject anything outside the map's rect (so pins only land on the map surface).
        Vector3 local = minimapHud.InverseTransformPoint(projected);
        Rect r = minimapHud.rect;
        if (!r.Contains(new Vector2(local.x, local.y))) return false;

        GameObject pin = Instantiate(pingPrefab, projected, Quaternion.identity);

        Transform parent = pinParent != null ? pinParent : minimapHud;
        // worldPositionStays = true preserves the pin's world scale at the moment of
        // parenting; afterwards it scales with the map naturally.
        pin.transform.SetParent(parent, true);

        _spawnedPins.Add(pin);
        return true;
    }

    public void ClearAllPins()
    {
        for (int i = _spawnedPins.Count - 1; i >= 0; i--)
        {
            if (_spawnedPins[i] != null) Destroy(_spawnedPins[i]);
        }
        _spawnedPins.Clear();
    }
}
