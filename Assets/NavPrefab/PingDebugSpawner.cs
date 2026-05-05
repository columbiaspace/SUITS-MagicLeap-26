using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PingDebugSpawner : MonoBehaviour
{
    [SerializeField] private GameObject pingPrefab;
    [SerializeField] private Transform handTransform;
    [SerializeField] private Vector3 handOffset = new Vector3(0f, 0f, 0f);
    [SerializeField] private float pinchThreshold = 0.99f;
    [SerializeField] private float releaseThreshold = 0.1f;
    [SerializeField] private float requiredPinchHoldTime = 1f;
    [SerializeField] private float spawnCooldown = 3f;
    [SerializeField] private bool destroyPingsAfterLifetime = false;
    [SerializeField] private float pingLifetime = 8f;
    [SerializeField] private float cameraFallbackDistance = 1f;
    [SerializeField] private bool forceCameraPlacement = true;
    [SerializeField] private float maxPinchDistanceFromCamera = 2f;
    [SerializeField] private bool placeOnGazeSurface = true;
    [SerializeField] private float maxGazeRayDistance = 20f;
    [SerializeField] private float surfaceOffset = 0.03f;
    [SerializeField] private LayerMask placementLayers = ~0;
    [SerializeField] private Key clearLastPingKey = Key.Z;
    [SerializeField] private Key clearAllPingsKey = Key.C;
    [SerializeField] private bool enableKeyboardClear = true;
    [SerializeField] private bool spawnTestSphereOnStart = false;
    [SerializeField] private float startupTestDelay = 2f;
    [SerializeField] private float ignoreInputAfterStart = 3f;
    [SerializeField] private bool useRightHand = true;

    private InputAction pinchValueAction;
    private InputAction magicLeapPinchValueAction;
    private InputAction pinchPositionAction;
    private bool wasPinching;
    private bool pingSpawnedForCurrentPinch;
    private bool readyForNewPinch;
    private float pinchStartedTime = -999f;
    private float lastSpawnTime = -999f;
    private float startupTime;
    private bool startupSphereSpawned;
    private readonly List<GameObject> spawnedPings = new List<GameObject>();

    private void Awake()
    {
        string hand = useRightHand ? "{RightHand}" : "{LeftHand}";
        startupTime = Time.time;

        pinchValueAction = new InputAction(
            name: "PinchValue",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{hand}/pinchValue"
        );

        magicLeapPinchValueAction = new InputAction(
            name: "MagicLeapPinchValue",
            type: InputActionType.Value,
            binding: $"<HandInteraction>{hand}/{{PinchValue}}"
        );

        pinchPositionAction = new InputAction(
            name: "PinchPosition",
            type: InputActionType.Value,
            binding: $"<XRHandDevice>{hand}/pinchPosition"
        );
    }

    private void OnEnable()
    {
        pinchValueAction.Enable();
        magicLeapPinchValueAction.Enable();
        pinchPositionAction.Enable();
    }

    private void OnDisable()
    {
        pinchValueAction.Disable();
        magicLeapPinchValueAction.Disable();
        pinchPositionAction.Disable();
    }

    private void OnDestroy()
    {
        pinchValueAction.Dispose();
        magicLeapPinchValueAction.Dispose();
        pinchPositionAction.Dispose();
    }

    private void Update()
    {
        if (spawnTestSphereOnStart &&
            !startupSphereSpawned &&
            Time.time >= startupTime + startupTestDelay)
        {
            SpawnGreenSphere(GetCameraSpawnPosition(), 0.2f);
            startupSphereSpawned = true;
        }

        if (enableKeyboardClear && Keyboard.current != null)
        {
            if (Keyboard.current[clearLastPingKey].wasPressedThisFrame)
            {
                ClearLastPing();
            }

            if (Keyboard.current[clearAllPingsKey].wasPressedThisFrame)
            {
                ClearAllPings();
            }
        }

        float pinchValue = Mathf.Max(
            pinchValueAction.ReadValue<float>(),
            magicLeapPinchValueAction.ReadValue<float>()
        );
        bool isPinching = pinchValue >= pinchThreshold;

        if (Time.time < startupTime + ignoreInputAfterStart)
        {
            return;
        }

        if (pinchValue <= releaseThreshold)
        {
            readyForNewPinch = true;
            wasPinching = false;
            pingSpawnedForCurrentPinch = false;
            pinchStartedTime = -999f;
            return;
        }

        if (!readyForNewPinch)
        {
            return;
        }

        if (isPinching && !wasPinching)
        {
            wasPinching = true;
            pingSpawnedForCurrentPinch = false;
            pinchStartedTime = Time.time;
        }

        if (isPinching &&
            !pingSpawnedForCurrentPinch &&
            Time.time >= pinchStartedTime + requiredPinchHoldTime &&
            Time.time >= lastSpawnTime + spawnCooldown)
        {
            SpawnPingOrSphere(GetHandOrCameraSpawnPosition());
            pingSpawnedForCurrentPinch = true;
            readyForNewPinch = false;
            lastSpawnTime = Time.time;
        }
    }

    private void SpawnPingOrSphere(Vector3 position)
    {
        if (pingPrefab != null)
        {
            GameObject ping = Instantiate(pingPrefab, position, Quaternion.identity);
            ping.transform.localScale = Vector3.one * 0.08f;
            PaintGreen(ping);
            spawnedPings.Add(ping);
            DestroyIfTemporary(ping);
            return;
        }

        SpawnGreenSphere(position, 0.08f);
    }

    private void SpawnGreenSphere(Vector3 position, float size)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "PING_DEBUG_VISIBLE_SPHERE";
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * size;
        PaintGreen(sphere);
        spawnedPings.Add(sphere);
        DestroyIfTemporary(sphere);
    }

    public void ClearLastPing()
    {
        for (int i = spawnedPings.Count - 1; i >= 0; i--)
        {
            GameObject ping = spawnedPings[i];
            spawnedPings.RemoveAt(i);

            if (ping != null)
            {
                Destroy(ping);
                return;
            }
        }
    }

    public void ClearAllPings()
    {
        foreach (GameObject ping in spawnedPings)
        {
            if (ping != null)
            {
                Destroy(ping);
            }
        }

        spawnedPings.Clear();
    }

    private void DestroyIfTemporary(GameObject ping)
    {
        if (destroyPingsAfterLifetime)
        {
            Destroy(ping, pingLifetime);
        }
    }

    private void PaintGreen(GameObject target)
    {
        Renderer renderer = target.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.green;
        }
    }

    private Vector3 GetHandOrCameraSpawnPosition()
    {
        if (forceCameraPlacement)
        {
            return GetGazeSpawnPosition();
        }

        Vector3 pinchPosition = pinchPositionAction.ReadValue<Vector3>();
        if (IsUsableWorldPosition(pinchPosition))
        {
            return pinchPosition;
        }

        if (handTransform != null && IsUsableWorldPosition(handTransform.position))
        {
            return handTransform.position + handTransform.TransformDirection(handOffset);
        }

        handTransform = FindHandTransform();
        if (handTransform != null && IsUsableWorldPosition(handTransform.position))
        {
            return handTransform.position + handTransform.TransformDirection(handOffset);
        }

        return GetCameraSpawnPosition();
    }

    private Transform FindHandTransform()
    {
        string preferredName = useRightHand ? "Right Hand" : "Left Hand";
        string compactName = useRightHand ? "RightHand" : "LeftHand";
        Transform[] transforms = FindObjectsOfType<Transform>();

        foreach (Transform candidate in transforms)
        {
            if (candidate.name == preferredName || candidate.name == compactName)
            {
                return candidate;
            }
        }

        return null;
    }

    private bool IsUsableWorldPosition(Vector3 position)
    {
        if (position == Vector3.zero)
        {
            return false;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindObjectOfType<Camera>();
        }

        if (mainCamera == null)
        {
            return true;
        }

        return Vector3.Distance(mainCamera.transform.position, position) <= maxPinchDistanceFromCamera;
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

    private Vector3 GetGazeSpawnPosition()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindObjectOfType<Camera>();
        }

        if (mainCamera == null)
        {
            return Vector3.forward * cameraFallbackDistance;
        }

        if (placeOnGazeSurface &&
            Physics.Raycast(
                mainCamera.transform.position,
                mainCamera.transform.forward,
                out RaycastHit hit,
                maxGazeRayDistance,
                placementLayers,
                QueryTriggerInteraction.Ignore))
        {
            return hit.point + hit.normal * surfaceOffset;
        }

        return mainCamera.transform.position + mainCamera.transform.forward * cameraFallbackDistance;
    }
}
