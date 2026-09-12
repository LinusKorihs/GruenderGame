#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelAtmosphereController))]
public sealed class LevelAtmosphereControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);
        if (GUILayout.Button("Apply Current Atmosphere"))
        {
            LevelAtmosphereController controller = (LevelAtmosphereController)target;
            controller.ApplyCurrentAtmosphere();
            EditorUtility.SetDirty(controller);
        }
    }
}

[CustomEditor(typeof(LevelAtmosphereProfile))]
public sealed class LevelAtmosphereProfileEditor : Editor
{
    private static bool showAmbient;
    private static bool showReflections;
    private static bool showUnityFog;
    private static bool showRuntime = true;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSkybox();
        EditorGUILayout.Space(8f);
        DrawDirectionalLight();
        EditorGUILayout.Space(8f);
        DrawTransition();
        EditorGUILayout.Space(8f);
        DrawFogOfWar();

        EditorGUILayout.Space(8f);
        showAmbient = EditorGUILayout.Foldout(showAmbient, "Advanced Ambient", true);
        if (showAmbient)
        {
            DrawAmbient();
        }

        showReflections = EditorGUILayout.Foldout(showReflections, "Advanced Reflections", true);
        if (showReflections)
        {
            DrawProperty("defaultReflectionMode");
            DrawProperty("defaultReflectionResolution");
            DrawProperty("reflectionIntensity");
        }

        showUnityFog = EditorGUILayout.Foldout(showUnityFog, "Advanced Unity Fog", true);
        if (showUnityFog)
        {
            DrawProperty("applyUnityFog");
            DrawProperty("unityFogEnabled");
            DrawProperty("unityFogColor");
            DrawProperty("unityFogMode");
            DrawProperty("unityFogDensity");
            DrawProperty("unityFogStartDistance");
            DrawProperty("unityFogEndDistance");
        }

        showRuntime = EditorGUILayout.Foldout(showRuntime, "Runtime Update", true);
        if (showRuntime)
        {
            DrawProperty("updateDynamicGI");
        }

        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(10);
        if (GUILayout.Button("Apply To Active Atmosphere Controller"))
        {
            LevelAtmosphereController controller = FindFirstObjectByType<LevelAtmosphereController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[Level Atmosphere] No active LevelAtmosphereController found.");
                return;
            }

            controller.ApplyPreviewProfile((LevelAtmosphereProfile)target);
            EditorUtility.SetDirty(controller);
        }
    }

    private void DrawSkybox()
    {
        DrawProperty("skyboxMode");

        SerializedProperty skyboxMode = serializedObject.FindProperty("skyboxMode");
        if (skyboxMode != null && skyboxMode.enumValueIndex == (int)LevelSkyboxMode.UseMaterial)
        {
            DrawProperty("skyboxMaterial");
        }
        else if (skyboxMode != null && skyboxMode.enumValueIndex == (int)LevelSkyboxMode.ProceduralColors)
        {
            DrawProperty("proceduralSkyTint");
            DrawProperty("proceduralGroundColor");
            DrawProperty("proceduralExposure");
            DrawProperty("proceduralAtmosphereThickness");
        }
    }

    private void DrawDirectionalLight()
    {
        DrawProperty("manageDirectionalLight");
        DrawProperty("preferExistingDirectionalLight");
        DrawProperty("disableDuplicateDirectionalLights");
        DrawProperty("directionalLightName");
        DrawProperty("directionalLightEulerAngles");
        DrawProperty("directionalLightColor");
        DrawProperty("directionalLightIntensity");
        DrawProperty("directionalLightIndirectMultiplier");
        DrawProperty("directionalLightShadowStrength");
        DrawProperty("directionalLightShadows");
        DrawProperty("directionalLightCullingMask");
        DrawProperty("useUrpPipelineLightSettings");
    }

    private void DrawTransition()
    {
        DrawProperty("useRuntimeBlend");
        DrawProperty("blendDuration");
        DrawProperty("blendCurve");
    }

    private void DrawFogOfWar()
    {
        DrawProperty("fogOfWarProfile");
    }

    private void DrawAmbient()
    {
        DrawProperty("applyAmbient");
        DrawProperty("ambientMode");
        DrawProperty("ambientLightColor");
        DrawProperty("ambientSkyColor");
        DrawProperty("ambientEquatorColor");
        DrawProperty("ambientGroundColor");
        DrawProperty("ambientIntensity");
        DrawProperty("subtractiveShadowColor");
    }

    private void DrawProperty(string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            EditorGUILayout.PropertyField(property, true);
        }
    }
}

[CustomEditor(typeof(LevelFogOfWarProfile))]
public sealed class LevelFogOfWarProfileEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);
        if (GUILayout.Button("Apply To Active Fog Of War Controller"))
        {
            LevelFogOfWarController controller = FindFirstObjectByType<LevelFogOfWarController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[Fog Of War] No active LevelFogOfWarController found.");
                return;
            }

            controller.Apply((LevelFogOfWarProfile)target, null);
            EditorUtility.SetDirty(controller);
        }
    }
}
#endif
