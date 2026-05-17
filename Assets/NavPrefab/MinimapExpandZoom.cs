using UnityEngine;

public class MinimapExpandZoom : MonoBehaviour
{
    public RectTransform minimapCanvas;

    [Header("Collapsed")]
    public Vector2 collapsedSize = new Vector2(200f, 200f);
    public Vector3 collapsedLocalPosition = new Vector3(-250f, 150f, 0f);

    [Header("Expanded")]
    public Vector2 expandedSize = new Vector2(600f, 600f);
    public Vector3 expandedLocalPosition = Vector3.zero;

    [Header("Animation")]
    public float animationSpeed = 8f;

    private bool isExpanded;
    private Vector2 targetSize;
    private Vector3 targetPosition;

    void Start()
    {
        targetSize = collapsedSize;
        targetPosition = collapsedLocalPosition;
    }

    void Update()
    {
        if (minimapCanvas == null) return;

        minimapCanvas.sizeDelta = Vector2.Lerp(
            minimapCanvas.sizeDelta,
            targetSize,
            Time.deltaTime * animationSpeed
        );

        minimapCanvas.localPosition = Vector3.Lerp(
            minimapCanvas.localPosition,
            targetPosition,
            Time.deltaTime * animationSpeed
        );
    }

    public void ToggleExpand()
    {
        isExpanded = !isExpanded;

        if (isExpanded)
        {
            targetSize = expandedSize;
            targetPosition = expandedLocalPosition;
        }
        else
        {
            targetSize = collapsedSize;
            targetPosition = collapsedLocalPosition;
        }
    }
}