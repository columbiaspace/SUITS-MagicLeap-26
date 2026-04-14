using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Reads the ML controller ray directly and invokes registered buttons
/// when the trigger is pressed while the ray intersects the button's rect.
/// Attach this to any persistent GameObject (e.g. the Canvas).
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

    [Header("Debug")]
    public bool enableDebugLogs = true;

    private readonly List<InputDevice> _allDevices = new List<InputDevice>();
    private bool _wasPressed;
    private float _diagnosticTimer;

    private void Start()
    {
        Debug.Log("[ControllerButtonInteraction] Script started.");
    }

    private void Update()
    {
        // Periodic diagnostics: log all found devices every 3 seconds
        _diagnosticTimer += Time.deltaTime;
        if (_diagnosticTimer >= 3f)
        {
            _diagnosticTimer = 0f;
            LogDeviceDiagnostics();
        }

        // Try all devices — no characteristic filter so we don't miss anything
        InputDevices.GetDevices(_allDevices);

        bool isPressed = false;
        Vector3 rayOrigin = Vector3.zero;
        Vector3 rayDirection = Vector3.forward;

        foreach (InputDevice device in _allDevices)
        {
            // Try triggerButton
            bool triggerHeld = false;
            device.TryGetFeatureValue(CommonUsages.triggerButton, out triggerHeld);

            // Also try primaryButton as fallback (bumper/menu on ML2)
            bool primaryHeld = false;
            device.TryGetFeatureValue(CommonUsages.primaryButton, out primaryHeld);

            if (!triggerHeld && !primaryHeld)
                continue;

            isPressed = true;

            device.TryGetFeatureValue(CommonUsages.devicePosition, out rayOrigin);
            Quaternion rotation = Quaternion.identity;
            device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
            rayDirection = rotation * Vector3.forward;

            if (enableDebugLogs)
                Debug.Log($"[ControllerButtonInteraction] Input detected on '{device.name}' " +
                          $"trigger={triggerHeld} primary={primaryHeld} " +
                          $"pos={rayOrigin} dir={rayDirection}");
            break;
        }

        if (isPressed && !_wasPressed)
        {
            TryClick(rayOrigin, rayDirection);
        }

        _wasPressed = isPressed;
    }

    private void TryClick(Vector3 origin, Vector3 direction)
    {
        Ray ray = new Ray(origin, direction);

        if (enableDebugLogs)
            Debug.Log($"[ControllerButtonInteraction] TryClick ray origin={origin} dir={direction}");

        foreach (ButtonEntry entry in buttons)
        {
            if (entry.button == null || entry.rectTransform == null)
            {
                Debug.LogWarning("[ControllerButtonInteraction] Button entry has null references.");
                continue;
            }

            bool interactable = entry.button.interactable;
            bool hits = RayHitsRect(ray, entry.rectTransform);

            if (enableDebugLogs)
                Debug.Log($"[ControllerButtonInteraction] Checking '{entry.button.name}' " +
                          $"interactable={interactable} rayHits={hits}");

            if (!interactable || !hits)
                continue;

            Debug.Log($"[ControllerButtonInteraction] >>> INVOKING onClick on '{entry.button.name}'");
            entry.button.onClick.Invoke();
            return;
        }

        if (enableDebugLogs)
            Debug.Log("[ControllerButtonInteraction] No button was hit.");
    }

    private static bool RayHitsRect(Ray ray, RectTransform rect)
    {
        Plane plane = new Plane(rect.forward, rect.position);

        float distance;
        if (!plane.Raycast(ray, out distance) || distance <= 0f)
            return false;

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
            Debug.Log("[ControllerButtonInteraction] No XR devices found.");
            return;
        }

        foreach (InputDevice device in _allDevices)
        {
            device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
            device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos);
            Debug.Log($"[ControllerButtonInteraction] Device: '{device.name}' " +
                      $"characteristics={device.characteristics} " +
                      $"trigger={trigger} pos={pos}");
        }

        // Also log button rect info
        foreach (ButtonEntry entry in buttons)
        {
            if (entry.rectTransform == null) continue;
            Debug.Log($"[ControllerButtonInteraction] Button '{entry.button?.name}' " +
                      $"worldPos={entry.rectTransform.position} " +
                      $"forward={entry.rectTransform.forward} " +
                      $"rect={entry.rectTransform.rect}");
        }
    }
}
