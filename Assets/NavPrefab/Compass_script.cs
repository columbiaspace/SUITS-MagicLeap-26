using TssApi;
using UnityEngine;

public class Compass_script : MonoBehaviour
{
    [Header("TSS")]
    [SerializeField] private TssUnityApiService tssApi;
    [Tooltip("Key inside the imu bucket — must match what TSS sends (e.g. ev1)")]
    [SerializeField] private string evaId = "eva1";

    [Header("Compass")]
    public RectTransform NorthArrow;

    [Header("Debug")]
    [Tooltip("When true, shows live TSS heading. Disable to use the override below.")]
    [SerializeField] private bool useTssHeading = true;
    [SerializeField] private float debugHeading = 0f;
    [Tooltip("How often (seconds) to print the heading to the Console. 0 = every frame.")]
    [SerializeField] private float logIntervalSeconds = 1f;

    private float _currentHeading = 0f;
    private float _logTimer = 0f;

    private void Awake()
    {
        // Always prefer the persistent singleton — see ARMinimapErica.Awake comment.
        if (TssUnityApiService.Instance != null) tssApi = TssUnityApiService.Instance;
        if (tssApi == null) tssApi = FindObjectOfType<TssUnityApiService>();

        if (tssApi == null)
            Debug.LogError("[Compass] No TssUnityApiService found — assign it in the Inspector.");
    }

    private void Update()
    {
        if (useTssHeading && tssApi != null)
            _currentHeading = ReadTssHeading();
        else
            _currentHeading = debugHeading;

        // TSS heading: 0 = North, 90 = East, 180 = South, 270 = West.
        // Negate so the arrow rotates opposite to the heading
        // (arrow points toward north as the player turns away from it).
        if (NorthArrow != null)
            NorthArrow.localEulerAngles = new Vector3(0f, 0f, -_currentHeading);

        _logTimer += Time.deltaTime;
        if (_logTimer >= logIntervalSeconds)
        {
            _logTimer = 0f;
            Debug.Log($"[Compass] heading={_currentHeading:F1}°  arrow_z={-_currentHeading:F1}°  source={(useTssHeading ? "TSS" : "debug")}");
        }
    }

    private float ReadTssHeading()
    {
        if (tssApi != null && tssApi.TryGetImuHeading(evaId, out float heading))
            return heading;

        return _currentHeading;
    }
}
