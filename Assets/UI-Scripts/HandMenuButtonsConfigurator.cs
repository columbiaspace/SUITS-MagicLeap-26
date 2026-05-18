using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HandMenuButtonsConfigurator : MonoBehaviour
{
    private enum MenuActionType
    {
        None = 0,
        ToggleVitals = 1,
        LoadScene = 2,
    }

    private struct MenuButtonDefinition
    {
        public readonly string label;
        public readonly MenuActionType actionType;
        public readonly string sceneNameOrPath;
        public readonly bool resetAfterInvoke;

        public MenuButtonDefinition(string label, MenuActionType actionType, string sceneNameOrPath, bool resetAfterInvoke)
        {
            this.label = label;
            this.actionType = actionType;
            this.sceneNameOrPath = sceneNameOrPath;
            this.resetAfterInvoke = resetAfterInvoke;
        }
    }

    private static readonly MenuButtonDefinition[] k_ButtonDefinitions =
    {
        new MenuButtonDefinition("Vitals", MenuActionType.ToggleVitals, string.Empty, false),
        new MenuButtonDefinition("Return to base", MenuActionType.None, string.Empty, false),
        new MenuButtonDefinition("exit ltv", MenuActionType.LoadScene, "Mission", true),
        new MenuButtonDefinition("Start mission", MenuActionType.LoadScene, "Mission", true),
        new MenuButtonDefinition("Start ingress", MenuActionType.LoadScene, "final_scenes/Ingress", true),
        new MenuButtonDefinition("Start egress", MenuActionType.LoadScene, "final_scenes/Egress", true),
    };

    private void Start()
    {
        ConfigureButtons();
    }

    [ContextMenu("Configure Hand Menu Buttons")]
    private void ConfigureButtons()
    {
        RectTransform contentRoot = FindContentRoot();
        if (contentRoot == null)
        {
            Debug.LogWarning("[HandMenuButtonsConfigurator] Could not locate hand menu content root.", this);
            return;
        }

        List<Toggle> rows = CollectDirectChildRows(contentRoot);
        if (rows.Count == 0)
        {
            Debug.LogWarning("[HandMenuButtonsConfigurator] No toggle rows found under hand menu content.", this);
            return;
        }

        while (rows.Count < k_ButtonDefinitions.Length)
        {
            Toggle sourceRow = rows[rows.Count - 1];
            GameObject clone = Instantiate(sourceRow.gameObject, contentRoot);
            clone.name = $"Item ({rows.Count + 1})";
            clone.transform.SetSiblingIndex(rows.Count);

            Toggle cloneToggle = clone.GetComponent<Toggle>();
            if (cloneToggle == null)
            {
                Destroy(clone);
                break;
            }

            rows.Add(cloneToggle);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            bool shouldShow = i < k_ButtonDefinitions.Length;
            Toggle row = rows[i];
            row.gameObject.SetActive(shouldShow);
            if (!shouldShow)
                continue;

            ConfigureRow(row, k_ButtonDefinitions[i]);
        }
    }

    private static List<Toggle> CollectDirectChildRows(RectTransform contentRoot)
    {
        var rows = new List<Toggle>();
        for (int i = 0; i < contentRoot.childCount; i++)
        {
            Transform child = contentRoot.GetChild(i);
            Toggle row = child.GetComponent<Toggle>();
            if (row != null)
                rows.Add(row);
        }

        return rows;
    }

    private static void ConfigureRow(Toggle row, MenuButtonDefinition definition)
    {
        row.group = null;
        row.SetIsOnWithoutNotify(false);

        Text label = row.GetComponentInChildren<Text>(true);
        if (label != null)
            label.text = definition.label;

        HandMenuToggleAction action = row.GetComponent<HandMenuToggleAction>();
        if (action == null)
            action = row.gameObject.AddComponent<HandMenuToggleAction>();

        switch (definition.actionType)
        {
            case MenuActionType.None:
                action.ConfigureNone();
                break;
            case MenuActionType.ToggleVitals:
                action.ConfigureToggleVitals();
                break;
            case MenuActionType.LoadScene:
                action.ConfigureLoadScene(definition.sceneNameOrPath, definition.resetAfterInvoke);
                break;
        }
    }

    private RectTransform FindContentRoot()
    {
        VerticalLayoutGroup layoutGroup = GetComponentInChildren<VerticalLayoutGroup>(true);
        if (layoutGroup != null)
            return layoutGroup.GetComponent<RectTransform>();

        Transform content = transform.Find("Panel/Scroll View/Viewport/Content");
        return content as RectTransform;
    }
}
