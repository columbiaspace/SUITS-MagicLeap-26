using System.Collections;
using System.Collections.Generic;
using TssApi;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the DSU panel display.
///
/// Phase 1 – UIA in progress: shows the default DCU overview image.
/// Phase 2 – All UIA steps complete: walks through the ordered DCU procedure
///            images (battery → oxy → battery → fan → pump → co2), advancing
///            each step once the corresponding TSS API field reaches its target.
/// Phase 3 – After the last DCU condition (CO₂ scrubber on) is true, returns to the
///            default DCU overview image.
/// </summary>
[RequireComponent(typeof(Image))]
public class DsuPanelUI : MonoBehaviour
{
    [Header("Panel Image Component")]
    [SerializeField] private Image displayImage;

    [Header("Default image shown while UIA is in progress")]
    [SerializeField] private Texture2D defaultDcuTexture;

    [Header("DCU procedure step images (in order)")]
    [SerializeField] private Texture2D dcuBatteryTexture;   // slide 0 – battery (set to UMB)
    [SerializeField] private Texture2D dcuOxyTexture;       // slide 1 – oxygen
    [SerializeField] private Texture2D dcuBattery2Texture;  // slide 2 – battery (switch to local)
    [SerializeField] private Texture2D dcuFanTexture;       // slide 3 – fan
    [SerializeField] private Texture2D dcuPumpTexture;      // slide 4 – pump
    [SerializeField] private Texture2D dcuCo2Texture;       // slide 5 – CO2

    [Header("UIA dependency — DSU stays at default until this carousel completes")]
    [SerializeField] private ImageCarouselUI uiaCarousel;

    [Header("TSS API Source")]
    [SerializeField] private TssUnityApiService tssApi;

    [SerializeField] private float syncIntervalSeconds = 0.2f;

    [Header("Debug")]
    [Tooltip("Skip UIA completion check and jump straight to DCU procedure — for testing only")]
    [SerializeField] private bool debugBypassUiaCheck = false;

    // ── UIA completion paths (all must be true) ──────────────────────────────
    // status.started intentionally omitted — react directly to UIA switch values
    private static readonly string[] UiaRequiredPaths =
    {
        "uia.eva1_power",
        "uia.eva1_oxy",
        "uia.eva1_water_supply",
        "uia.eva1_water_waste",
    };

    // ── DCU step definitions ─────────────────────────────────────────────────
    // 6 slides; 5 advance-rules (one between each consecutive pair).
    // Once rule[i] is satisfied the panel moves from slide i to slide i+1.
    // Rule 0 is special-cased: advance when EVA1 battery is active (lu or ps) per /api/v1/dcu/eva1 shape.
    private static readonly string[] DcuAdvancePaths =
    {
        "dcu.eva1.batt",  // slide 0→1: batt.lu || batt.ps
        "dcu.eva1.oxy",   // slide 1→2: oxygen on → advance
        "dcu.eva1.batt",  // slide 2→3: battery switched to local (ps=true) → advance
        "dcu.eva1.fan",   // slide 3→4: fan on → advance
        "dcu.eva1.pump",  // slide 4→5: pump on → advance
    };

    // Expected *bool* value of the TSS field that signals the step is done.
    // Rule 0 uses Eva1BatteryActive(); batt rule 2 expects "ps" == true (local).
    private static readonly string[] DcuAdvanceSubKeys =
    {
        null,  // unused — rule 0 handled in DcuAdvanceSatisfied
        null,  // dcu.eva1.oxy     == true  → done with slide 1
        "ps",  // dcu.eva1.batt.ps == true  → done with slide 2
        null,  // dcu.eva1.fan     == true  → done with slide 3
        null,  // dcu.eva1.pump    == true  → done with slide 4
    };

