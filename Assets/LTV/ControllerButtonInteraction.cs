using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Fallback controller-ray input for the LTV HUD.
///
/// Reads pose + trigger from any connected XR InputDevice and invokes registered
/// Button onClick when the ray crosses the button's rect. Runs alongside the
/// standard XR UI pipeline (GraphicRaycaster + TrackedDeviceGraphicRaycaster) so
/// the button is clickable even if the XR Ray Interactor isn't wired up.
///
/// InputDevice poses are reported in XR tracking space. We transform them into
/// world space via the tracking-space reference transform (XR Origin / camera
/// parent) so the ray hits match the user-visible canvas.
/// </summary>
public class ControllerButtonInteraction : MonoBehaviour
{
    [System.Serializable]
    public struct ButtonEntry
    {
        public Button button;
        public RectTransform rectTransform;
    }

    [Header("Buttons to interact with")]
    public List<ButtonEntry> buttons;

    [Header("Tracking-space reference")]
    [Tooltip("Transform that represents the XR tracking origin (usually the XR Origin's Camera Offset, or the main camera's parent). " +
             "If null, we auto-detect via Camera.main.transform.parent.")]
    public Transform trackingSpace;

    [Header("Debug")]
    public bool enableDebugLogs = true;
    [Tooltip("Seconds between device-listing diagnostic logs. 0 disables periodic diagnostics.")]
    public float diagnosticIntervalSeconds = 3f;

    private readonly List<InputDevice> _allDevices = new List<InputDevice>();
    private bool _wasPressed;
    private float _diagnosticTimer;

    private void Start()
    {
        if (trackingSpace == null)
        {
            AutoFindTrackingSpace();
        }

        Debug.Log($"[CtrlButton] Start \u2014 buttons={buttons?.Count ?? 0} trackingSpace={(trackingSpace != null ? trackingSpace.name : "<world>")}" , this);
    }

    private void Update()
    {
        if (diagnosticIntervalSeconds > 0f)
        {
            _diagnosticTimer += Time.deltaTime;
            if (_diagnosticTimer >= diagnosticIntervalSeconds)
            {
                _diagnosticTimer = 0f;
                LogDeviceDiagnostics();
            }
        }

        InputDevices.GetDevices(_allDevices);

        bool isPressed = false;
        Vector3 localOrigin = Vector3.zero;
        Quaternion localRotation = Quaternion.identity;

        foreach (InputDevice device in _allDevices)
        {
            device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerHeld);
            device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryHeld);

            if (!triggerHeld && !primaryHeld) continue;

            isPressed = true;
            device.TryGetFeatureValue(CommonUsages.devicePosition, out localOrigin);
            device.TryGetFeatureValue(CommonUsages.deviceRotation, out localRotation);

            if (enableDebugLogs)
            {
                Debug.Log($"[CtrlButton] Input '{device.name}' trigger={triggerHeld} primary={primaryHeld} localPos={localOrigin}", this);
            }
            break;
        }

        if (isPressed && !_wasPressed)
        {
            Vector3 worldOrigin;
            Quaternion worldRotation;
            TransformToWorld(localOrigin, localRotation, out worldOrigin, out worldRotation);
            TryClick(worldOrigin, worldRotation * Vector3.forward);
        }

        _wasPressed = isPressed;
    }

    /// <summary>
    /// Public entry-point so tests (and other systems) can fire a synthetic click at a world ray.
    /// Returns the button that was invoked (null if nothing hit).
    /// </summary>
    public Button TryClick(Vector3 worldOrigin, Vector3 worldDirection)
    {
        Ray ray = new Ray(worldOrigin, worldDirection.normalized);

        if (enableDebugLogs)
        {
            Debug.Log($"[CtrlButton] TryClick origin={worldOrigin} dir={ray.direction}", this);
        }

        if (buttons == null) return null;

        foreach (ButtonEntry entry in buttons)
        {
            if (entry.button == null || entry.rectTransform == null)
            {
                Debug.LogWarning("[CtrlButton] Button entry has null references.", this);
                continue;
            }

            bool interactable = entry.button.interactable;
            bool hits = RayHitsRect(ray, entry.rectTransform);

            if (enableDebugLogs)
            {
                Debug.Log($"[CtrlButton] Checking '{entry.button.name}' interactable={interactable} hits={hits}", this);
            }

            if (!interactable || !hits) continue;

            Debug.Log($"[CtrlButton] >>> Invoking onClick on '{entry.button.name}'", this);
            entry.button.onClick.Invoke();
            return entry.button;
        }

        if (enableDebugLogs) Debug.Log("[CtrlButton] No button was hit.", this);
        return null;
    }

    private void TransformToWorld(Vector3 localPos, Quaternion localRot, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (trackingSpace == null)
        {
            AutoFindTrackingSpace();
        }

        if (trackingSpace == null)
        {
            worldPos = localPos;
            worldRot = localRot;
            return;
        }

        worldPos = trackingSpace.TransformPoint(localPos);
        worldRot = trackingSpace.rotation * localRot;
    }

    private void AutoFindTrackingSpace()
    {
        if (Camera.main != null && Camera.main.transform.parent != null)
        {
            trackingSpace = Camera.main.transform.parent;
        }
    }

    private static bool RayHitsRect(Ray ray, RectTransform rect)
    {
        Plane plane = new Plane(-rect.forward, rect.position);

        if (!plane.Raycast(ray, out float distance) || distance <= 0f)
        {
            return false;
        }

        Vector3 hitWorld = ray.GetPoint(distance);
        Vector3 hitLocal = rect.InverseTransformPoint(hitWorld);

        Rect bounds = rect.rect;
        return bounds.Contains(new Vector2(hitLocal.x, hitLocal.y));
    }

    private void LogDeviceDiagnostics()
    {
        if (!enableDebugLogs) return;

        InputDevices.GetDevices(_allDevices);

        if (_allDevices.Count == 0)
        {
            Debug.Log("[CtrlButton] No XR devices found.", this);
            return;
        }

        foreach (InputDevice device in _allDevices)
        {
            device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
            device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos);
            Debug.Log($"[CtrlButton] Device '{device.name}' chars={device.characteristics} trigger={trigger} localPos={pos}", this);
        }

        if (buttons == null) return;
        foreach (ButtonEntry entry in buttons)
        {
            if (entry.rectTransform == null) continue;
            Debug.Log($"[CtrlButton] Button '{entry.button?.name}' worldPos={entry.rectTransform.position} forward={entry.rectTransform.forward} rect={entry.rectTransform.rect}", this);
        }
    }
}
