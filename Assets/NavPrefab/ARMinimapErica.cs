using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using System.Collections.Generic;

public class ARMinimapErica : MonoBehaviour
{
    public RectTransform minimapRect;
    public RectTransform playerIcon;
    public RectTransform pathContainer;

    [FormerlySerializedAs("trailDotPrefab")]
    public GameObject pathDotPrefab;

    // CHANGED: Replaced hardcoded worldToMapScale = 8f with worldUnitsVisible = 50f.
    // worldUnitsVisible defines how many world units span the full width of the minimap.
    // This makes scale relative to the minimap rect size, so you can freely resize
    // MinimapBackground in the Inspector without breaking dot positions.
    // Tune this value: smaller = zoomed in, larger = zoomed out.
    public float worldUnitsVisible = 3f;

    // ADDED: Computed property that derives pixels-per-world-unit from the rect size.
    // Because it reads minimapRect.sizeDelta.x at runtime, it automatically stays
    // correct if you resize the minimap rect in the Inspector or at runtime.
    float MapScale => minimapRect.sizeDelta.x / worldUnitsVisible;

    private List<GameObject> _pathDots = new List<GameObject>();

    void Start() { }

    void Update()
    {
        UpdatePlayerIcon();
    }

    void UpdatePlayerIcon()
    {
        Vector3 worldPos = Camera.main.transform.position;

        // CHANGED: was * worldToMapScale (fixed 8f). Now uses MapScale so position
        // is always proportional to both the rect size and worldUnitsVisible.
        Vector2 mapPos = new Vector2(worldPos.x, worldPos.z) * MapScale;

        mapPos.x = Mathf.Clamp(mapPos.x, -minimapRect.sizeDelta.x / 2, minimapRect.sizeDelta.x / 2);
        mapPos.y = Mathf.Clamp(mapPos.y, -minimapRect.sizeDelta.y / 2, minimapRect.sizeDelta.y / 2);

        playerIcon.anchoredPosition = mapPos;
        playerIcon.localEulerAngles = new Vector3(0, 0, -Camera.main.transform.eulerAngles.y);
    }

    public void DrawPathOnMinimap(HashSet<Vector2Int> pathCells)
    {
        foreach (GameObject dot in _pathDots)
            if (dot) Destroy(dot);
        _pathDots.Clear();

        foreach (Vector2Int cell in pathCells)
        {
            float worldX = cell.x * TileManager.TILE_SIZE;
            float worldZ = cell.y * TileManager.TILE_SIZE;

            // CHANGED: was * worldToMapScale (fixed 8f). Now uses MapScale for the
            // same reason as UpdatePlayerIcon — keeps path dots consistent with
            // player icon position at any rect size or worldUnitsVisible value.
            Vector2 mapPos = new Vector2(worldX, worldZ) * MapScale;
            mapPos.x = Mathf.Clamp(mapPos.x, -minimapRect.sizeDelta.x / 2, minimapRect.sizeDelta.x / 2);
            mapPos.y = Mathf.Clamp(mapPos.y, -minimapRect.sizeDelta.y / 2, minimapRect.sizeDelta.y / 2);

            GameObject dot = Instantiate(pathDotPrefab, pathContainer);
            dot.GetComponent<RectTransform>().anchoredPosition = mapPos;

            var img = dot.GetComponent<Image>();
            if (img) img.color = new Color(0.2f, 0.5f, 1.0f, 0.9f);

            _pathDots.Add(dot);
        }
    }
}