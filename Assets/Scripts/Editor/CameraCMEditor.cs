#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CameraCM))]
public class CameraCMEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CameraCM cameraCM = (CameraCM)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Live Camera Tuning", EditorStyles.boldLabel);

        if (GUILayout.Button("Apply Starting Stage"))
        {
            ApplyAndDirty(cameraCM, c => c.ApplyStartingStageNow());
        }

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Wide"))
        {
            ApplyAndDirty(cameraCM, c => c.ApplyWideStageNow());
        }

        if (GUILayout.Button("Default"))
        {
            ApplyAndDirty(cameraCM, c => c.ApplyDefaultStageNow());
        }

        if (GUILayout.Button("Close"))
        {
            ApplyAndDirty(cameraCM, c => c.ApplyCloseStageNow());
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "Edit SO_Camera values, then press a stage button to push radius, vertical angle, FOV, and X/Y/Z target offset to the Cinemachine camera immediately. In Play Mode, live stage tuning also refreshes the current stage automatically.",
            MessageType.Info);
    }

    private static void ApplyAndDirty(CameraCM cameraCM, System.Action<CameraCM> apply)
    {
        Object orbital = GetObjectReference(cameraCM, "orbital");
        Object cmCamera = GetObjectReference(cameraCM, "cmCamera");

        List<Object> undoObjects = new List<Object> { cameraCM };
        if (orbital != null) undoObjects.Add(orbital);
        if (cmCamera != null) undoObjects.Add(cmCamera);

        Undo.RecordObjects(undoObjects.ToArray(), "Apply Camera Stage");

        apply(cameraCM);

        EditorUtility.SetDirty(cameraCM);
        if (orbital != null) EditorUtility.SetDirty(orbital);
        if (cmCamera != null) EditorUtility.SetDirty(cmCamera);

        PrefabUtility.RecordPrefabInstancePropertyModifications(cameraCM);
        if (orbital != null) PrefabUtility.RecordPrefabInstancePropertyModifications(orbital);
        if (cmCamera != null) PrefabUtility.RecordPrefabInstancePropertyModifications(cmCamera);

        if (!Application.isPlaying)
        {
            UnityEditor.SceneView.RepaintAll();
        }
    }

    private static Object GetObjectReference(CameraCM cameraCM, string propertyName)
    {
        SerializedObject serializedCamera = new SerializedObject(cameraCM);
        return serializedCamera.FindProperty(propertyName)?.objectReferenceValue;
    }
}
#endif