    private Sprite[] _slideSprites;   // 7 entries: [0]=default, [1‑6]=procedure steps
    private bool _uiaComplete;
    private bool _pendingDcuBatteryIntro;
    private int _dcuStep;             // 0‑5 within the DCU procedure
    private Coroutine _syncCoroutine;
    private string _debugText = "DSU: waiting for data…";
    private float _debugLogTimer;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (displayImage == null)
            displayImage = GetComponent<Image>();

        if (tssApi == null)
            tssApi = TssUnityApiService.Instance ?? FindObjectOfType<TssUnityApiService>();

        BuildSprites();
        _dcuStep = 0;
        _uiaComplete = false;
        ShowDefault();
    }

    private void OnEnable()
    {
        if (tssApi == null) return;
        tssApi.EvaUpdated += OnPacketUpdated;
        _syncCoroutine = StartCoroutine(SyncLoop());
    }

    private void OnDisable()
    {
        if (_syncCoroutine != null)
        {
            StopCoroutine(_syncCoroutine);
            _syncCoroutine = null;
        }

        if (tssApi != null)
            tssApi.EvaUpdated -= OnPacketUpdated;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void BuildSprites()
    {
        _slideSprites = new Sprite[]
        {
            MakeSprite(defaultDcuTexture),   // index 0 – default overview
            MakeSprite(dcuBatteryTexture),   // index 1 – battery (UMB)
            MakeSprite(dcuOxyTexture),       // index 2 – oxygen
            MakeSprite(dcuBattery2Texture),  // index 3 – battery (local)
            MakeSprite(dcuFanTexture),       // index 4 – fan
            MakeSprite(dcuPumpTexture),      // index 5 – pump
            MakeSprite(dcuCo2Texture),       // index 6 – CO2
        };
    }

    private static Sprite MakeSprite(Texture2D tex)
    {
        if (tex == null) return null;
        return Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    private IEnumerator SyncLoop()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.05f, syncIntervalSeconds));
        while (true)
        {
            Evaluate(tssApi?.GetEva());
            yield return wait;
        }
    }

    private void OnPacketUpdated(Dictionary<string, object> packet) => Evaluate(packet);

    private void Evaluate(Dictionary<string, object> eva)
    {
        if (eva == null || eva.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        bool uiaDone = debugBypassUiaCheck || (uiaCarousel != null ? uiaCarousel.IsComplete : AllUiaStepsDone(eva));

        // ── Phase 1: show default until UIA is finished ───────────────────────
        if (!_uiaComplete)
        {
            if (uiaDone)
            {
                _pendingDcuBatteryIntro = true;
                _uiaComplete = true;
                _dcuStep = 0;
                Debug.Log("[DSU] UIA complete — entering DCU procedure");
            }
            else
            {
                sb.AppendLine("[DSU] Phase 1 — waiting for UIA:");
                foreach (string path in UiaRequiredPaths)
                {
                    bool ok = GetBoolVal(eva, path);
                    sb.AppendLine($"  [{(ok ? "✓" : "✗")}] {path}");
                }
                sb.Append("  → showing default DCU image");
                UpdateDebug(sb.ToString());
                ShowDefault();
                return;
            }
        }

        // ── Phase 2a: battery intro slide ─────────────────────────────────────
        if (_pendingDcuBatteryIntro)
        {
            bool battActive = Eva1BatteryActive(eva);
            sb.AppendLine("[DSU] Phase 2 — battery intro");
            sb.AppendLine($"  batt.lu={GetBoolVal(eva, "dcu.eva1.batt.lu")}  batt.ps={GetBoolVal(eva, "dcu.eva1.batt.ps")}  active={battActive}");
            sb.Append("  → slide 1 (dcu-battery)");
            UpdateDebug(sb.ToString());
            ApplySlide(1);
            if (battActive)
                _pendingDcuBatteryIntro = false;
            return;
        }

        // ── Phase 2b: DCU step sequence ───────────────────────────────────────
        int completed = 0;
        for (int i = 0; i < DcuAdvancePaths.Length; i++)
        {
            if (DcuAdvanceSatisfied(i, eva)) completed++;
            else break;
        }

        GetBool(eva, "dcu.eva1.co2", out bool co2On);
        bool allDone = completed >= DcuAdvancePaths.Length;

        sb.AppendLine("[DSU] Phase 2 — DCU sequence:");
        sb.AppendLine($"  batt.lu={GetBoolVal(eva, "dcu.eva1.batt.lu")} batt.ps={GetBoolVal(eva, "dcu.eva1.batt.ps")}");
        sb.AppendLine($"  oxy={GetBoolVal(eva, "dcu.eva1.oxy")}  fan={GetBoolVal(eva, "dcu.eva1.fan")}  pump={GetBoolVal(eva, "dcu.eva1.pump")}  co2={co2On}");
        sb.Append($"  completed={completed}/{DcuAdvancePaths.Length}");

        if (allDone && co2On)
        {
            sb.Append("  → all done, back to default");
            UpdateDebug(sb.ToString());
            ShowDefault();
            return;
        }

        _dcuStep = Mathf.Clamp(completed, 0, 5);
        sb.Append($"  → slide {_dcuStep + 1}");
        UpdateDebug(sb.ToString());
        ApplySlide(_dcuStep + 1);
    }

    private void UpdateDebug(string text)
    {
        _debugText = text;
        _debugLogTimer -= Time.deltaTime;
        if (_debugLogTimer <= 0f)
        {
            Debug.Log(text);
            _debugLogTimer = 1f;
        }
    }

    private static bool GetBoolVal(Dictionary<string, object> eva, string path)
        => GetBool(eva, path, out bool v) && v;

    private void ShowDefault() => ApplySlide(0);

    private void ApplySlide(int index)
    {
        if (displayImage == null) return;
        Sprite s = (_slideSprites != null && index < _slideSprites.Length)
            ? _slideSprites[index]
            : null;

        displayImage.sprite = s;
        displayImage.enabled = s != null;
        displayImage.preserveAspect = false;
    }


    // ── TSS helpers ───────────────────────────────────────────────────────────

    private static bool AllUiaStepsDone(Dictionary<string, object> eva)
    {
        foreach (string path in UiaRequiredPaths)
        {
            if (!GetBool(eva, path, out bool v) || !v)
                return false;
        }
        return true;
    }

    private static bool DcuAdvanceSatisfied(int ruleIndex, Dictionary<string, object> eva)
    {
        if (ruleIndex == 0)
        {
            return Eva1BatteryActive(eva);
        }

        string basePath = DcuAdvancePaths[ruleIndex];
        string subKey   = DcuAdvanceSubKeys[ruleIndex];

        string fullPath = subKey == null ? basePath : basePath + "." + subKey;
        return GetBool(eva, fullPath, out bool v) && v;
    }

    /// <summary>True when dcu.eva1.batt.lu or dcu.eva1.batt.ps is true (EVA1 battery path active).</summary>
    private static bool Eva1BatteryActive(Dictionary<string, object> eva)
    {
        bool lu = GetBool(eva, "dcu.eva1.batt.lu", out bool l) && l;
        bool ps = GetBool(eva, "dcu.eva1.batt.ps", out bool p) && p;
        return lu || ps;
    }

    private static bool GetBool(Dictionary<string, object> source, string path, out bool value)
    {
        value = false;
        if (source == null || string.IsNullOrEmpty(path)) return false;

        object current = source;
        foreach (string key in path.Split('.'))
        {
            if (current is not Dictionary<string, object> dict || !dict.TryGetValue(key, out current))
                return false;
        }

        if (current is bool b)      { value = b; return true; }
        if (current is string s && bool.TryParse(s, out bool pb)) { value = pb; return true; }
        if (current is System.IConvertible c)
        {
            try { value = System.Math.Abs(c.ToDouble(null)) > double.Epsilon; return true; }
            catch { /* fall through */ }
        }
        return false;
    }
}
