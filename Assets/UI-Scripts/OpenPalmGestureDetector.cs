using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;

public class OpenPalmGestureDetector : MonoBehaviour
{
    [SerializeField] private Camera userCamera;
    [SerializeField] private float palmFacingDot = 0.7f;
    [SerializeField] private float fingerExtendedDistance = 0.13f;
    [SerializeField] private float holdSeconds = 0.3f;
    [SerializeField] private float releaseSeconds = 0.5f;

    public UnityEvent OnGestureTriggered;

    private static readonly List<XRHandSubsystem> _cache = new List<XRHandSubsystem>();
    private XRHandSubsystem _hands;

    private enum State { Idle, Holding, Cooldown }
    private State _state;
    private float _timeInState;
    private float _fingerExtendedSqr;

    private void Awake()
    {
        _fingerExtendedSqr = fingerExtendedDistance * fingerExtendedDistance;
        if (OnGestureTriggered == null) OnGestureTriggered = new UnityEvent();
    }

    private void Update()
    {
        if (_hands == null || !_hands.running)
        {
            SubsystemManager.GetSubsystems(_cache);
            _hands = null;
            for (int i = 0; i < _cache.Count; i++) if (_cache[i].running) { _hands = _cache[i]; break; }
            if (_hands == null) return;
        }

        if (userCamera == null) userCamera = Camera.main;
        if (userCamera == null) return;

        bool active = IsOpenPalmFacingUser(_hands.leftHand) || IsOpenPalmFacingUser(_hands.rightHand);

        switch (_state)
        {
            case State.Idle:
                if (active) { _state = State.Holding; _timeInState = 0f; }
                break;
            case State.Holding:
                if (!active) { _state = State.Idle; break; }
                _timeInState += Time.deltaTime;
                if (_timeInState >= holdSeconds)
                {
                    OnGestureTriggered?.Invoke();
                    _state = State.Cooldown;
                    _timeInState = 0f;
                }
                break;
            case State.Cooldown:
                if (active) { _timeInState = 0f; }
                else
                {
                    _timeInState += Time.deltaTime;
                    if (_timeInState >= releaseSeconds) _state = State.Idle;
                }
                break;
        }
    }

    private bool IsOpenPalmFacingUser(XRHand hand)
    {
        if (!hand.isTracked) return false;
        if (!TryPos(hand, XRHandJointID.Wrist, out Vector3 wrist)) return false;
        if (!TryPos(hand, XRHandJointID.IndexMetacarpal, out Vector3 indexBase)) return false;
        if (!TryPos(hand, XRHandJointID.LittleMetacarpal, out Vector3 littleBase)) return false;

        Vector3 wristToIndex = indexBase - wrist;
        Vector3 wristToLittle = littleBase - wrist;
        Vector3 normal = hand.handedness == Handedness.Right
            ? Vector3.Cross(wristToLittle, wristToIndex).normalized
            : Vector3.Cross(wristToIndex, wristToLittle).normalized;

        Vector3 palmCenter = (indexBase + littleBase + wrist) * (1f / 3f);
        Vector3 toCamera = (userCamera.transform.position - palmCenter).normalized;
        if (Vector3.Dot(normal, toCamera) < palmFacingDot) return false;

        return Extended(hand, XRHandJointID.IndexTip, wrist)
            && Extended(hand, XRHandJointID.MiddleTip, wrist)
            && Extended(hand, XRHandJointID.RingTip, wrist)
            && Extended(hand, XRHandJointID.LittleTip, wrist);
    }

    private bool Extended(XRHand hand, XRHandJointID tipId, Vector3 wrist)
    {
        return TryPos(hand, tipId, out Vector3 tip) && (tip - wrist).sqrMagnitude >= _fingerExtendedSqr;
    }

    private static bool TryPos(XRHand hand, XRHandJointID id, out Vector3 pos)
    {
        if (hand.GetJoint(id).TryGetPose(out Pose pose)) { pos = pose.position; return true; }
        pos = default;
        return false;
    }
}
