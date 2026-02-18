using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ImageCarouselUI : MonoBehaviour
{
    [SerializeField] private Image displayImage;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button backButton;
    [SerializeField] private List<Sprite> slides = new List<Sprite>();

    private int index = 0;

    void Awake()
    {
        if (nextButton) nextButton.onClick.AddListener(Next);
        if (backButton) backButton.onClick.AddListener(Prev);
        Refresh();
    }

    void Next()
    {
        if (slides.Count == 0) return;
        index = (index + 1) % slides.Count;
        Refresh();
    }

    void Prev()
    {
        if (slides.Count == 0) return;
        index = (index - 1 + slides.Count) % slides.Count;
        Refresh();
    }

    void Refresh()
    {
        if (!displayImage) return;

        if (slides.Count == 0)
        {
            displayImage.enabled = false;
            return;
        }

        displayImage.enabled = true;
        displayImage.sprite = slides[index];
        displayImage.preserveAspect = true;
    }
}

