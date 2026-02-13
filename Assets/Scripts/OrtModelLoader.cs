// Assets/Scripts/OrtModelLoader.cs
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Copies ONNX models from StreamingAssets to persistentDataPath.
/// Handles Android's APK-packaged StreamingAssets via UnityWebRequest.
/// </summary>
public static class OrtModelLoader
{
    public static IEnumerator CopyModelToPersistent(string streamingAssetsRelativePath, string outputFileName)
    {
        var src = Path.Combine(Application.streamingAssetsPath, streamingAssetsRelativePath);
        var dst = Path.Combine(Application.persistentDataPath, outputFileName);

        if (File.Exists(dst))
        {
            Debug.Log("[ORT] Model already exists: " + dst);
            yield break;
        }

        var dstDir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
        {
            Directory.CreateDirectory(dstDir);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android: StreamingAssets are inside the APK; must use UnityWebRequest
        using (var req = UnityWebRequest.Get(src))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[ORT] StreamingAssets read failed: " + req.error + " src=" + src);
                yield break;
            }
            File.WriteAllBytes(dst, req.downloadHandler.data);
        }
#else
        // Editor/macOS/Windows: StreamingAssets are normal files
        if (!File.Exists(src))
        {
            Debug.LogError("[ORT] Source model not found: " + src);
            yield break;
        }
        File.Copy(src, dst);
#endif

        Debug.Log("[ORT] Copied model to: " + dst);
    }
}
