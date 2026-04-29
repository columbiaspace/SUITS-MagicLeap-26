using UnityEngine;

public class MinimapExpandZoom : MonoBehaviour
{
    [Header("Main Panel")]
    public RectTransform minimapBackground;

    [Header("Things that should NOT grow")]
    public RectTransform playerIcon;
    public RectTransform pathContainer;
    public RectTransform button;

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
        if (minimapBackground == null) return;

        minimapBackground.sizeDelta = Vector2.Lerp(
            minimapBackground.sizeDelta,
            targetSize,
            Time.deltaTime * animationSpeed
        );

        minimapBackground.localPosition = Vector3.Lerp(
            minimapBackground.localPosition,
            targetPosition,
            Time.deltaTime * animationSpeed
        );

        KeepChildSizesConstant();
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

    private void KeepChildSizesConstant()
    {
        float scaleX = minimapBackground.sizeDelta.x / collapsedSize.x;
        float scaleY = minimapBackground.sizeDelta.y / collapsedSize.y;

        Vector3 inverseScale = new Vector3(
            1f / scaleX,
            1f / scaleY,
            1f
        );

        if (playerIcon != null)
            playerIcon.localScale = inverseScale;

        if (pathContainer != null)
            pathContainer.localScale = inverseScale;

        if (button != null)
            button.localScale = inverseScale;
    }
}