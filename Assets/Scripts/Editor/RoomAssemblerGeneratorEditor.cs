#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RoomAssemblerGenerator))]
public class RoomAssemblerGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gen = (RoomAssemblerGenerator)target;

        if (GUILayout.Button("Enable NavMesh Mesh Read/Write"))
        {
            EnableNavMeshMeshReadWrite(gen);
        }
    }

    private static void EnableNavMeshMeshReadWrite(RoomAssemblerGenerator generator)
    {
        if (generator == null || generator.config == null)
        {
            Debug.LogError("[PCG NavMesh] Cannot scan meshes because RoomAssemblerConfig is missing.", generator);
            return;
        }

        HashSet<RoomDefinition> definitions = CollectRoomDefinitions(generator.config);
        HashSet<string> modelPaths = new HashSet<string>();

        foreach (RoomDefinition definition in definitions)
        {
            if (definition == null || definition.prefab == null)
                continue;

            MeshCollider[] colliders = definition.prefab.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Mesh mesh = colliders[i].sharedMesh;
                if (mesh == null || mesh.isReadable)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(assetPath))
                    modelPaths.Add(assetPath);
            }
        }

        if (modelPaths.Count == 0)
        {
            Debug.Log("[PCG NavMesh] All MeshCollider source meshes already allow Read/Write.", generator);
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Enable Mesh Read/Write",
            $"Enable Read/Write and reimport {modelPaths.Count} model asset(s)?\n\n" +
            "This is required for runtime NavMesh baking from MeshColliders, but increases mesh memory usage.",
            "Enable and Reimport",
            "Cancel");

        if (!confirmed)
            return;

        int changed = 0;
        foreach (string assetPath in modelPaths)
        {
            if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer || importer.isReadable)
                continue;

            importer.isReadable = true;
            importer.SaveAndReimport();
            changed++;
        }

        AssetDatabase.Refresh();
        Debug.Log(
            $"[PCG NavMesh] Enabled Read/Write on {changed} model asset(s). " +
            "Runtime NavMesh MeshCollider warnings should now be resolved.",
            generator);
    }

    private static HashSet<RoomDefinition> CollectRoomDefinitions(RoomAssemblerConfig config)
    {
        HashSet<RoomDefinition> definitions = new HashSet<RoomDefinition>
        {
            config.startRoom,
            config.endRoom,
            config.wallCapRoom
        };

        if (config.roomPool != null)
        {
            for (int i = 0; i < config.roomPool.Count; i++)
                definitions.Add(config.roomPool[i]);
        }

        definitions.Remove(null);
        return definitions;
    }
}

[CustomEditor(typeof(LevelSystemController))]
public sealed class LevelSystemControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (GUILayout.Button("Enable Environment Prop Read/Write"))
        {
            EnableEnvironmentPropReadWrite((LevelSystemController)target);
        }
    }

    private static void EnableEnvironmentPropReadWrite(LevelSystemController levelSystem)
    {
        string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art/Models/Environment" });
        List<string> disabledPaths = new List<string>();

        for (int i = 0; i < modelGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(modelGuids[i]);
            if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
                disabledPaths.Add(path);
        }

        if (disabledPaths.Count == 0)
        {
            Debug.Log("[Level System NavMesh] All environment prop meshes already allow Read/Write.", levelSystem);
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Enable Environment Prop Read/Write",
            $"Enable Read/Write and reimport {disabledPaths.Count} environment model asset(s)?\n\n" +
            "This makes runtime NavMesh baking build-safe for MeshCollider props.",
            "Enable and Reimport",
            "Cancel");
        if (!confirmed)
            return;

        for (int i = 0; i < disabledPaths.Count; i++)
        {
            if (AssetImporter.GetAtPath(disabledPaths[i]) is not ModelImporter importer)
                continue;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        AssetDatabase.Refresh();
        Debug.Log($"[Level System NavMesh] Enabled Read/Write on {disabledPaths.Count} environment model asset(s).", levelSystem);
    }
}

[CustomEditor(typeof(LevelProfileLoader))]
public class LevelProfileLoaderEditor : Editor
{
    private static bool runtimeFoldout = true;

    private SerializedProperty levelProfile;
    private SerializedProperty levelAtmosphereProfile;
    private SerializedProperty generateAfterApply;
    private SerializedProperty logProfileApplication;

    private void OnEnable()
    {
        levelProfile = serializedObject.FindProperty("levelProfile");
        levelAtmosphereProfile = serializedObject.FindProperty("levelAtmosphereProfile");
        generateAfterApply = serializedObject.FindProperty("generateAfterApply");
        logProfileApplication = serializedObject.FindProperty("logProfileApplication");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawProperty(levelProfile);
        DrawProperty(levelAtmosphereProfile);

        GUILayout.Space(8);
        runtimeFoldout = EditorGUILayout.Foldout(runtimeFoldout, "Runtime", true);
        if (runtimeFoldout)
        {
            EditorGUI.indentLevel++;
            DrawProperty(generateAfterApply);
            DrawProperty(logProfileApplication);
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();

        LevelProfileLoader loader = (LevelProfileLoader)target;

        GUILayout.Space(10);

        if (GUILayout.Button("Validate And Apply"))
        {
            loader.ValidateAndApplyLevelProfile();
            EditorUtility.SetDirty(loader);
        }

        GUILayout.Space(4);

        if (GUILayout.Button("Generate"))
        {
            loader.Generate();
            EditorUtility.SetDirty(loader);
        }

        GUILayout.Space(4);

        if (GUILayout.Button("Clear Generated Content"))
        {
            loader.ClearGeneratedLevelContent();
            EditorUtility.SetDirty(loader);
        }

        GUILayout.Space(4);

        if (GUILayout.Button("Move Player + Minions Near Exit"))
        {
            LevelStartRunFlowController controller = FindFirstObjectByType<LevelStartRunFlowController>();
            if (controller != null)
            {
                controller.MovePlayerNearGeneratedExitForTesting();
            }
            else
            {
                Debug.LogWarning("[RunFlow] No LevelStartRunFlowController found for exit teleport test.", loader);
            }
        }
    }

    private static void DrawProperty(SerializedProperty property)
    {
        if (property != null)
        {
            EditorGUILayout.PropertyField(property);
        }
    }
}
#endif
