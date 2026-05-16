using UnityEngine;
using UnityEngine.UI;

public class VitalsButton : MonoBehaviour
{
    public GameObject healthCanvas;
    public Image buttonImage;

    [Header("Colors")]
    public Color normalColor = new Color(0.498f, 0.788f, 0.867f, 0.4f);
    public Color selectedColor = new Color(0.35f, 0.6f, 0.7f, 0.55f);

    [Header("Spawn Placement")]
    [Tooltip("When opening the menu, snap it to a position in front of the user so it's always visible. Disable to keep wherever it was last left.")]
    public bool snapInFrontOfCamera = true;
    [Tooltip("Local offset from the camera when re-snapping (x = right, y = up, z = forward).")]
    public Vector3 spawnOffsetFromCamera = new Vector3(0f, -0.05f, 0.9f);

    private void Start()
    {
        if (buttonImage != null)
            buttonImage.color = (healthCanvas != null && healthCanvas.activeSelf) ? selectedColor : normalColor;
    }

    public void VitalsButton_OnClick()
    {
        if (healthCanvas == null) return;

        bool newState = !healthCanvas.activeSelf;

        if (newState && snapInFrontOfCamera)
        {
            PositionMenuInFrontOfCamera();
        }

        healthCanvas.SetActive(newState);

        if (buttonImage != null)
            buttonImage.color = newState ? selectedColor : normalColor;
    }

    private void PositionMenuInFrontOfCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Transform camT = cam.transform;
        Vector3 forwardFlat = camT.forward;
        forwardFlat.y = 0f;
        if (forwardFlat.sqrMagnitude < 0.0001f) forwardFlat = Vector3.forward;
        forwardFlat.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forwardFlat);

        Vector3 spawn = camT.position
            + forwardFlat * spawnOffsetFromCamera.z
            + right * spawnOffsetFromCamera.x
            + Vector3.up * spawnOffsetFromCamera.y;

        healthCanvas.transform.position = spawn;
        healthCanvas.transform.rotation = Quaternion.LookRotation(forwardFlat, Vector3.up);

        Rigidbody rb = healthCanvas.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}
