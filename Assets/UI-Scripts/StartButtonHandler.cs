// Loads the configured scene when the Starter screen START button is pressed.
using UnityEngine;
using UnityEngine.SceneManagement;

public class StartButtonHandler : MonoBehaviour
{
    [SerializeField] private string nextSceneName = "Mission";

    public void OnStartPressed()
    {
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogWarning("[StartButtonHandler] nextSceneName is empty; ignoring START press.");
            return;
        }

        SceneManager.LoadScene(nextSceneName);
    }
}
