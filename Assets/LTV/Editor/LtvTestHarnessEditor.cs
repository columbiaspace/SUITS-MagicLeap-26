using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LtvTestHarness))]
public class LtvTestHarnessEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LtvTestHarness harness = (LtvTestHarness)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Diagnosis Controls", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Start Diagnosis", GUILayout.Height(28)))
        {
            harness.TriggerStartDiagnosis();
        }

        if (GUILayout.Button("Next Step", GUILayout.Height(28)))
        {
            harness.TriggerAdvanceStep();
        }

        if (GUILayout.Button("Stop Diagnosis", GUILayout.Height(28)))
        {
            harness.TriggerStopDiagnosis();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Log Full Snapshot", GUILayout.Height(28)))
        {
            harness.LogFullSnapshot();
        }

        if (GUILayout.Button("Run E2E Test", GUILayout.Height(28)))
        {
            harness.RunEndToEndTest();
        }
        EditorGUILayout.EndHorizontal();
    }
}
