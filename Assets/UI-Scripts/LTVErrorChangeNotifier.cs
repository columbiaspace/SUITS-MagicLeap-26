using System.Collections.Generic;
using TssApi;
using UnityEngine;

/// LTV-scene-only observer: chimes when the TSS LTV error set gains or loses codes.
/// Does NOT mutate the priority queue — LtvInstructionService already inserts new
/// errors (without preempting) and drives resolution. Reads cached poller data only,
/// so it adds no extra TSS fetch.
public class LTVErrorChangeNotifier : MonoBehaviour
{
    [SerializeField] private TssUnityApiService tssPoller;    // falls back to TssUnityApiService.Instance
    [SerializeField] private AudioClip newErrorChime;
    [SerializeField] private AudioClip resolvedErrorChime;
    [SerializeField] private AudioSource newErrorSource;      // created at runtime if unassigned
    [SerializeField] private AudioSource resolvedErrorSource; // created at runtime if unassigned
    [Tooltip("Diff cadence; match the TSS poller interval (default 1s). Reads cached data, no fetch.")]
    [SerializeField] private float checkIntervalSeconds = 1f;
    [Tooltip("Delay on the resolved chime when both fire in one poll, so they don't overlap.")]
    [SerializeField] private float staggerSeconds = 0.3f;

    private readonly HashSet<string> _previousCodes = new HashSet<string>();
    private bool _baselineSet;
    private bool _warnedNoPoller;
    private float _timer;

    private void Awake()
    {
        if (tssPoller == null) tssPoller = TssUnityApiService.Instance;
        newErrorSource = EnsureSource(newErrorSource);
        resolvedErrorSource = EnsureSource(resolvedErrorSource);
        if (newErrorChime == null) newErrorChime = SilentPlaceholder("new_error_chime");
        if (resolvedErrorChime == null) resolvedErrorChime = SilentPlaceholder("resolved_error_chime");
    }

    private void Update()
    {
        if (tssPoller == null)
        {
            tssPoller = TssUnityApiService.Instance; // poller may init later (bootstrap order)
            if (tssPoller == null)
            {
                if (!_warnedNoPoller)
                {
                    Debug.LogWarning("[LTVChime] No TSS poller assigned or found; notifier idle.", this);
                    _warnedNoPoller = true;
                }
                return;
            }
        }

        _timer += Time.deltaTime;
        if (_timer < checkIntervalSeconds) return;
        _timer = 0f;

        if (!IsPollerOnline()) return; // wait for a real poll before baselining

        HashSet<string> current = CollectActiveCodes();

        if (!_baselineSet)
        {
            _previousCodes.UnionWith(current); // first real poll = baseline, no chime
            _baselineSet = true;
            return;
        }

        bool anyNew = false;
        foreach (string c in current) if (!_previousCodes.Contains(c)) { anyNew = true; break; }
        bool anyResolved = false;
        foreach (string c in _previousCodes) if (!current.Contains(c)) { anyResolved = true; break; }

        if (anyNew) Play(newErrorSource, newErrorChime, 0f);
        if (anyResolved) Play(resolvedErrorSource, resolvedErrorChime, anyNew ? staggerSeconds : 0f);

        _previousCodes.Clear();
        _previousCodes.UnionWith(current);
    }

    private HashSet<string> CollectActiveCodes()
    {
        var set = new HashSet<string>();
        List<Dictionary<string, object>> list = tssPoller.GetLtvErrorProcedures();
        if (list == null) return set;
        foreach (Dictionary<string, object> raw in list)
        {
            if (raw == null) continue;
            if (!raw.TryGetValue("needs_resolved", out object nr) || !IsTrue(nr)) continue;
            string code = raw.TryGetValue("code", out object c) ? c?.ToString() : null;
            if (!string.IsNullOrEmpty(code)) set.Add(code);
        }
        return set;
    }

    private bool IsPollerOnline()
    {
        Dictionary<string, object> health = tssPoller.GetHealth();
        return health != null && health.TryGetValue("source_online", out object v) && IsTrue(v);
    }

    private void Play(AudioSource source, AudioClip clip, float delay)
    {
        if (source == null || clip == null)
        {
            Debug.LogWarning("[LTVChime] missing AudioSource or AudioClip; skipping chime.", this);
            return;
        }
        source.clip = clip;
        if (delay > 0f) source.PlayDelayed(delay); else source.Play();
    }

    private AudioSource EnsureSource(AudioSource s)
    {
        if (s == null) s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = false;
        s.spatialBlend = 0f; // 2D UI chime
        return s;
    }

    private static AudioClip SilentPlaceholder(string clipName)
    {
        Debug.LogWarning($"[LTVChime] {clipName} not assigned — using silent placeholder. " +
                         $"Drop the real clip at Assets/Audio/{clipName}.wav and assign it in the inspector.");
        return AudioClip.Create(clipName, 1, 1, 44100, false);
    }

    private static bool IsTrue(object v)
    {
        if (v is bool b) return b;
        if (v is string s)
        {
            if (bool.TryParse(s, out bool pb)) return pb;
            return double.TryParse(s, out double pd) && pd != 0d;
        }
        if (v == null) return false;
        return double.TryParse(v.ToString(), out double d) && d != 0d;
    }
}
