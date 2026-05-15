using System;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays the current wall-clock time and mission elapsed time.
/// Attach to the TimeDisplay object above the CompassHud.
/// Wire clockText and elapsedText in the Inspector.
/// </summary>
public class MissionTimeHUD : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI clockText;
    [SerializeField] private TextMeshProUGUI elapsedText;

    private float _startTime;

    private void OnEnable()
    {
        _startTime = Time.realtimeSinceStartup;
    }

    private void Update()
    {
        if (clockText != null)
            clockText.text = DateTime.Now.ToString("HH:mm:ss");

        if (elapsedText != null)
        {
            float elapsed = Time.realtimeSinceStartup - _startTime;
            int h = (int)(elapsed / 3600);
            int m = (int)((elapsed % 3600) / 60);
            int s = (int)(elapsed % 60);
            elapsedText.text = $"T+ {h:D2}:{m:D2}:{s:D2}";
        }
    }
}
