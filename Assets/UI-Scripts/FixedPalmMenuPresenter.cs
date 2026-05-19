using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
#if XR_HANDS_1_1_OR_NEWER
using UnityEngine.XR.Hands;
#endif

public class FixedPalmMenuPresenter : MonoBehaviour
{
    [Header("Fixed Menu Placement")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float distanceFromCamera = 0.75f;
    [SerializeField] private float verticalOffset = -0.12f;
    [SerializeField] private float horizontalOffset = 0f;
    [SerializeField] private Vector3 fixedWorldScale = new Vector3(0.0015f, 0.0015f, 0.0015f);
    [SerializeField] private float followSmoothing = 18f;

    [Header("Palm Gesture")]
    [SerializeField] private bool requireOpenHand = true;
    [SerializeField] private float palmFacingCameraThreshold = 0.55f;
    [SerializeField] private float hideDelaySeconds = 0.2f;

    [Header("XR UI Interaction")]
    [SerializeField] private bool assignMainCameraToCanvases = true;
    [SerializeField] private bool requireTrackedDeviceRaycaster = true;

    [Header("Palm Debug Indicator")]
    [SerializeField] private bool showPalmDetectionIndicator = true;
    [SerializeField] private float indicatorDistanceFromCamera = 0.65f;
    [SerializeField] private Vector2 indicatorOffsetFromCenter = new Vector2(0.25f, -0.22f);
    [SerializeField] private float indicatorScale = 0.025f;
    [SerializeField] private Color noPalmColor = new Color(0.45f, 0.45f, 0.45f, 0.85f);
    [SerializeField] private Color palmTrackedColor = new Color(1f, 0.85f, 0.15f, 0.9f);
    [SerializeField] private Color palmMenuVisibleColor = new Color(0.1f, 1f, 0.25f, 0.95f);

    [Header("Legacy Hand Menu")]
    [SerializeField] private bool disableLegacyPalmFollow = true;

    private Canvas[] canvases;
    private GraphicRaycaster[] graphicRaycasters;
    private TrackedDeviceGraphicRaycaster[] trackedDeviceGraphicRaycasters;
    private CanvasGroup canvasGroup;
    private float lastValidGestureTime = float.NegativeInfinity;
    private bool isVisible;
    private GameObject palmDetectionIndicator;
    private Renderer palmDetectionIndicatorRenderer;
    private Material palmDetectionIndicatorMaterial;

    private enum PalmGestureState
    {
        NoTrackedPalm,
        PalmTracked,
        PalmPresentingMenu,
    }

#if XR_HANDS_1_1_OR_NEWER
    private XRHandSubsystem handSubsystem;
    private static readonly List<XRHandSubsystem> s_HandSubsystems = new List<XRHandSubsystem>();
#endif

    private void Awake()
    {
        CacheUiComponents();

        if (disableLegacyPalmFollow)
            DisableLegacyHandMenu();
    }

    private void OnEnable()
    {
        ResolveCamera();
        EnsureTrackedDeviceRaycasters();
        ConfigureCanvasesForXrUi();
        EnsurePalmDetectionIndicator();
#if XR_HANDS_1_1_OR_NEWER
        ResolveHandSubsystem();
#else
        Debug.LogWarning("[FixedPalmMenuPresenter] XR Hands package symbols are unavailable; palm gesture detection is disabled.", this);
#endif
        SetMenuVisible(false, true);
    }

    private void OnDisable()
    {
        if (palmDetectionIndicator != null)
            palmDetectionIndicator.SetActive(false);
    }

    private void OnDestroy()
    {
        if (palmDetectionIndicatorMaterial != null)
            Destroy(palmDetectionIndicatorMaterial);

        if (palmDetectionIndicator != null)
            Destroy(palmDetectionIndicator);
    }

    private void Update()
    {
        ResolveCamera();
        ConfigureCanvasesForXrUi();

        PalmGestureState gestureState = GetPalmGestureState();
        bool gestureIsValid = gestureState == PalmGestureState.PalmPresentingMenu;
        if (gestureIsValid)
            lastValidGestureTime = Time.time;

        bool shouldShow = gestureIsValid || Time.time - lastValidGestureTime <= hideDelaySeconds;
        SetMenuVisible(shouldShow);
        UpdatePalmDetectionIndicator(gestureState);

        if (shouldShow)
            PositionInFrontOfCamera();
    }

    private void CacheUiComponents()
    {
        canvases = GetComponentsInChildren<Canvas>(true);
        graphicRaycasters = GetComponentsInChildren<GraphicRaycaster>(true);
        trackedDeviceGraphicRaycasters = GetComponentsInChildren<TrackedDeviceGraphicRaycaster>(true);
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void ResolveCamera()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void EnsureTrackedDeviceRaycasters()
    {
        if (!requireTrackedDeviceRaycaster)
            return;

        if (trackedDeviceGraphicRaycasters != null && trackedDeviceGraphicRaycasters.Length > 0)
            return;

        Canvas rootCanvas = GetComponent<Canvas>();
        if (rootCanvas == null)
            return;

        gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        trackedDeviceGraphicRaycasters = GetComponentsInChildren<TrackedDeviceGraphicRaycaster>(true);
    }

    private void ConfigureCanvasesForXrUi()
    {
        if (!assignMainCameraToCanvases || cameraTransform == null || canvases == null)
            return;

        Camera eventCamera = cameraTransform.GetComponent<Camera>();
        if (eventCamera == null)
            return;

        foreach (Canvas canvas in canvases)
        {
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
                continue;

            canvas.worldCamera = eventCamera;
        }
    }

    private void EnsurePalmDetectionIndicator()
    {
        if (!showPalmDetectionIndicator || palmDetectionIndicator != null)
            return;

        palmDetectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        palmDetectionIndicator.name = "Palm Menu Detection Indicator";

        Collider indicatorCollider = palmDetectionIndicator.GetComponent<Collider>();
        if (indicatorCollider != null)
            Destroy(indicatorCollider);

        palmDetectionIndicatorRenderer = palmDetectionIndicator.GetComponent<Renderer>();
        if (palmDetectionIndicatorRenderer != null)
        {
            Shader indicatorShader = Shader.Find("Unlit/Color");
            if (indicatorShader == null)
                indicatorShader = Shader.Find("Sprites/Default");

            if (indicatorShader != null)
            {
                palmDetectionIndicatorMaterial = new Material(indicatorShader);
                palmDetectionIndicatorRenderer.material = palmDetectionIndicatorMaterial;
            }
        }
    }

    private void UpdatePalmDetectionIndicator(PalmGestureState gestureState)
    {
        if (!showPalmDetectionIndicator)
        {
            if (palmDetectionIndicator != null)
                palmDetectionIndicator.SetActive(false);
            return;
        }

        EnsurePalmDetectionIndicator();
        if (palmDetectionIndicator == null || cameraTransform == null)
            return;

        palmDetectionIndicator.SetActive(true);
        palmDetectionIndicator.transform.position =
            cameraTransform.position +
            cameraTransform.forward * indicatorDistanceFromCamera +
            cameraTransform.right * indicatorOffsetFromCenter.x +
            cameraTransform.up * indicatorOffsetFromCenter.y;
        palmDetectionIndicator.transform.rotation = Quaternion.LookRotation(cameraTransform.forward, cameraTransform.up);
        palmDetectionIndicator.transform.localScale = Vector3.one * indicatorScale;

        if (palmDetectionIndicatorMaterial != null)
            palmDetectionIndicatorMaterial.color = GetIndicatorColor(gestureState);
    }

    private Color GetIndicatorColor(PalmGestureState gestureState)
    {
        switch (gestureState)
        {
            case PalmGestureState.PalmPresentingMenu:
                return palmMenuVisibleColor;
            case PalmGestureState.PalmTracked:
                return palmTrackedColor;
            default:
                return noPalmColor;
        }
    }

    private void DisableLegacyHandMenu()
    {
        Transform searchRoot = transform.root;
        MonoBehaviour[] behaviours = searchRoot.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null || behaviour == this)
                continue;

            if (behaviour.GetType().Name == "HandMenu")
                behaviour.enabled = false;
        }
    }

