using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

/// LTV-scene-only voice-summoned reference map. Subscribes to the persistent
/// VoiceIntents static events (same API as HUDVisibilityController). The map
/// lives outside HUDRoot, so the HUD toggle does not affect it.
public class LTVReferenceMapController : MonoBehaviour
{
    [Header("Map")]
    [Tooltip("Sprite shown on the map. If null, loaded from Resources/LTVReferenceMap.")]
    public Sprite mapSprite;
    [Tooltip("World-space width and height of the map quad, in meters.")]
    public Vector2 worldSize = new Vector2(0.5f, 0.4f);
    [Tooltip("Distance in front of the camera at which the map first spawns.")]
    public float spawnDistanceMeters = 1.0f;

    private GameObject _map;

    private void OnEnable()
    {
        VoiceIntents.LtvReferenceMapShowRequested += Show;
        VoiceIntents.LtvReferenceMapHideRequested += Hide;
    }

    private void OnDisable()
    {
        VoiceIntents.LtvReferenceMapShowRequested -= Show;
        VoiceIntents.LtvReferenceMapHideRequested -= Hide;
    }

    private void Start()
    {
        if (mapSprite == null) mapSprite = Resources.Load<Sprite>("LTVReferenceMap");
        if (mapSprite == null)
            Debug.LogWarning("[LTVMap] No mapSprite assigned and Resources/LTVReferenceMap not found; map will render blank.");
    }

    private void OnDestroy() { if (_map != null) Destroy(_map); }

    public void Show()
    {
        if (_map != null && _map.activeSelf) return;
        if (_map == null) _map = BuildMap();
        PlaceInFrontOfCamera(_map.transform);
        _map.SetActive(true);
    }

    public void Hide() { if (_map != null) _map.SetActive(false); }

    private GameObject BuildMap()
    {
        var root = new GameObject("LTVReferenceMap",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1024f, 819f);
        rt.localScale = new Vector3(worldSize.x / rt.sizeDelta.x, worldSize.y / rt.sizeDelta.y, 1f);

        var imageGo = new GameObject("Image", typeof(RectTransform), typeof(Image));
        var imageRt = (RectTransform)imageGo.transform;
        imageRt.SetParent(root.transform, false);
        imageRt.anchorMin = Vector2.zero; imageRt.anchorMax = Vector2.one;
        imageRt.offsetMin = imageRt.offsetMax = Vector2.zero;
        var img = imageGo.GetComponent<Image>();
        img.sprite = mapSprite;
        img.preserveAspect = true;

        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(rt.sizeDelta.x, rt.sizeDelta.y, 1f);
        var rb = root.AddComponent<Rigidbody>();
        rb.useGravity = false; rb.isKinematic = true;

        root.AddComponent<XRGrabInteractable>();
        root.AddComponent<AdjustableFollowUI>();

        root.SetActive(false);
        return root;
    }

    private void PlaceInFrontOfCamera(Transform mapXform)
    {
        Transform cam = Camera.main != null ? Camera.main.transform : null;
        if (cam == null)
        {
            Debug.LogWarning("[LTVMap] Camera.main not found; spawning at world origin.");
            mapXform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return;
        }
        Vector3 forwardFlat = cam.forward; forwardFlat.y = 0f;
        if (forwardFlat.sqrMagnitude < 1e-4f) forwardFlat = Vector3.forward;
        forwardFlat.Normalize();
        Vector3 pos = cam.position + forwardFlat * spawnDistanceMeters;
        pos.y = cam.position.y;
        mapXform.SetPositionAndRotation(pos, Quaternion.LookRotation(forwardFlat, Vector3.up));
    }
}
