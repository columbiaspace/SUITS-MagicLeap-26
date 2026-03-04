using UnityEngine;
using UnityEngine.UI;
public class ARMinimapErica : MonoBehaviour
{
    public RectTransform minimapRect;
    public RectTransform playerIcon;
    public RectTransform pathContainer;
    public GameObject trailDotPrefab;
    
    public float worldToMapScale = 8f;
    public float recordDistance = 0.25f;

    private Vector3 lastRecordedPos;

    void Start()
    {
        lastRecordedPos = Camera.main.transform.position;
    }

    void Update()
    {
        UpdatePlayerIcon();
        RecordTrail();
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

    void RecordTrail()
    {
        Vector3 currentPos = Camera.main.transform.position;
        if (Vector3.Distance(currentPos, lastRecordedPos) > recordDistance)
        {
            Vector2 mapPos = new Vector2(currentPos.x, currentPos.z) * worldToMapScale;
            GameObject dot = Instantiate(trailDotPrefab, pathContainer);
            dot.GetComponent<RectTransform>().anchoredPosition = mapPos;
            lastRecordedPos = currentPos;
        }
    }
}

