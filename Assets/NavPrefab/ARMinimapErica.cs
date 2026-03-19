using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class ARMinimapErica : MonoBehaviour
{
    public RectTransform minimapRect;
    public RectTransform playerIcon;
    public RectTransform pathContainer;
    public GameObject pathDotPrefab;

    public float worldToMapScale = 8f;

    private List<GameObject> _pathDots = new List<GameObject>();

    void Start() { }

    void Update()
    {
        UpdatePlayerIcon();
        // RecordTrail() removed
    }

    void UpdatePlayerIcon()
    {
        Vector3 worldPos = Camera.main.transform.position;
        Vector2 mapPos = new Vector2(worldPos.x, worldPos.z) * worldToMapScale;

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

            Vector2 mapPos = new Vector2(worldX, worldZ) * worldToMapScale;
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