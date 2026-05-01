using UnityEngine;
using UnityEngine.InputSystem;

public class PinchPingSpawner : MonoBehaviour
{
    [SerializeField] private GameObject pingPrefab;
    [SerializeField] private float pinchThreshold = 0.8f;
    [SerializeField] private float pingCooldown = 0.3f;
    [SerializeField] private bool useRightHand = true;

    private InputAction pinchPositionAction;
    private InputAction pinchValueAction;

    private bool wasPinching;
    private float lastPingTime = -999f;

    private void Awake()
    {
        string hand = useRightHand ? "{RightHand}" : "{LeftHand}";

        pinchPositionAction = new InputAction(
            name: "PinchPosition",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{hand}/pinchPose/position"
        );

        pinchValueAction = new InputAction(
            name: "PinchValue",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{hand}/pinchValue"
        );
    }

    private void OnEnable()
    {
        pinchPositionAction.Enable();
        pinchValueAction.Enable();
    }

    private void OnDisable()
    {
        pinchPositionAction.Disable();
        pinchValueAction.Disable();
    }

    private void OnDestroy()
    {
        pinchPositionAction.Dispose();
        pinchValueAction.Dispose();
    }

    private void Update()
    {
        float pinchValue = pinchValueAction.ReadValue<float>();
        Vector3 pinchPosition = pinchPositionAction.ReadValue<Vector3>();

        bool isPinching = pinchValue >= pinchThreshold;

        if (isPinching && !wasPinching && Time.time > lastPingTime + pingCooldown)
        {
            Instantiate(pingPrefab, pinchPosition, Quaternion.identity);
            lastPingTime = Time.time;
        }

        wasPinching = isPinching;
    }
}
