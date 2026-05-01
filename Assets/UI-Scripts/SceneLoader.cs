using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public void LoadScene(string sceneNameOrPath)
    {
        LoadSceneByNameOrPath(sceneNameOrPath);
    }

    public static void LoadSceneByNameOrPath(string sceneNameOrPath)
    {
        if (!string.IsNullOrEmpty(sceneNameOrPath) && sceneNameOrPath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            var path = sceneNameOrPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                ? sceneNameOrPath
                : sceneNameOrPath + ".unity";
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(path);
            if (buildIndex >= 0)
            {
                SceneManager.LoadScene(buildIndex);
                return;
            }
        }

        SceneManager.LoadScene(sceneNameOrPath);
    }
}