    private void SetMenuVisible(bool visible, bool force = false)
    {
        if (!force && isVisible == visible)
            return;

        isVisible = visible;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        if (canvases != null)
        {
            foreach (Canvas canvas in canvases)
            {
                if (canvas != null)
                    canvas.enabled = visible;
            }
        }

        if (graphicRaycasters != null)
        {
            foreach (GraphicRaycaster raycaster in graphicRaycasters)
            {
                if (raycaster != null)
                    raycaster.enabled = visible;
            }
        }

        if (trackedDeviceGraphicRaycasters != null)
        {
            foreach (TrackedDeviceGraphicRaycaster raycaster in trackedDeviceGraphicRaycasters)
            {
                if (raycaster != null)
                    raycaster.enabled = visible;
            }
        }
    }

    private void PositionInFrontOfCamera()
    {
        if (cameraTransform == null)
            return;

        Vector3 targetPosition =
            cameraTransform.position +
            cameraTransform.forward * distanceFromCamera +
            cameraTransform.right * horizontalOffset +
            Vector3.up * verticalOffset;

        Quaternion targetRotation = Quaternion.LookRotation(cameraTransform.position - targetPosition, Vector3.up);

        float lerp = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPosition, lerp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lerp);
        transform.localScale = fixedWorldScale;
    }

    private PalmGestureState GetPalmGestureState()
    {
#if XR_HANDS_1_1_OR_NEWER
        if (handSubsystem == null || !handSubsystem.running)
            ResolveHandSubsystem();

        if (handSubsystem == null)
            return PalmGestureState.NoTrackedPalm;

        bool leftTracked = IsPalmTracked(handSubsystem.leftHand);
        bool rightTracked = IsPalmTracked(handSubsystem.rightHand);
        if (IsPalmPresentingMenu(handSubsystem.leftHand) || IsPalmPresentingMenu(handSubsystem.rightHand))
            return PalmGestureState.PalmPresentingMenu;

        return leftTracked || rightTracked ? PalmGestureState.PalmTracked : PalmGestureState.NoTrackedPalm;
#else
        return PalmGestureState.NoTrackedPalm;
#endif
    }

