using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;

/// Detects an open-palm-facing-user gesture on either hand via XR Hands.
/// Fires OnGestureTriggered after the pose is held continuously for HoldSeconds.
/// Requires a release period (ReleaseSeconds) below thresholds before re-firing.
public class OpenPalmGestureDetector : MonoBehaviour
{
    [SerializeField] private float holdSeconds = 0.3f;
    [SerializeField] private float releaseSeconds = 0.5f;
    [SerializeField, Range(0f, 1f), Tooltip("Min dot(palmNormal, toCamera). cos(~45°) ≈ 0.7.")]
    private float palmFacingDotMin = 0.7f;

    public UnityEvent OnGestureTriggered = new UnityEvent();

    private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();
    private XRHandSubsystem subsystem;
    private Transform cameraTransform;
    private float heldSeconds;
    private float releasedSeconds;
    private bool firedThisHold;

    private void OnEnable()
    {
        SubsystemManager.GetSubsystems(s_Subsystems);
        subsystem = s_Subsystems.Count > 0 ? s_Subsystems[0] : null;
        if (subsystem == null)
        {
            Debug.LogWarning("[OpenPalmGesture] No XRHandSubsystem available; detector idle.");
            return;
        }
        subsystem.updatedHands += OnHandsUpdated;
    }

    private void OnDisable()
    {
        if (subsystem != null) subsystem.updatedHands -= OnHandsUpdated;
        subsystem = null;
        heldSeconds = 0f;
        releasedSeconds = 0f;
        firedThisHold = false;
    }

    private void OnHandsUpdated(XRHandSubsystem sub, XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType updateType)
    {
        if (updateType != XRHandSubsystem.UpdateType.Dynamic) return;
        if (cameraTransform == null) cameraTransform = Camera.main != null ? Camera.main.transform : null;
        if (cameraTransform == null) return;

        bool active =
            ((flags & XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints) != 0 && IsOpenPalmFacingUser(sub.leftHand, Handedness.Left)) ||
            ((flags & XRHandSubsystem.UpdateSuccessFlags.RightHandJoints) != 0 && IsOpenPalmFacingUser(sub.rightHand, Handedness.Right));

        float dt = Time.deltaTime;
        if (active)
        {
            releasedSeconds = 0f;
            heldSeconds += dt;
            if (!firedThisHold && heldSeconds >= holdSeconds)
            {
                firedThisHold = true;
                OnGestureTriggered?.Invoke();
            }
        }
        else
        {
            heldSeconds = 0f;
            if (firedThisHold)
            {
                releasedSeconds += dt;
                if (releasedSeconds >= releaseSeconds) firedThisHold = false;
            }
        }
    }

    private bool IsOpenPalmFacingUser(XRHand hand, Handedness handedness)
    {
        if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist)) return false;
        if (!hand.GetJoint(XRHandJointID.IndexMetacarpal).TryGetPose(out var indexMcp)) return false;
        if (!hand.GetJoint(XRHandJointID.LittleMetacarpal).TryGetPose(out var littleMcp)) return false;

        Vector3 across = littleMcp.position - indexMcp.position;
        Vector3 along = (indexMcp.position + littleMcp.position) * 0.5f - wrist.position;
        Vector3 palmNormal = Vector3.Cross(along, across);
        if (handedness == Handedness.Left) palmNormal = -palmNormal;
        if (palmNormal.sqrMagnitude < 1e-8f) return false;
        palmNormal.Normalize();

        Vector3 toCamera = cameraTransform.position - wrist.position;
        if (toCamera.sqrMagnitude < 1e-6f) return false;
        if (Vector3.Dot(palmNormal, toCamera.normalized) < palmFacingDotMin) return false;

        return Extended(hand, XRHandJointID.IndexTip, XRHandJointID.IndexProximal, wrist.position)
            && Extended(hand, XRHandJointID.MiddleTip, XRHandJointID.MiddleProximal, wrist.position)
            && Extended(hand, XRHandJointID.RingTip, XRHandJointID.RingProximal, wrist.position)
            && Extended(hand, XRHandJointID.LittleTip, XRHandJointID.LittleProximal, wrist.position);
    }

    private static bool Extended(XRHand hand, XRHandJointID tip, XRHandJointID proximal, Vector3 wrist)
    {
        if (!hand.GetJoint(tip).TryGetPose(out var t)) return false;
        if (!hand.GetJoint(proximal).TryGetPose(out var p)) return false;
        return (t.position - wrist).sqrMagnitude > (p.position - wrist).sqrMagnitude;
    }
}
