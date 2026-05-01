using UnityEngine;
using UnityEngine.UI;

public class VitalsButton : MonoBehaviour
{
    public GameObject healthCanvas;
    public Image buttonImage;

    [Header("Colors")]
    public Color normalColor = new Color(0.498f, 0.788f, 0.867f, 0.4f);
    public Color selectedColor = new Color(0.35f, 0.6f, 0.7f, 0.55f);

    private bool isActive = false;

    private void Start()
    {
        if (buttonImage != null)
            buttonImage.color = normalColor;
    }

    public void VitalsButton_OnClick()
    {
        isActive = !isActive;

        if (healthCanvas != null)
            healthCanvas.SetActive(isActive);

        if (buttonImage != null)
            buttonImage.color = isActive ? selectedColor : normalColor;
    }
}