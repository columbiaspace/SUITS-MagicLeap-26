using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Companion script for grabbable world-space UI panels.
/// While the user is holding the panel (XRGrabInteractable selected) the
/// XR Interaction Toolkit moves the transform with the hand. After release,
/// the panel stays exactly where it was placed in world space.
///
/// Optionally, you can opt back into the legacy "always follow camera"
/// behaviour by ticking <see cref="continuouslyFollowCamera"/>.
/// </summary>
public class AdjustableFollowUI : MonoBehaviour
{
    [Header("Tracking Setup (legacy)")]
    [Tooltip("Optional camera reference; only used when 'continuouslyFollowCamera' is enabled.")]
    public Transform cameraTransform;
    [Tooltip("Smoothing factor when camera-following is enabled.")]
    public float smoothTime = 0.15f;

    [Header("Behaviour")]
    [Tooltip("When ON, the panel keeps a fixed offset from the camera at all times (old behaviour). " +
             "When OFF (default), grabbing follows the hand and releasing leaves the panel where it was placed.")]
    [SerializeField] private bool continuouslyFollowCamera = false;

    private XRGrabInteractable grabInteractable;
    private bool isGrabbed = false;
    private Vector3 targetOffset;
    private Vector3 velocity = Vector3.zero;

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        grabInteractable = GetComponent<XRGrabInteractable>();
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnGrab);
            grabInteractable.selectExited.AddListener(OnRelease);
        }

        if (continuouslyFollowCamera)
            UpdateOffset();
    }

    private void Update()
    {
        if (!continuouslyFollowCamera) return;
        if (isGrabbed) return;
        if (cameraTransform == null) return;

        Vector3 cameraEuler = cameraTransform.eulerAngles;
        Quaternion yawRotation = Quaternion.Euler(0f, cameraEuler.y, 0f);

        Vector3 targetPosition = cameraTransform.position + (yawRotation * targetOffset);
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime);

        Vector3 lookAtPos = cameraTransform.position;
        lookAtPos.y = transform.position.y;
        transform.LookAt(2f * transform.position - lookAtPos);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        isGrabbed = true;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        isGrabbed = false;
        if (continuouslyFollowCamera)
            UpdateOffset();
    }

    private void UpdateOffset()
    {
        if (cameraTransform == null) return;

        Vector3 cameraEuler = cameraTransform.eulerAngles;
        Quaternion inverseYaw = Quaternion.Inverse(Quaternion.Euler(0f, cameraEuler.y, 0f));
        targetOffset = inverseYaw * (transform.position - cameraTransform.position);
    }
}
