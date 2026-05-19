using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-scene HUD visibility toggle. Drop one in each non-Starter scene; drag the
/// widget roots that should hide/show together into <see cref="hudElements"/>.
/// Safety-critical alerts (low O₂, depress, suit failure) must NOT be added.
/// </summary>
public class HUDVisibilityController : MonoBehaviour
{
    [SerializeField] private List<GameObject> hudElements = new List<GameObject>();
    [SerializeField] private OpenPalmGestureDetector gestureDetector;

    private bool _isVisible = true;
    private readonly HashSet<int> _warnedNullIndices = new HashSet<int>();

    private void OnEnable()
    {
        if (gestureDetector != null) gestureDetector.OnGestureTriggered.AddListener(Toggle);
        else Debug.LogWarning($"[HUD] {name}: gestureDetector not assigned; gesture toggle disabled here.", this);

        VoiceIntents.HudShowDisplayRequested += Show;
        VoiceIntents.HudClearDisplayRequested += Hide;
    }

    private void OnDisable()
    {
        if (gestureDetector != null) gestureDetector.OnGestureTriggered.RemoveListener(Toggle);
        VoiceIntents.HudShowDisplayRequested -= Show;
        VoiceIntents.HudClearDisplayRequested -= Hide;
    }

    public void Toggle() { _isVisible = !_isVisible; Apply(); }
    public void Show() { if (_isVisible) return; _isVisible = true; Apply(); }
    public void Hide() { if (!_isVisible) return; _isVisible = false; Apply(); }

    /// <summary>Override for critical safety alerts. Logged so an unexpected reveal is traceable.</summary>
    public void ForceShow()
    {
        Debug.Log($"[HUD] ForceShow on {name} (was {(_isVisible ? "visible" : "hidden")}).", this);
        _isVisible = true;
        Apply();
    }

    private void Apply()
    {
        for (int i = 0; i < hudElements.Count; i++)
        {
            GameObject el = hudElements[i];
            if (el == null)
            {
                if (_warnedNullIndices.Add(i))
                    Debug.LogWarning($"[HUD] {name}: hudElements[{i}] is null — skipping. Re-assign in Inspector.", this);
                continue;
            }
            el.SetActive(_isVisible);
        }
    }
}
