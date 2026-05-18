using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
public class HandMenuToggleAction : MonoBehaviour
{
    private enum ActionType
    {
        None = 0,
        ToggleVitals = 1,
        LoadScene = 2,
    }

    [Header("Action")]
    [SerializeField] private ActionType actionType = ActionType.None;
    [SerializeField] private string sceneNameOrPath = string.Empty;
    [SerializeField] private bool resetToggleAfterInvoke;

    [Header("Optional References")]
    [SerializeField] private VitalsButton vitalsButton;

    private Toggle toggle;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
    }

    public void ConfigureNone()
    {
        actionType = ActionType.None;
        sceneNameOrPath = string.Empty;
        resetToggleAfterInvoke = false;
    }

    public void ConfigureToggleVitals()
    {
        actionType = ActionType.ToggleVitals;
        sceneNameOrPath = string.Empty;
        resetToggleAfterInvoke = false;
    }

    public void ConfigureLoadScene(string sceneName, bool resetAfterInvoke = true)
    {
        actionType = ActionType.LoadScene;
        sceneNameOrPath = sceneName ?? string.Empty;
        resetToggleAfterInvoke = resetAfterInvoke;
    }

    private void OnEnable()
    {
        if (toggle == null)
            toggle = GetComponent<Toggle>();

        if (toggle != null)
            toggle.onValueChanged.AddListener(HandleToggleChanged);
    }

    private void OnDisable()
    {
        if (toggle != null)
            toggle.onValueChanged.RemoveListener(HandleToggleChanged);
    }

    private void HandleToggleChanged(bool isOn)
    {
        switch (actionType)
        {
            case ActionType.ToggleVitals:
                SetVitalsVisible(isOn);
                break;
            case ActionType.LoadScene:
                if (!isOn)
                    return;

                LoadScene(sceneNameOrPath);
                if (resetToggleAfterInvoke && toggle != null)
                    toggle.SetIsOnWithoutNotify(false);
                break;
        }
    }

    private void SetVitalsVisible(bool shouldBeVisible)
    {
        VitalsButton resolvedVitalsButton = ResolveVitalsButton();
        if (resolvedVitalsButton == null || resolvedVitalsButton.healthCanvas == null)
        {
            Debug.LogWarning("[HandMenuToggleAction] Could not find VitalsButton with a health canvas.");
            return;
        }

        if (resolvedVitalsButton.healthCanvas.activeSelf != shouldBeVisible)
            resolvedVitalsButton.VitalsButton_OnClick();
    }

    private VitalsButton ResolveVitalsButton()
    {
        if (vitalsButton != null)
            return vitalsButton;

#if UNITY_2023_1_OR_NEWER
        vitalsButton = FindFirstObjectByType<VitalsButton>(FindObjectsInactive.Include);
#else
        vitalsButton = FindObjectOfType<VitalsButton>(true);
#endif

        return vitalsButton;
    }

    private static void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[HandMenuToggleAction] Scene name/path is empty.");
            return;
        }

        if (sceneName.StartsWith("Assets/", StringComparison.Ordinal))
        {
            string scenePath = sceneName.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                ? sceneName
                : sceneName + ".unity";
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
            if (buildIndex >= 0)
            {
                SceneManager.LoadScene(buildIndex);
                return;
            }
        }

        SceneManager.LoadScene(sceneName);
    }
}
