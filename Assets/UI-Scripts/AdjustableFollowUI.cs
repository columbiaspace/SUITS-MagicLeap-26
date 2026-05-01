using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class AdjustableFollowUI : MonoBehaviour
{
    [Header("Tracking Setup")]
    public Transform cameraTransform;
    public float smoothTime = 0.15f;
    
    private XRGrabInteractable grabInteractable;
    private bool isGrabbed = false;
    private Vector3 targetOffset;
    private Vector3 velocity = Vector3.zero;

    void Start()
    {
        // Find the main camera if one isn't assigned
        if (cameraTransform == null) cameraTransform = Camera.main.transform;
        
        // Hook into the XR Grab events
        grabInteractable = GetComponent<XRGrabInteractable>();
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnGrab);
            grabInteractable.selectExited.AddListener(OnRelease);
        }

        // Calculate the initial offset so the menu starts right where you placed it in the scene
        UpdateOffset();
    }

    void Update()
    {
        // If the user is holding the menu, let XRI handle the movement. Do nothing here.
        if (isGrabbed) return; 

        // 1. Calculate the user's "Yaw" (side-to-side turning)
        // We ignore X and Z rotation so the menu doesn't tilt weirdly if the user looks up or down.
        Vector3 cameraEuler = cameraTransform.eulerAngles;
        Quaternion yawRotation = Quaternion.Euler(0, cameraEuler.y, 0);
        
        // 2. Calculate where the menu SHOULD be based on the saved offset
        Vector3 targetPosition = cameraTransform.position + (yawRotation * targetOffset);
        
        // 3. Smoothly glide to that position
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime);

        // 4. Smoothly rotate to always face the user (Billboarding)
        Vector3 lookAtPos = cameraTransform.position;
        lookAtPos.y = transform.position.y; // Keep the UI perfectly vertical
        transform.LookAt(2 * transform.position - lookAtPos); // Reverse look-at so the text isn't backwards
    }

    // Triggered the moment the user pinches/grabs the menu
    private void OnGrab(SelectEnterEventArgs args)
    {
        isGrabbed = true;
    }

    // Triggered the moment the user lets go of the menu
    private void OnRelease(SelectExitEventArgs args)
    {
        isGrabbed = false;
        UpdateOffset(); // Save the new position the user dropped it at!
    }

    // Calculates where the menu is currently sitting relative to the user's head
    private void UpdateOffset()
    {
        Vector3 cameraEuler = cameraTransform.eulerAngles;
        Quaternion inverseYaw = Quaternion.Inverse(Quaternion.Euler(0, cameraEuler.y, 0));
        
        // Save this distance and height as the new target
        targetOffset = inverseYaw * (transform.position - cameraTransform.position);
    }
}