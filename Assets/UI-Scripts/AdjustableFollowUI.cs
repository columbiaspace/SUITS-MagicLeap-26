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

    private void OnEnable()
    {
        // Ensure camera reference is ready (Start may not have run yet on first enable).
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        // VitalsButton positions the panel before SetActive(true), so by the time
        // OnEnable fires the transform is already at the correct front-of-camera
        // position — recalculating the offset here locks it in from that new spot.
        if (continuouslyFollowCamera)
            UpdateOffset();
    }

    private void Update()
    {
        if (!continuouslyFollowCamera) return;
        if (isGrabbed) return;
        if (cameraTransform == null) return;

        // Follow the camera's world position using a fixed world-space offset.
        // Head rotation is intentionally ignored — the panel stays where it was
        // placed in the world and only moves when the user physically moves.
        Vector3 targetPosition = cameraTransform.position + targetOffset;
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime);

        // Always face the user regardless of panel position.
        transform.LookAt(2f * transform.position - cameraTransform.position);
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

        // Store the offset in world space so head rotation doesn't affect panel position.
        targetOffset = transform.position - cameraTransform.position;
    }
}
