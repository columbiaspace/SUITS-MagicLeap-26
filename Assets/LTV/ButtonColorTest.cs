using UnityEngine;
using UnityEngine.UI;

public class ButtonColorTest : MonoBehaviour
{
    public Image background;

    private void Start()
    {
        Debug.Log("[ButtonColorTest] Script is running. Press Space to turn background red.");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            OnButtonClicked();
        }
    }

    public void OnButtonClicked()
    {
        Debug.Log("[ButtonColorTest] OnButtonClicked fired!");
        background.color = Color.red;
    }
}