#if XR_HANDS_1_1_OR_NEWER
    private void ResolveHandSubsystem()
    {
        if (handSubsystem != null && handSubsystem.running)
            return;

        SubsystemManager.GetSubsystems(s_HandSubsystems);
        handSubsystem = null;

        foreach (XRHandSubsystem subsystem in s_HandSubsystems)
        {
            if (subsystem != null && subsystem.running)
            {
                handSubsystem = subsystem;
                return;
            }
        }

        if (s_HandSubsystems.Count > 0)
            handSubsystem = s_HandSubsystems[0];
    }

    private static bool IsPalmTracked(XRHand hand)
    {
        if (!hand.isTracked)
            return false;

        XRHandJoint palm = hand.GetJoint(XRHandJointID.Palm);
        return palm.TryGetPose(out _);
    }

    private bool IsPalmPresentingMenu(XRHand hand)
    {
        if (!hand.isTracked || cameraTransform == null)
            return false;

        XRHandJoint palm = hand.GetJoint(XRHandJointID.Palm);
        if (!palm.TryGetPose(out Pose palmPose))
            return false;

        Vector3 directionToCamera = (cameraTransform.position - palmPose.position).normalized;
        Vector3 palmNormal = palmPose.rotation * Vector3.up;
        bool palmFacesCamera = Vector3.Dot(palmNormal, directionToCamera) >= palmFacingCameraThreshold;

        if (!palmFacesCamera)
            return false;

        return !requireOpenHand || IsOpenHand(hand);
    }

    private static bool IsOpenHand(XRHand hand)
    {
        return IsFingerExtended(hand, XRHandJointID.IndexTip, XRHandJointID.IndexIntermediate) &&
               IsFingerExtended(hand, XRHandJointID.MiddleTip, XRHandJointID.MiddleIntermediate) &&
               IsFingerExtended(hand, XRHandJointID.RingTip, XRHandJointID.RingIntermediate) &&
               IsFingerExtended(hand, XRHandJointID.LittleTip, XRHandJointID.LittleIntermediate);
    }

    private static bool IsFingerExtended(XRHand hand, XRHandJointID tipId, XRHandJointID middleJointId)
    {
        if (!(hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wristPose) &&
              hand.GetJoint(tipId).TryGetPose(out Pose tipPose) &&
              hand.GetJoint(middleJointId).TryGetPose(out Pose middlePose)))
        {
            return false;
        }

        float wristToTip = (tipPose.position - wristPose.position).sqrMagnitude;
        float wristToMiddle = (middlePose.position - wristPose.position).sqrMagnitude;
        return wristToTip > wristToMiddle;
    }
#endif
}
