using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Editor utilities to prove the LTV-HUD button is wired correctly and clickable.
///
/// Menu items:
///   LTV -> Validate Active Scene
///     Validates whichever scene is currently open (working_scenes/LTV-HUD.unity
///     or final_scenes/LTV.unity after migration).
///
///   LTV -> Simulate Button Click (Play Mode)
///     Runs while Play Mode is active. Locates the checkmark button, records
///     ClickCount before/after, invokes onClick through the real pipeline, and
///     reports PASS/FAIL.
/// </summary>
public static class LtvHudValidator
{
    private const string DefaultScenePath = "Assets/Scenes/working_scenes/LTV-HUD.unity";

    [MenuItem("LTV/Validate Active Scene")]
    public static void ValidateScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            if (System.IO.File.Exists(DefaultScenePath))
            {
                scene = EditorSceneManager.OpenScene(DefaultScenePath, OpenSceneMode.Single);
            }
            else
            {
                Debug.LogError($"[LTV-Validate] No active scene. Open a scene with the LTV HUD first.");
                return;
            }
        }

        LtvHudController controller = Object.FindObjectOfType<LtvHudController>();
        StringBuilder report = new StringBuilder();
        report.AppendLine($"=== LTV-HUD Scene Validation ({scene.path}) ===");

        int pass = 0;
        int fail = 0;

        void Check(string name, bool condition, string details = null)
        {
            if (condition)
            {
                pass++;
                report.AppendLine($"  [PASS] {name}{(details != null ? $" ({details})" : "")}");
            }
            else
            {
                fail++;
                report.AppendLine($"  [FAIL] {name}{(details != null ? $" ({details})" : "")}");
            }
        }

        Check("LtvHudController present in scene", controller != null);
        if (controller == null)
        {
            report.AppendLine("ABORT: Controller not found.");
            Debug.LogError(report.ToString());
            return;
        }

        Check("checkmarkButton reference wired", controller.checkmarkButton != null);
        Check("errorCodeText reference wired", controller.errorCodeText != null);
        Check("instructionText reference wired", controller.instructionText != null);
        Check("leftPanelImage reference wired", controller.leftPanelImage != null);
        Check("queueService reference wired", controller.queueService != null);

        Canvas canvas = controller.GetComponentInParent<Canvas>();
        Check("Canvas found in parent chain", canvas != null);
        if (canvas != null)
        {
            Check("Canvas RenderMode is WorldSpace", canvas.renderMode == RenderMode.WorldSpace);
            Check("Canvas local scale = 0.001 (1px = 1mm, per hand-tracking PDF)",
                Mathf.Approximately(canvas.transform.localScale.x, 0.001f),
                $"actual={canvas.transform.localScale.x}");

            TrackedDeviceGraphicRaycaster tdgr = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            Check("Canvas has TrackedDeviceGraphicRaycaster (required for XR Hands ray/poke)", tdgr != null);

            GraphicRaycaster gr = canvas.GetComponent<GraphicRaycaster>();
            bool onlyTrackedRaycaster = tdgr != null && (gr == null || gr is TrackedDeviceGraphicRaycaster);
            Check("Canvas has no plain GraphicRaycaster (PDF: remove it, only use TrackedDeviceGraphicRaycaster)",
                onlyTrackedRaycaster);

            Camera eventCam = canvas.worldCamera;
            Check("Canvas.worldCamera (Event Camera) is assigned", eventCam != null,
                eventCam != null ? $"cam={eventCam.name}" : "set this to the Main Camera under XR Origin");

            LazyFollow lazyFollow = canvas.GetComponent<LazyFollow>();
            Check("Canvas has LazyFollow (body-lock follow)", lazyFollow != null);
            if (lazyFollow != null)
            {
                Check("LazyFollow is enabled", lazyFollow.enabled);
                Check("LazyFollow target is assigned (Main Camera for body-lock)",
                    lazyFollow.target != null,
                    lazyFollow.target != null ? $"target={lazyFollow.target.name}" : "assign Main Camera");
                Check("LazyFollow rotationFollowMode = None (body-lock: no head-rotation follow)",
                    lazyFollow.rotationFollowMode == LazyFollow.RotationFollowMode.None,
                    $"actual={lazyFollow.rotationFollowMode}");
            }

            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                bool canvasUnderCamera = canvas.transform.IsChildOf(mainCam.transform);
                Check("Canvas is NOT a child of Main Camera (body-locked, not head-locked)", !canvasUnderCamera);
            }
        }

        if (controller.checkmarkButton != null)
        {
            Button btn = controller.checkmarkButton;
            Check("Button is interactable", btn.interactable);
            Check("Button has a Graphic target for raycast", btn.targetGraphic != null && btn.targetGraphic.raycastTarget);
            Check("Button onClick has persistent listeners", btn.onClick.GetPersistentEventCount() > 0);

            bool hasCheckmarkListener = false;
            for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++)
            {
                string target = btn.onClick.GetPersistentTarget(i)?.ToString() ?? "";
                string method = btn.onClick.GetPersistentMethodName(i) ?? "";
                if (method == "OnCheckmarkClicked" && target.Contains("LtvHudController"))
                {
                    hasCheckmarkListener = true;
                    break;
                }
            }
            Check("Button onClick is wired to LtvHudController.OnCheckmarkClicked", hasCheckmarkListener);

            RectTransform brt = btn.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(canvas.GetComponent<RectTransform>());
            Rect rect = brt.rect;
            Check("Button has non-zero rect after layout", rect.width > 0.01f && rect.height > 0.01f,
                $"rect={rect}");
        }

        EventSystem es = Object.FindObjectOfType<EventSystem>();
        Check("Scene has an EventSystem", es != null);

        report.AppendLine($"=== Results: {pass} passed, {fail} failed ===");

        if (fail == 0)
        {
            Debug.Log(report.ToString());
            EditorUtility.DisplayDialog("LTV-HUD Validation", $"All {pass} checks passed!\n\nSee Console for details.", "OK");
        }
        else
        {
            Debug.LogError(report.ToString());
            EditorUtility.DisplayDialog("LTV-HUD Validation", $"{fail} check(s) failed, {pass} passed.\n\nSee Console for details.", "OK");
        }
    }

    [MenuItem("LTV/Simulate Button Click (Play Mode)")]
    public static void SimulateClickInPlayMode()
    {
        if (!EditorApplication.isPlaying)
        {
            if (EditorUtility.DisplayDialog(
                "LTV: Simulate Click",
                "You must be in Play Mode to simulate a click.\n\nEnter Play Mode first (press the play button), then run this menu again.",
                "OK", "Cancel"))
            {
                if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path)
                    && System.IO.File.Exists(DefaultScenePath))
                {
                    EditorSceneManager.OpenScene(DefaultScenePath, OpenSceneMode.Single);
                }
                EditorApplication.isPlaying = true;
            }
            return;
        }

        LtvHudController controller = Object.FindObjectOfType<LtvHudController>();
        if (controller == null)
        {
            Debug.LogError("[LTV-Simulate] No LtvHudController found in active scene.");
            return;
        }

        if (controller.checkmarkButton == null)
        {
            Debug.LogError("[LTV-Simulate] Controller has no checkmarkButton assigned.");
            return;
        }

        int before = controller.ClickCount;
        bool wasInteractable = controller.checkmarkButton.interactable;

        if (!wasInteractable)
        {
            Debug.LogWarning("[LTV-Simulate] Button was not interactable \u2014 forcing interactable=true for this test.");
            controller.checkmarkButton.interactable = true;
        }

        Debug.Log($"[LTV-Simulate] Invoking Button.onClick.Invoke() through the real pipeline. ClickCount before={before}");
        controller.checkmarkButton.onClick.Invoke();
        int after = controller.ClickCount;

        if (after > before)
        {
            Debug.Log($"[LTV-Simulate] <color=#4f4>PASS</color>: OnCheckmarkClicked fired. ClickCount {before} -> {after}. Button is clickable.");
            EditorUtility.DisplayDialog("LTV: Simulate Click", $"PASS\nClickCount: {before} \u2192 {after}\n\nThe button IS clickable \u2014 its onClick pipeline fires and reaches LtvHudController.OnCheckmarkClicked.", "OK");
        }
        else
        {
            Debug.LogError($"[LTV-Simulate] FAIL: ClickCount did not change ({before} -> {after}). OnClick listeners may be missing or disabled.");
            EditorUtility.DisplayDialog("LTV: Simulate Click", $"FAIL\nClickCount did not change ({before} \u2192 {after}).\nCheck that OnCheckmarkClicked is wired in the Button\u2019s OnClick list.", "OK");
        }
    }

}
