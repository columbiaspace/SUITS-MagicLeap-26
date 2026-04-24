using UnityEngine;
using UnityEngine.SceneManagement;

public class AIAPersistentRoot : MonoBehaviour
{
    [SerializeField] private GameObject persistentCanvasRoot;

    private Canvas persistentCanvas;

    public static AIAPersistentRoot Instance { get; private set; }

    public static bool HasInstance => Instance != null;

    private void Awake()
    {
        persistentCanvas ??= persistentCanvasRoot != null ? persistentCanvasRoot.GetComponent<Canvas>() : null;

        if (Instance != null && Instance != this)
        {
            DestroyManagedObjects();
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);
        if (persistentCanvasRoot != null && persistentCanvasRoot != gameObject)
        {
            DontDestroyOnLoad(persistentCanvasRoot);
        }

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        RefreshForScene(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Instance = null;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshForScene(scene);
    }

    public void RefreshForScene(Scene scene)
    {
        if (persistentCanvas == null && persistentCanvasRoot != null)
        {
            persistentCanvas = persistentCanvasRoot.GetComponent<Canvas>();
        }

        if (persistentCanvas != null)
        {
            Camera sceneCamera = Camera.main;
            if (sceneCamera != null)
            {
                persistentCanvas.worldCamera = sceneCamera;
            }
        }

        if (persistentCanvasRoot != null)
        {
            persistentCanvasRoot.SetActive(AIASceneCatalog.IsAiaEnabledScene(scene.name));
        }
    }

    private void DestroyManagedObjects()
    {
        if (persistentCanvasRoot != null && persistentCanvasRoot != gameObject)
        {
            Destroy(persistentCanvasRoot);
        }

        Destroy(gameObject);
    }
}
