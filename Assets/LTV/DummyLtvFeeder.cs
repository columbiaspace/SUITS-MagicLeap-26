using System;
using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;

// Local test rig for the LTV priority queue and instruction flow. Enable this
// GameObject to bypass TSS UDP, drive _ltv with hardcoded errors, and auto-flip
// state to "resolved" once the service enters verification. Disable before
// running against a real TSS server.
public class DummyLtvFeeder : MonoBehaviour
{
    [SerializeField] private TssUnityApiService tssApi;
    [SerializeField] private LtvInstructionService ltvService;
    [Tooltip("Seconds to wait after verification begins before flipping the TSS state to 'resolved.'")]
    [SerializeField] private float autoResolveDelay = 0.8f;
    [Tooltip("Manual override key. Press while verifying to immediately resolve the current error at the dummy TSS.")]
    [SerializeField] private KeyCode forceResolveKey = KeyCode.V;

    private readonly List<DummyError> _errors = new List<DummyError>();
    private string _autoResolvePending;

    private void Awake()
    {
        BuildErrors();
    }

    private void OnEnable()
    {
        // Always prefer the persistent singleton. When LTV is entered via Mission,
        // this scene's local TssUnityApiService Awake'd, called Destroy(this), and is
        // flagged-but-not-yet-destroyed — the serialized ref still passes ==null, so
        // we'd otherwise write dummy data into a doomed component while
        // LtvInstructionService reads from Mission's singleton. Mismatch = headset
        // shows zero errors. Editor (LTV opened directly) didn't hit this because
        // LTV's local instance was the singleton.
        TssUnityApiService persistent = TssUnityApiService.Instance;
        if (persistent != null) tssApi = persistent;
        else if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();
        if (ltvService == null) ltvService = FindObjectOfType<LtvInstructionService>();

        if (tssApi == null || ltvService == null)
        {
            Debug.LogError("[DummyLtv] Missing tssApi or ltvService reference. Disabling.", this);
            enabled = false;
            return;
        }

        tssApi.SkipUdpPolling = true;
        PushSnapshot();
        ltvService.InstructionUpdated += OnInstructionUpdated;
        Debug.Log($"[DummyLtv] Active. {_errors.Count} dummy errors loaded; UDP polling paused. Press {forceResolveKey} during verify to force-resolve.", this);
    }

    private void OnDisable()
    {
        if (ltvService != null) ltvService.InstructionUpdated -= OnInstructionUpdated;
        if (tssApi != null) tssApi.SkipUdpPolling = false;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(forceResolveKey)) return;
        if (ltvService != null && ltvService.IsVerifying && ltvService.CurrentError != null)
        {
            ResolveErrorByCode(ltvService.CurrentError.Code);
        }
    }

    private void OnInstructionUpdated(Dictionary<string, object> snapshot)
    {
        if (snapshot == null) return;
        bool verifying = snapshot.TryGetValue("verifying", out object v) && v is bool b && b;
        string code = snapshot.TryGetValue("error_code", out object c) ? Convert.ToString(c) : null;
        if (verifying && !string.IsNullOrEmpty(code) && _autoResolvePending != code)
        {
            _autoResolvePending = code;
            StartCoroutine(AutoResolveAfter(autoResolveDelay, code));
        }
    }

    private IEnumerator AutoResolveAfter(float delay, string code)
    {
        yield return new WaitForSeconds(delay);
        if (_autoResolvePending == code) _autoResolvePending = null;
        if (ltvService != null && ltvService.IsVerifying && ltvService.CurrentError != null && ltvService.CurrentError.Code == code)
        {
            ResolveErrorByCode(code);
        }
    }

    private void ResolveErrorByCode(string code)
    {
        for (int i = 0; i < _errors.Count; i++)
        {
            if (_errors[i].Code == code)
            {
                _errors[i].NeedsResolved = false;
                break;
            }
        }
        PushSnapshot();
        Debug.Log($"[DummyLtv] Resolved {code}; refreshed TSS snapshot.", this);
    }

    private void PushSnapshot()
    {
        List<object> procedures = new List<object>();
        Dictionary<string, object> errorBools = new Dictionary<string, object>();
        for (int i = 0; i < _errors.Count; i++)
        {
            DummyError e = _errors[i];
            if (e.NeedsResolved)
            {
                List<object> steps = new List<object>(e.Procedures.Count);
                for (int s = 0; s < e.Procedures.Count; s++) steps.Add(e.Procedures[s]);
                procedures.Add(new Dictionary<string, object>
                {
                    { "code", e.Code },
                    { "description", e.Description },
                    { "needs_resolved", true },
                    { "procedures", steps }
                });
            }
            errorBools[e.ErrorKey] = e.NeedsResolved;
        }

        tssApi.OverrideLtvData(new Dictionary<string, object>
        {
            { "error_procedures", procedures },
            { "errors", errorBools }
        });
    }

    // 4800 ERM is listed LAST on purpose. The ERM-forced-first priority patch
    // in LtvError must still pop it before 4968/4509/1969.
    private void BuildErrors()
    {
        _errors.Clear();
        _errors.Add(new DummyError("4509", "nav_system", "NAV Restart & Manual Return to Home", new List<string>
        {
            "1. Confirm NAV system error is active.",
            "2. Initiate NAV restart from the control panel.",
            "3. Wait for boot-complete indicator.",
            "4. Verify NAV has returned to nominal."
        }));
        _errors.Add(new DummyError("1969", "fuse", "Backup Fuse Error", new List<string>
        {
            "1. Locate the backup fuse panel.",
            "2. Inspect and replace the burnt fuse.",
            "3. Reset the power switch."
        }));
        _errors.Add(new DummyError("4968", "power_subsystem_bus", "Subsystem Power Bus Error", new List<string>
        {
            "1. Check bus voltage on the main panel.",
            "2. Engage the backup relay.",
            "3. Verify downstream loads are powered."
        }));
        _errors.Add(new DummyError("4800", "recovery_mode", "Exit Recovery Mode (ERM)", new List<string>
        {
            "1. Confirm recovery-mode condition is present.",
            "2. Stabilize attitude using forward thrusters.",
            "3. Verify communications uplink is stable.",
            "4. Announce ERM success."
        }));
    }

    [Serializable]
    private class DummyError
    {
        public string Code;
        public string ErrorKey;
        public string Description;
        public List<string> Procedures;
        public bool NeedsResolved;

        public DummyError(string code, string errorKey, string description, List<string> procedures)
        {
            Code = code; ErrorKey = errorKey; Description = description; Procedures = procedures; NeedsResolved = true;
        }
    }
}
