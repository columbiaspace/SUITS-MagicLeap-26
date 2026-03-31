using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;
using UnityEngine.UI;

public class TestTSS : MonoBehaviour
{
    [Header("TSS")]
    [SerializeField] private TssUnityApiService tssApi;

    [Header("EVA telemetry paths (under telemetry.{evaId})")]
    [SerializeField] private string evaId = "eva1";
    [SerializeField] private string posXKey = "pos_x";
    [SerializeField] private string posYKey = "pos_y";
    [SerializeField] private string headingKey = "heading";

    [Header("Display")]
    [SerializeField] private Text displayText;
    [SerializeField] private float updateIntervalSeconds = 1f;

    private Coroutine _pollRoutine;

    private void Awake()
    {
        if (tssApi == null)
        {
            tssApi = TssUnityApiService.Instance;
        }

        if (tssApi == null)
        {
            tssApi = FindObjectOfType<TssUnityApiService>();
        }
    }

    private void OnEnable()
    {
        if (_pollRoutine == null)
        {
            _pollRoutine = StartCoroutine(PollEvaPoseRoutine());
        }
    }

    private void OnDisable()
    {
        if (_pollRoutine != null)
        {
            StopCoroutine(_pollRoutine);
            _pollRoutine = null;
        }
    }

    private IEnumerator PollEvaPoseRoutine()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.05f, updateIntervalSeconds));
        while (true)
        {
            RefreshDisplay();
            yield return wait;
        }
    }

    private void RefreshDisplay()
    {
        if (tssApi == null)
        {
            return;
        }

        string id = string.IsNullOrWhiteSpace(evaId) ? "eva1" : evaId.Trim().ToLowerInvariant();
        Dictionary<string, object> eva = tssApi.GetEva();

        double posX = ReadTelemetryField(eva, id, posXKey);
        double posY = ReadTelemetryField(eva, id, posYKey);
        double heading = ReadTelemetryField(eva, id, headingKey);

        string line = $"EVA {id}: pos_x={posX:F3}, pos_y={posY:F3}, heading={heading:F2}°";

        if (displayText != null)
        {
            displayText.text = line;
        }

        Debug.Log("[TestTSS] " + line);
    }

    private static double ReadTelemetryField(Dictionary<string, object> evaPacket, string evaIdValue, string fieldKey)
    {
        if (string.IsNullOrEmpty(fieldKey))
        {
            return 0d;
        }

        object raw = GetPath(evaPacket, $"telemetry.{evaIdValue}.{fieldKey}");
        return ToDouble(raw);
    }

    private static object GetPath(Dictionary<string, object> source, string path)
    {
        if (source == null || string.IsNullOrEmpty(path))
        {
            return null;
        }

        object current = source;
        foreach (string part in path.Split('.'))
        {
            if (!(current is Dictionary<string, object> dict) || !dict.TryGetValue(part, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static double ToDouble(object value)
    {
        if (value == null)
        {
            return 0d;
        }

        if (value is double d)
        {
            return d;
        }

        if (value is float f)
        {
            return f;
        }

        if (value is int i)
        {
            return i;
        }

        if (value is long l)
        {
            return l;
        }

        if (value is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
        {
            return parsed;
        }

        try
        {
            return System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0d;
        }
    }
}
