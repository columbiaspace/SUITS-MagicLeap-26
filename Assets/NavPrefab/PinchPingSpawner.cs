using UnityEngine;
using UnityEngine.InputSystem;

public class PinchPingSpawner : MonoBehaviour
{
    [SerializeField] private GameObject pingPrefab;
    [SerializeField] private Transform handTransform;
    [SerializeField] private Vector3 handOffset = new Vector3(0f, 0f, 0.05f);
    [SerializeField] private float pinchThreshold = 0.8f;
    [SerializeField] private float releaseThreshold = 0.35f;
    [SerializeField] private float pingCooldown = 0.3f;
    [SerializeField] private float pingLifetime = 8f;
    [SerializeField] private float cameraFallbackDistance = 1f;
    [SerializeField] private bool spawnTestPingOnStart = true;
    [SerializeField] private float startupTestDelay = 2f;
    [SerializeField] private bool useRightHand = true;

    private InputAction pinchValueAction;
    private bool wasPinching;
    private float lastPingTime = -999f;
    private float startupTime;
    private bool startupTestPingSpawned;

    private void Awake()
    {
        string hand = useRightHand ? "{RightHand}" : "{LeftHand}";
        startupTime = Time.time;

        // This is the same XRI/HandInteraction pinch binding that already spawned pings before.
        pinchValueAction = new InputAction(
            name: "PinchValue",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{hand}/pinchValue"
        );
    }

    private void OnEnable()
    {
        pinchValueAction.Enable();
    }

    private void OnDisable()
    {
        pinchValueAction.Disable();
    }

    private void OnDestroy()
    {
        pinchValueAction.Dispose();
    }

    private void Update()
    {
        if (spawnTestPingOnStart &&
            !startupTestPingSpawned &&
            Time.time >= startupTime + startupTestDelay)
        {
            SpawnVisibleDebugSphere(GetCameraSpawnPosition());
            startupTestPingSpawned = true;
        }

        if (pingPrefab == null)
        {
            return;
        }

        float pinchValue = pinchValueAction.ReadValue<float>();
        bool isPinching = pinchValue >= pinchThreshold;

        if (isPinching && !wasPinching && Time.time > lastPingTime + pingCooldown)
        {
            SpawnPing(GetSpawnPosition());

            wasPinching = true;
            lastPingTime = Time.time;
        }

        if (pinchValue <= releaseThreshold)
        {
            wasPinching = false;
        }
    }

    private void SpawnPing(Vector3 position)
    {
        GameObject ping = Instantiate(pingPrefab, position, Quaternion.identity);
        ping.transform.localScale = Vector3.one * 0.15f;

        Renderer renderer = ping.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.green;
        }

        Destroy(ping, pingLifetime);
    }

    private void SpawnVisibleDebugSphere(Vector3 position)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "PING_SCRIPT_IS_RUNNING_TEST_SPHERE";
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * 0.2f;

        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.green;
        }

        Destroy(sphere, pingLifetime);
    }

    private Vector3 GetSpawnPosition()
    {
        if (handTransform != null)
        {
            return handTransform.position + handTransform.TransformDirection(handOffset);
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            return GetCameraSpawnPosition();
        }

        return Vector3.forward * cameraFallbackDistance;
    }

    private Vector3 GetCameraSpawnPosition()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindObjectOfType<Camera>();
        }

        if (mainCamera != null)
        {
            return mainCamera.transform.position + mainCamera.transform.forward * cameraFallbackDistance;
        }

        return Vector3.forward * cameraFallbackDistance;
    }
}
