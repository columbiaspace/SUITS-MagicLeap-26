using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.MagicLeap;
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
    [SerializeField] private MenuTriggerHand menuTriggerHand = MenuTriggerHand.Right;
    [SerializeField] private float palmShowFacingCameraThreshold = 0.6f;
    [SerializeField] private float palmHideFacingCameraThreshold = 0.45f;
    [SerializeField] private float hideDelaySeconds = 0.2f;
    [SerializeField] private float fingerExtensionSlack = 0.85f;

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

    [Header("Debug Logging")]
    [SerializeField] private bool logHandState;
    [SerializeField] private float handStateLogIntervalSeconds = 1f;

    [Header("Debug HUD")]
    [SerializeField] private bool showDebugStatusText = true;
    [SerializeField] private bool forceShowMenu;
    [SerializeField] private bool debugBypassPalmFacingCheck;
    [SerializeField] private bool debugBypassOpenHandCheck;
    [SerializeField] private float debugTextDistanceFromCamera = 0.65f;
    [SerializeField] private Vector2 debugTextOffsetFromCenter = new Vector2(-0.32f, -0.18f);
    [SerializeField] private float debugTextCharacterSize = 0.012f;
    [SerializeField] private Color debugTextColor = new Color(0.9f, 1f, 1f, 1f);

    [Header("Legacy Hand Menu")]
    [SerializeField] private bool disableLegacyPalmFollow = true;
    [SerializeField] private MonoBehaviour legacyHandMenu;

    private Canvas[] canvases;
    private GraphicRaycaster[] graphicRaycasters;
    private TrackedDeviceGraphicRaycaster[] trackedDeviceGraphicRaycasters;
    private CanvasGroup canvasGroup;
    private float lastValidGestureTime = float.NegativeInfinity;
    private bool isVisible;
    private bool currentShouldShow;
    private bool canvasesConfiguredForXrUi;
    private PalmGestureState currentGestureState = PalmGestureState.NoTrackedPalm;
    private GameObject palmDetectionIndicator;
    private Renderer palmDetectionIndicatorRenderer;
    private Material palmDetectionIndicatorMaterial;
    private TextMesh debugStatusText;
    private GameObject debugStatusTextObject;
    private bool lastLeftTracked;
    private bool lastRightTracked;
    private bool lastLeftOpen;
    private bool lastRightOpen;
    private bool lastLeftPresenting;
    private bool lastRightPresenting;

    private enum PalmGestureState
    {
        NoTrackedPalm,
        PalmTracked,
        PalmPresentingMenu,
    }

    private enum MenuTriggerHand
    {
        Left,
        Right,
        Either,
    }

#if XR_HANDS_1_1_OR_NEWER
    private XRHandSubsystem handSubsystem;
    private bool subscribedToHandUpdates;
    private static readonly List<XRHandSubsystem> s_HandSubsystems = new List<XRHandSubsystem>();
