// Hooked to the Starter scene START button. Loads the configured next scene.
using UnityEngine;
using UnityEngine.SceneManagement;

public class StartButtonHandler : MonoBehaviour
{
    [SerializeField] private string nextSceneName = "Mission";

    public void OnStartPressed()
    {
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogWarning("StartButtonHandler: nextSceneName is empty; no scene loaded.");
            return;
        }
        SceneManager.LoadScene(nextSceneName);
    }
}
