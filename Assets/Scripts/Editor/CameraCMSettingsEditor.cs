#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CameraCMSettings))]
public class CameraCMSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CameraCMSettings settings = (CameraCMSettings)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Apply To Scene Camera", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Wide"))
        {
            ApplyToSceneCameras(settings, cameraCM => cameraCM.ApplyWideStageNow());
        }

        if (GUILayout.Button("Default"))
        {
            ApplyToSceneCameras(settings, cameraCM => cameraCM.ApplyDefaultStageNow());
        }

        if (GUILayout.Button("Close"))
        {
            ApplyToSceneCameras(settings, cameraCM => cameraCM.ApplyCloseStageNow());
        }

        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Starting Stage"))
        {
            ApplyToSceneCameras(settings, cameraCM => cameraCM.ApplyStartingStageNow());
        }

        EditorGUILayout.HelpBox(
            "These buttons update CameraCM objects in the open scene that reference this settings asset. They do not create or commit changes.",
            MessageType.Info);
    }

    private static void ApplyToSceneCameras(CameraCMSettings settings, System.Action<CameraCM> apply)
    {
        CameraCM[] sceneCameras = Object.FindObjectsByType<CameraCM>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<Object> undoObjects = new();

        for (int i = 0; i < sceneCameras.Length; i++)
        {
            CameraCM cameraCM = sceneCameras[i];
            if (cameraCM == null || cameraCM.Settings != settings) continue;

            undoObjects.Add(cameraCM);
            AddReference(undoObjects, cameraCM, "orbital");
            AddReference(undoObjects, cameraCM, "cmCamera");
        }

        if (undoObjects.Count == 0)
        {
            Debug.LogWarning($"[CameraCMSettingsEditor] No CameraCM in the open scene uses '{settings.name}'.");
            return;
        }

        Undo.RecordObjects(undoObjects.ToArray(), "Apply Camera Settings Stage");

        for (int i = 0; i < sceneCameras.Length; i++)
        {
            CameraCM cameraCM = sceneCameras[i];
            if (cameraCM == null || cameraCM.Settings != settings) continue;

            apply(cameraCM);
            EditorUtility.SetDirty(cameraCM);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cameraCM);
        }

        for (int i = 0; i < undoObjects.Count; i++)
        {
            if (undoObjects[i] != null) EditorUtility.SetDirty(undoObjects[i]);
        }

        SceneView.RepaintAll();
    }

    private static void AddReference(List<Object> objects, CameraCM cameraCM, string propertyName)
    {
        Object reference = GetObjectReference(cameraCM, propertyName);
        if (reference != null && !objects.Contains(reference)) objects.Add(reference);
    }

    private static Object GetObjectReference(CameraCM cameraCM, string propertyName)
    {
        SerializedObject serializedCamera = new SerializedObject(cameraCM);
        return serializedCamera.FindProperty(propertyName)?.objectReferenceValue;
    }
}
#endif