#endif

    // Hand-tracking is a runtime ("dangerous") permission on ML2. Manifest declaration
    // alone is not enough: until MLPermissions grants HAND_TRACKING at runtime, the
    // XRHandSubsystem exists but every XRHand.isTracked stays false, so the dot is gray.
    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();
    private bool handTrackingPermissionGranted;
    private bool handTrackingPermissionRequested;
    private bool loggedSubsystemFound;
    private bool loggedSubsystemRunning;
    private bool loggedSubsystemMissing;
    private float lastHandStateLogTime;
    private float lastRightPalmDot = float.NaN;
    private float lastLeftPalmDot = float.NaN;

    private void Awake()
    {
        Debug.Log("[FixedPalmMenuPresenter] Awake running.", this);
        CacheUiComponents();

        if (disableLegacyPalmFollow)
            DisableLegacyHandMenu();

        permissionCallbacks.OnPermissionGranted += OnHandTrackingPermissionGranted;
        permissionCallbacks.OnPermissionDenied += OnHandTrackingPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain += OnHandTrackingPermissionDenied;
    }

    private void Start()
    {
        Debug.Log("[FixedPalmMenuPresenter] Start running.", this);
        if (handTrackingPermissionRequested)
            return;

        handTrackingPermissionRequested = true;
        Debug.Log($"[FixedPalmMenuPresenter] Permission requested: {MLPermission.HandTracking}", this);
        MLPermissions.RequestPermission(MLPermission.HandTracking, permissionCallbacks);
    }

    private void OnEnable()
    {
        Debug.Log("[FixedPalmMenuPresenter] OnEnable running.", this);
        ResolveCamera();
        EnsureTrackedDeviceRaycasters();
        ConfigureCanvasesForXrUi();
        EnsurePalmDetectionIndicator();
        EnsureDebugStatusText();
#if XR_HANDS_1_1_OR_NEWER
        TrySubscribeToHandUpdates();
#endif
#if !XR_HANDS_1_1_OR_NEWER
        Debug.LogWarning("[FixedPalmMenuPresenter] XR Hands package symbols are unavailable; palm gesture detection is disabled.", this);
#endif
        SetMenuVisible(false, true);
    }

    private void OnDisable()
    {
#if XR_HANDS_1_1_OR_NEWER
        UnsubscribeFromHandUpdates();
#endif
        if (palmDetectionIndicator != null)
            palmDetectionIndicator.SetActive(false);

        if (debugStatusTextObject != null)
            debugStatusTextObject.SetActive(false);
    }

    private void OnDestroy()
    {
#if XR_HANDS_1_1_OR_NEWER
        UnsubscribeFromHandUpdates();
#endif
        permissionCallbacks.OnPermissionGranted -= OnHandTrackingPermissionGranted;
        permissionCallbacks.OnPermissionDenied -= OnHandTrackingPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain -= OnHandTrackingPermissionDenied;

        if (palmDetectionIndicatorMaterial != null)
            Destroy(palmDetectionIndicatorMaterial);

        if (palmDetectionIndicator != null)
            Destroy(palmDetectionIndicator);

        if (debugStatusTextObject != null)
            Destroy(debugStatusTextObject);
    }

    private void OnHandTrackingPermissionGranted(string permission)
    {
        if (permission != MLPermission.HandTracking)
            return;

        handTrackingPermissionGranted = true;
        Debug.Log($"[FixedPalmMenuPresenter] Permission granted: {permission}. Resolving XRHandSubsystem...", this);
#if XR_HANDS_1_1_OR_NEWER
        ResolveHandSubsystem();
        TrySubscribeToHandUpdates();
#endif
    }

    private void OnHandTrackingPermissionDenied(string permission)
    {
        if (permission != MLPermission.HandTracking)
            return;

        handTrackingPermissionGranted = false;
        Debug.LogWarning($"[FixedPalmMenuPresenter] Permission denied: {permission}. Hand tracking disabled; dot stays gray.", this);
    }

    private void Update()
    {
        if (cameraTransform == null && ResolveCamera())
            ConfigureCanvasesForXrUi();

        bool shouldShow = forceShowMenu || currentShouldShow;
        SetMenuVisible(shouldShow);
        UpdatePalmDetectionIndicator(currentGestureState);
        UpdateDebugStatusText(currentGestureState, shouldShow);

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

    private bool ResolveCamera()
    {
        if (cameraTransform != null)
            return true;

        if (Camera.main == null)
            return false;

        cameraTransform = Camera.main.transform;
        return true;
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
        if (canvasesConfiguredForXrUi || !assignMainCameraToCanvases || cameraTransform == null || canvases == null)
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

        canvasesConfiguredForXrUi = true;
    }

    private void EnsurePalmDetectionIndicator()
    {
        if (!showPalmDetectionIndicator || palmDetectionIndicator != null)
            return;

        palmDetectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        palmDetectionIndicator.name = "Palm Menu Detection Indicator";
        palmDetectionIndicator.transform.SetParent(transform, true);

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

    private void EnsureDebugStatusText()
    {
        if (!showDebugStatusText || debugStatusTextObject != null)
            return;

        debugStatusTextObject = new GameObject("Hand Menu Debug Status");
        debugStatusTextObject.transform.SetParent(transform, true);
        debugStatusText = debugStatusTextObject.AddComponent<TextMesh>();
        debugStatusText.anchor = TextAnchor.UpperLeft;
        debugStatusText.alignment = TextAlignment.Left;
        debugStatusText.fontSize = 48;
        debugStatusText.characterSize = debugTextCharacterSize;
        debugStatusText.color = debugTextColor;
    }

    private void UpdateDebugStatusText(PalmGestureState gestureState, bool shouldShow)
    {
        if (!showDebugStatusText)
        {
            if (debugStatusTextObject != null)
                debugStatusTextObject.SetActive(false);
            return;
        }

        EnsureDebugStatusText();
        if (debugStatusTextObject == null || debugStatusText == null || cameraTransform == null)
            return;

        debugStatusTextObject.SetActive(true);
        debugStatusTextObject.transform.position =
            cameraTransform.position +
            cameraTransform.forward * debugTextDistanceFromCamera +
            cameraTransform.right * debugTextOffsetFromCenter.x +
            cameraTransform.up * debugTextOffsetFromCenter.y;
        debugStatusTextObject.transform.rotation = Quaternion.LookRotation(cameraTransform.forward, cameraTransform.up);
        debugStatusText.characterSize = debugTextCharacterSize;
        debugStatusText.color = debugTextColor;
        debugStatusText.text =
            "Hand Menu Debug\n" +
            $"Presenter: running\n" +
            $"Permission: {GetPermissionStatus()}\n" +
            $"XR Hands: {GetXrHandsStatus()}\n" +
            $"Menu: {(shouldShow ? "shown" : "hidden")} force={forceShowMenu}\n" +
            $"Bypass: palm={debugBypassPalmFacingCheck} open={debugBypassOpenHandCheck}\n" +
            $"L: tracked={lastLeftTracked} open={lastLeftOpen} dot={Fmt(lastLeftPalmDot)} present={lastLeftPresenting}\n" +
            $"R: tracked={lastRightTracked} open={lastRightOpen} dot={Fmt(lastRightPalmDot)} present={lastRightPresenting}\n" +
            $"Threshold: show={palmShowFacingCameraThreshold:F2} hide={palmHideFacingCameraThreshold:F2}";
    }

    private string GetPermissionStatus()
    {
        if (handTrackingPermissionGranted)
            return "granted";

        return handTrackingPermissionRequested ? "requested/waiting" : "not requested";
    }

    private string GetXrHandsStatus()
    {
#if XR_HANDS_1_1_OR_NEWER
        if (handSubsystem == null)
            return "missing";

        return handSubsystem.running ? "running" : "found but stopped";
#else
        return "XR_HANDS symbols unavailable";
#endif
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
        if (legacyHandMenu != null)
        {
            legacyHandMenu.enabled = false;
            return;
        }

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
        Debug.Log($"[FixedPalmMenuPresenter] Menu {(visible ? "shown" : "hidden")}.", this);

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

    private void EvaluateGestureAndUpdateMenu()
    {
        currentGestureState = GetPalmGestureState();
        bool gestureIsValid = currentGestureState == PalmGestureState.PalmPresentingMenu;
        if (gestureIsValid)
            lastValidGestureTime = Time.time;

        currentShouldShow = gestureIsValid || Time.time - lastValidGestureTime <= hideDelaySeconds;
        bool shouldShow = forceShowMenu || currentShouldShow;
        SetMenuVisible(shouldShow);
        UpdatePalmDetectionIndicator(currentGestureState);
        UpdateDebugStatusText(currentGestureState, shouldShow);

        if (shouldShow)
            PositionInFrontOfCamera();
    }

    private PalmGestureState GetPalmGestureState()
    {
#if XR_HANDS_1_1_OR_NEWER
        if (!handTrackingPermissionGranted)
        {
            ResetLastHandState();
            LogHandStateRateLimited(false, false, false, false);
            return PalmGestureState.NoTrackedPalm;
        }

        if (handSubsystem == null || !handSubsystem.running)
            ResolveHandSubsystem();

        if (handSubsystem == null)
        {
            ResetLastHandState();
            LogHandStateRateLimited(false, false, false, false);
            return PalmGestureState.NoTrackedPalm;
        }

        bool leftTracked = IsPalmTracked(handSubsystem.leftHand);
        bool rightTracked = IsPalmTracked(handSubsystem.rightHand);
        lastLeftTracked = leftTracked;
        lastRightTracked = rightTracked;
        lastLeftOpen = false;
        lastRightOpen = false;

        bool rightPresenting = IsPalmPresentingMenu(handSubsystem.rightHand, isRightHand: true);
        bool leftPresenting = IsPalmPresentingMenu(handSubsystem.leftHand, isRightHand: false);
        lastLeftPresenting = leftPresenting;
        lastRightPresenting = rightPresenting;
        LogHandStateRateLimited(leftTracked, rightTracked, leftPresenting, rightPresenting);

        if (ShouldOpenFromHands(leftPresenting, rightPresenting))
            return PalmGestureState.PalmPresentingMenu;

        return leftTracked || rightTracked ? PalmGestureState.PalmTracked : PalmGestureState.NoTrackedPalm;
#else
        return PalmGestureState.NoTrackedPalm;
#endif
    }

    private void ResetLastHandState()
    {
        lastLeftTracked = false;
        lastRightTracked = false;
        lastLeftOpen = false;
        lastRightOpen = false;
        lastLeftPresenting = false;
        lastRightPresenting = false;
        lastLeftPalmDot = float.NaN;
        lastRightPalmDot = float.NaN;
    }

    private static string Fmt(float value)
    {
        return float.IsNaN(value) ? "n/a" : value.ToString("F2");
    }

    private bool ShouldOpenFromHands(bool leftPresenting, bool rightPresenting)
    {
        switch (menuTriggerHand)
        {
            case MenuTriggerHand.Left:
                return leftPresenting;
            case MenuTriggerHand.Either:
                return leftPresenting || rightPresenting;
            default:
                return rightPresenting;
        }
    }

#if XR_HANDS_1_1_OR_NEWER
    private void TrySubscribeToHandUpdates()
    {
        if (!handTrackingPermissionGranted || subscribedToHandUpdates)
            return;

        if (handSubsystem == null || !handSubsystem.running)
            ResolveHandSubsystem();

        if (handSubsystem == null || !handSubsystem.running)
            return;

        handSubsystem.updatedHands += OnUpdatedHands;
        subscribedToHandUpdates = true;
        Debug.Log("[FixedPalmMenuPresenter] Subscribed to XRHandSubsystem.updatedHands.", this);
    }

    private void UnsubscribeFromHandUpdates()
    {
        if (!subscribedToHandUpdates || handSubsystem == null)
            return;

        handSubsystem.updatedHands -= OnUpdatedHands;
        subscribedToHandUpdates = false;
    }

    private void OnUpdatedHands(
        XRHandSubsystem subsystem,
        XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
        XRHandSubsystem.UpdateType updateType)
    {
        if (updateType != XRHandSubsystem.UpdateType.Dynamic)
            return;

        EvaluateGestureAndUpdateMenu();
    }

    private void ResolveHandSubsystem()
    {
        if (handSubsystem != null && handSubsystem.running)
            return;

        SubsystemManager.GetSubsystems(s_HandSubsystems);
        handSubsystem = null;

        foreach (XRHandSubsystem subsystem in s_HandSubsystems)
        {
            if (subsystem == null)
                continue;

            handSubsystem = subsystem;
            if (!loggedSubsystemFound)
            {
                Debug.Log($"[FixedPalmMenuPresenter] XRHandSubsystem found (running={subsystem.running}).", this);
                loggedSubsystemFound = true;
            }

            if (subsystem.running)
            {
                if (!loggedSubsystemRunning)
                {
                    Debug.Log("[FixedPalmMenuPresenter] XRHandSubsystem running; joint data should now flow.", this);
                    loggedSubsystemRunning = true;
                }
                return;
            }
        }

        if (handSubsystem == null && !loggedSubsystemMissing)
        {
            Debug.LogWarning("[FixedPalmMenuPresenter] No XRHandSubsystem present after permission grant. Check OpenXR HandTracking feature is enabled for Android.", this);
            loggedSubsystemMissing = true;
        }
    }

    private static bool IsPalmTracked(XRHand hand)
    {
        XRHandJoint palm = hand.GetJoint(XRHandJointID.Palm);
        return palm.TryGetPose(out _);
    }

    private bool IsPalmPresentingMenu(XRHand hand, bool isRightHand)
    {
        if (cameraTransform == null)
            return false;

        XRHandJoint palm = hand.GetJoint(XRHandJointID.Palm);
        if (!palm.TryGetPose(out Pose palmPose))
            return false;

        Vector3 directionToCamera = (cameraTransform.position - palmPose.position).normalized;
        // In OpenXR XR Hands, -Z of the palm-local frame is the palm-face normal.
        Vector3 palmNormal = palmPose.rotation * Vector3.back;
        float palmDot = Vector3.Dot(palmNormal, directionToCamera);
        if (isRightHand) lastRightPalmDot = palmDot; else lastLeftPalmDot = palmDot;
        float facingThreshold = isVisible ? palmHideFacingCameraThreshold : palmShowFacingCameraThreshold;
        if (!debugBypassPalmFacingCheck && palmDot < facingThreshold)
            return false;

        bool isOpen = IsOpenHand(hand);
        if (isRightHand) lastRightOpen = isOpen; else lastLeftOpen = isOpen;

        return debugBypassOpenHandCheck || !requireOpenHand || isOpen;
    }

    private void LogHandStateRateLimited(bool leftTracked, bool rightTracked, bool leftPresenting, bool rightPresenting)
    {
        if (!logHandState) return;
        if (Time.unscaledTime - lastHandStateLogTime < handStateLogIntervalSeconds) return;
        lastHandStateLogTime = Time.unscaledTime;
        Debug.Log(
            $"[FixedPalmMenuPresenter] L:tracked={leftTracked} dot={Fmt(lastLeftPalmDot)} present={leftPresenting} | " +
            $"R:tracked={rightTracked} dot={Fmt(lastRightPalmDot)} present={rightPresenting} | " +
            $"thresholds=show:{palmShowFacingCameraThreshold:F2}/hide:{palmHideFacingCameraThreshold:F2}",
            this);
    }

    private bool IsOpenHand(XRHand hand)
    {
        return IsFingerExtended(hand, XRHandJointID.IndexTip, XRHandJointID.IndexIntermediate) &&
               IsFingerExtended(hand, XRHandJointID.MiddleTip, XRHandJointID.MiddleIntermediate) &&
               IsFingerExtended(hand, XRHandJointID.RingTip, XRHandJointID.RingIntermediate) &&
               IsFingerExtended(hand, XRHandJointID.LittleTip, XRHandJointID.LittleIntermediate);
    }

    private bool IsFingerExtended(XRHand hand, XRHandJointID tipId, XRHandJointID middleJointId)
    {
        if (!(hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wristPose) &&
              hand.GetJoint(tipId).TryGetPose(out Pose tipPose) &&
              hand.GetJoint(middleJointId).TryGetPose(out Pose middlePose)))
        {
            return false;
        }

        float wristToTip = (tipPose.position - wristPose.position).sqrMagnitude;
        float wristToMiddle = (middlePose.position - wristPose.position).sqrMagnitude;
        return wristToTip > wristToMiddle * fingerExtensionSlack;
    }
#endif
}
