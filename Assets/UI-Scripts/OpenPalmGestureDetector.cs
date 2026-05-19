using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;

/// <summary>
/// Fires <see cref="OnGestureTriggered"/> once when either hand holds an open palm
/// facing the user for <see cref="holdSeconds"/>. Requires <see cref="releaseSeconds"/>
/// of release before re-firing. Scene-scoped; subscribe to act on it.
/// </summary>
public class OpenPalmGestureDetector : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float palmFacingDotThreshold = 0.7f;
    [SerializeField, Range(0f, 1f)] private float fingerStraightDotThreshold = 0.85f;
    [SerializeField] private float holdSeconds = 0.3f;
    [SerializeField] private float releaseSeconds = 0.5f;
    public UnityEvent OnGestureTriggered;

    private XRHandSubsystem _subsystem;
    private Transform _xrOriginTransform;
    private Camera _headCamera;
    private static readonly List<XRHandSubsystem> s_SubsystemsReuse = new List<XRHandSubsystem>();
    private readonly float[] _heldSince = { -1f, -1f };
    private readonly float[] _releasedSince = { 0f, 0f };
    private readonly bool[] _awaitingRelease = { false, false };

    private void OnDisable()
    {
        if (_subsystem != null) { _subsystem.updatedHands -= OnUpdatedHands; _subsystem = null; }
        for (int i = 0; i < 2; i++) { _heldSince[i] = -1f; _releasedSince[i] = 0f; _awaitingRelease[i] = false; }
    }

    private void Update()
    {
        if (_subsystem != null && _subsystem.running) return;
        SubsystemManager.GetSubsystems(s_SubsystemsReuse);
        for (int i = 0; i < s_SubsystemsReuse.Count; i++)
        {
            if (!s_SubsystemsReuse[i].running) continue;
            _subsystem = s_SubsystemsReuse[i];
            _subsystem.updatedHands -= OnUpdatedHands;
            _subsystem.updatedHands += OnUpdatedHands;
            return;
        }
    }

    private void OnUpdatedHands(XRHandSubsystem sub, XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType updateType)
    {
        if (updateType != XRHandSubsystem.UpdateType.Dynamic) return;
        Evaluate(sub.leftHand, Handedness.Left, 0);
        Evaluate(sub.rightHand, Handedness.Right, 1);
    }

    private void Evaluate(XRHand hand, Handedness handedness, int idx)
    {
        bool active = hand.isTracked && IsOpenPalmFacingUser(hand, handedness);
        float now = Time.unscaledTime;

        if (!active)
        {
            _heldSince[idx] = -1f;
            if (!_awaitingRelease[idx]) return;
            _releasedSince[idx] += Time.unscaledDeltaTime;
            if (_releasedSince[idx] >= releaseSeconds) { _awaitingRelease[idx] = false; _releasedSince[idx] = 0f; }
            return;
        }

        if (_awaitingRelease[idx]) return;
        if (_heldSince[idx] < 0f) _heldSince[idx] = now;
        if (now - _heldSince[idx] < holdSeconds) return;

        _awaitingRelease[idx] = true;
        _releasedSince[idx] = 0f;
        _heldSince[idx] = -1f;
        OnGestureTriggered?.Invoke();
    }

    private bool IsOpenPalmFacingUser(XRHand hand, Handedness handedness)
    {
        if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist)) return false;
        if (!hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out Pose middleProx)) return false;
        if (!IsExtended(hand, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexTip)) return false;
        if (!IsExtended(hand, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleTip)) return false;
        if (!IsExtended(hand, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingTip)) return false;
        if (!IsExtended(hand, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleTip)) return false;
        if (!hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose indexProx)) return false;
        if (!hand.GetJoint(XRHandJointID.LittleProximal).TryGetPose(out Pose littleProx)) return false;

        Vector3 palmNormal = Vector3.Cross(middleProx.position - wrist.position, indexProx.position - littleProx.position);
        if (handedness == Handedness.Right) palmNormal = -palmNormal;
        if (palmNormal.sqrMagnitude < 1e-6f) return false;
        palmNormal.Normalize();

        if (_xrOriginTransform == null) { var o = FindObjectOfType<XROrigin>(); if (o != null) _xrOriginTransform = o.transform; }
        if (_headCamera == null) _headCamera = Camera.main;
        if (_headCamera == null) return false;

        Vector3 palmWorld = wrist.position;
        Vector3 normalWorld = palmNormal;
        if (_xrOriginTransform != null)
        {
            palmWorld = _xrOriginTransform.TransformPoint(palmWorld);
            normalWorld = _xrOriginTransform.TransformDirection(normalWorld);
        }

        Vector3 toHead = _headCamera.transform.position - palmWorld;
        if (toHead.sqrMagnitude < 1e-6f) return false;
        return Vector3.Dot(normalWorld, toHead.normalized) > palmFacingDotThreshold;
    }

    private bool IsExtended(XRHand hand, XRHandJointID proximal, XRHandJointID intermediate, XRHandJointID tip)
    {
        if (!hand.GetJoint(proximal).TryGetPose(out Pose p)) return false;
        if (!hand.GetJoint(intermediate).TryGetPose(out Pose m)) return false;
        if (!hand.GetJoint(tip).TryGetPose(out Pose t)) return false;
        Vector3 a = m.position - p.position;
        Vector3 b = t.position - m.position;
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return false;
        return Vector3.Dot(a.normalized, b.normalized) > fingerStraightDotThreshold;
    }
}
