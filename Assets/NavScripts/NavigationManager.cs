using UnityEngine;

public class NavigationManager : MonoBehaviour
{
    // Constants
    private const float GROUND_Y = 0f;
    private const string UNLIT_SHADER = "Unlit/Color";
    private const int LINE_POINT_COUNT = 2;

    // Pin geometry (meters)
    private const float STICK_DIAMETER = 0.02f;
    private const float STICK_HALF_HEIGHT = 0.15f;
    private const float STICK_Y_OFFSET = 0.15f;
    private const float HEAD_DIAMETER = 0.08f;
    private const float HEAD_Y_OFFSET = 0.34f;

    // Inspector Fields
    [Header("References")]
    [SerializeField] private Transform xrOrigin;

    [Header("Pin Settings")]
    [SerializeField] private Vector3 pinPosition = new Vector3(3f, 0f, 3f);
    [SerializeField] private Color pinColor = Color.red;

    [Header("Path Settings")]
    [SerializeField] private float lineWidth = 0.05f;
    [SerializeField] private Color pathColor = new Color(1f, 0f, 0.61f, 1f);

    // Private State
    private LineRenderer lineRenderer;
    private GameObject pinMarker;

    // Lifecycle

    void Start()
    {
        CreatePinMarker();
        SetupLineRenderer();
    }

    void Update()
    {
        if (!IsReady()) return;

        UpdatePathPositions();
    }

    // Readiness Check

    private bool IsReady()
    {
        return xrOrigin != null && lineRenderer != null;
    }

    // Path Updates

    private void UpdatePathPositions()
    {
        Vector3 startPos = GetUserGroundPosition();
        Vector3 endPos = GetPinGroundPosition();

        SetLineEndpoints(startPos, endPos);
    }

    private Vector3 GetUserGroundPosition()
    {
        return ProjectToGround(xrOrigin.position);
    }

    private Vector3 GetPinGroundPosition()
    {
        return ProjectToGround(pinPosition);
    }

    private Vector3 ProjectToGround(Vector3 position)
    {
        return new Vector3(position.x, GROUND_Y, position.z);
    }

    private void SetLineEndpoints(Vector3 start, Vector3 end)
    {
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }

    // Pin Construction

    private void CreatePinMarker()
    {
        pinMarker = CreateEmptyObject("PinMarker", pinPosition);

        Material pinMat = CreateUnlitMaterial(pinColor);

        AttachStick(pinMarker.transform, pinMat);
        AttachHead(pinMarker.transform, pinMat);
    }

    private void AttachStick(Transform parent, Material mat)
    {
        Vector3 scale = new Vector3(STICK_DIAMETER, STICK_HALF_HEIGHT, STICK_DIAMETER);
        Vector3 offset = new Vector3(0f, STICK_Y_OFFSET, 0f);

        CreatePrimitivePart("PinStick", PrimitiveType.Cylinder, parent, scale, offset, mat);
    }

    private void AttachHead(Transform parent, Material mat)
    {
        Vector3 scale = new Vector3(HEAD_DIAMETER, HEAD_DIAMETER, HEAD_DIAMETER);
        Vector3 offset = new Vector3(0f, HEAD_Y_OFFSET, 0f);

        CreatePrimitivePart("PinHead", PrimitiveType.Sphere, parent, scale, offset, mat);
    }

    private void CreatePrimitivePart(
        string name, PrimitiveType type, Transform parent,
        Vector3 localScale, Vector3 localPosition, Material mat)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localScale = localScale;
        part.transform.localPosition = localPosition;
        part.GetComponent<Renderer>().material = mat;
        RemoveCollider(part);
    }

    // Line Renderer Setup

    private void SetupLineRenderer()
    {
        lineRenderer = gameObject.AddComponent<LineRenderer>();

        lineRenderer.material = CreateUnlitMaterial(pathColor);
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = LINE_POINT_COUNT;
        lineRenderer.useWorldSpace = true;

        // Initialize both points at the pin so there's no flash on frame 1
        Vector3 pinGround = GetPinGroundPosition();
        SetLineEndpoints(pinGround, pinGround);
    }

    // Utilities 

    private GameObject CreateEmptyObject(string name, Vector3 position)
    {
        GameObject obj = new GameObject(name);
        obj.transform.position = position;
        return obj;
    }

    private Material CreateUnlitMaterial(Color color)
    {
        Material mat = new Material(Shader.Find(UNLIT_SHADER));
        mat.color = color;
        return mat;
    }

    private void RemoveCollider(GameObject obj)
    {
        Collider col = obj.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }
}
