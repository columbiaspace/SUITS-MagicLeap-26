using UnityEngine;

/// <summary>
/// Desktop testing camera. Attach to any GameObject (e.g. the ML Rig).
/// Creates its own camera and disables the XR camera so nothing fights for control.
/// Remove before building for the headset.
/// </summary>
public class DesktopFlyCamera : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float lookSpeed = 3f;
    [SerializeField] private float sprintMultiplier = 3f;

    private float _yaw;
    private float _pitch = 45f;
    private Transform _rig;

    void Start()
    {
        foreach (Camera c in FindObjectsOfType<Camera>())
            c.gameObject.SetActive(false);

        GameObject camObj = new GameObject("DesktopCamera");
        Camera cam = camObj.AddComponent<Camera>();
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 500f;
        cam.fieldOfView = 70f;
        cam.tag = "MainCamera";

        _rig = camObj.transform;
        _rig.position = new Vector3(0f, 10f, -5f);
        _rig.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        transform.position = _rig.position;
    }

    void Update()
    {
        if (Input.GetMouseButton(1))
        {
            _yaw += Input.GetAxis("Mouse X") * lookSpeed;
            _pitch -= Input.GetAxis("Mouse Y") * lookSpeed;
            _pitch = Mathf.Clamp(_pitch, -90f, 90f);
        }
        _rig.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f);
        Vector3 flat = _rig.forward;
        flat.y = 0f;
        flat.Normalize();
        Vector3 right = _rig.right;
        right.y = 0f;
        right.Normalize();

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += flat;
        if (Input.GetKey(KeyCode.S)) move -= flat;
        if (Input.GetKey(KeyCode.A)) move -= right;
        if (Input.GetKey(KeyCode.D)) move += right;
        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

        _rig.position += move.normalized * speed * Time.deltaTime;

        transform.position = _rig.position;
    }
}
