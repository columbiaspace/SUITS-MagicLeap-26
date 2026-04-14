using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Utility component wired to UI Button onClick events to navigate between scenes.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
