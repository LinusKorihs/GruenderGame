using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class LevelAtmosphereController : MonoBehaviour
{
    private const string ProceduralSkyboxShaderName = "Skybox/Procedural";
    private const string SkyTintProperty = "_SkyTint";
    private const string GroundColorProperty = "_GroundColor";
    private const string ExposureProperty = "_Exposure";
    private const string AtmosphereThicknessProperty = "_AtmosphereThickness";

    [Header("Default")]
    [SerializeField] private LevelAtmosphereProfile defaultProfile;
    [SerializeField] private bool applyDefaultOnStart;
    [SerializeField] private bool liveApplyProfileChanges = true;

    [Header("Debug")]
    [SerializeField] private bool logAtmosphere;
    [SerializeField] private bool drawDebugGizmos = true;

    private Material runtimeProceduralSkybox;
    private LevelAtmosphereProfile currentProfile;
    private Transform currentRuntimeSceneRoot;
    private Light managedDirectionalLight;
    private LevelFogOfWarController fogOfWarController;
    private Coroutine blendRoutine;
    private bool hasAppliedAtmosphere;
#if UNITY_EDITOR
    private bool editorApplyQueued;
#endif

    public LevelAtmosphereProfile DefaultProfile => defaultProfile;
    public Light ManagedDirectionalLight => managedDirectionalLight;

    private void OnEnable()
    {
        LevelAtmosphereProfile.ProfileChanged += HandleAtmosphereProfileChanged;
    }

    private void OnDisable()
    {
        LevelAtmosphereProfile.ProfileChanged -= HandleAtmosphereProfileChanged;
    }

    private void Start()
    {
        if (applyDefaultOnStart)
        {
            Apply(defaultProfile, null);
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying && liveApplyProfileChanges)
        {
            QueueEditorApplyCurrentAtmosphere();
        }
    }

    [ContextMenu("Apply Current Atmosphere")]
    public void ApplyCurrentAtmosphere()
    {
        Apply(currentProfile != null ? currentProfile : defaultProfile, ResolveCurrentRuntimeSceneRoot());
    }

    public void ApplyPreviewProfile(LevelAtmosphereProfile profile)
    {
        Apply(profile, ResolveCurrentRuntimeSceneRoot());
    }

    public void ConfigureFogOfWar(LevelFogOfWarController controller)
    {
        if (controller != null)
        {
            fogOfWarController = controller;
        }
    }

    public void ClearRuntimeObjects(Transform runtimeSceneRoot = null)
    {
        if (blendRoutine != null)
        {
            StopCoroutine(blendRoutine);
            blendRoutine = null;
        }

        ResolveFogOfWarController();
        if (fogOfWarController != null)
        {
            fogOfWarController.ClearRuntimeObjects();
        }

        if (managedDirectionalLight != null && ShouldClearManagedLight(managedDirectionalLight, runtimeSceneRoot))
        {
            if (RenderSettings.sun == managedDirectionalLight)
            {
                RenderSettings.sun = null;
            }

            DestroyRuntime(managedDirectionalLight.gameObject);
            managedDirectionalLight = null;
        }

        hasAppliedAtmosphere = false;
        currentRuntimeSceneRoot = null;
    }

    public Light Apply(LevelAtmosphereProfile profile, Transform runtimeSceneRoot)
    {
        LevelAtmosphereProfile activeProfile = profile != null ? profile : defaultProfile;
        if (activeProfile == null)
        {
            Log("No atmosphere profile assigned. Render settings left unchanged.");
            return null;
        }

        currentRuntimeSceneRoot = runtimeSceneRoot != null ? runtimeSceneRoot : ResolveCurrentRuntimeSceneRoot();

        Light sunLight = null;
        if (activeProfile.manageDirectionalLight)
        {
            sunLight = EnsureDirectionalLight(activeProfile, currentRuntimeSceneRoot);
            DisableDuplicateDirectionalLights(activeProfile, sunLight);
        }

        bool shouldBlend = Application.isPlaying &&
            hasAppliedAtmosphere &&
            activeProfile.useRuntimeBlend &&
            activeProfile.blendDuration > 0f;

        if (blendRoutine != null)
        {
            StopCoroutine(blendRoutine);
            blendRoutine = null;
        }

        if (shouldBlend)
        {
            AtmosphereState startState = AtmosphereState.Capture(sunLight);
            blendRoutine = StartCoroutine(BlendToProfile(activeProfile, sunLight, startState));
        }
        else
        {
            ApplyDirectionalLight(activeProfile, sunLight);
            ApplyRenderSettings(activeProfile, sunLight);
        }

        ApplyFogOfWar(activeProfile);
        currentProfile = activeProfile;
        hasAppliedAtmosphere = true;
        Log($"Applied '{activeProfile.name}' using {(sunLight != null ? sunLight.name : "no managed directional light")}.");
        return sunLight;
    }

    private Light EnsureDirectionalLight(LevelAtmosphereProfile profile, Transform runtimeSceneRoot)
    {
        Transform parent = runtimeSceneRoot != null ? runtimeSceneRoot : transform;
        string lightName = string.IsNullOrWhiteSpace(profile.directionalLightName)
            ? "Directional Light"
            : profile.directionalLightName;

        if (managedDirectionalLight != null)
        {
            if (runtimeSceneRoot != null &&
                managedDirectionalLight.gameObject.scene != runtimeSceneRoot.gameObject.scene &&
                profile.preferExistingDirectionalLight)
            {
                Light sceneLight = FindBestExistingDirectionalLight(runtimeSceneRoot, lightName);
                if (sceneLight != null && sceneLight.gameObject.scene == runtimeSceneRoot.gameObject.scene)
                {
                    MoveLightUnderRuntimeRootIfSafe(sceneLight, runtimeSceneRoot);
                    managedDirectionalLight = sceneLight;
                    return managedDirectionalLight;
                }
            }

            return managedDirectionalLight;
        }

        Transform existing = parent.Find(lightName);
        Light directionalLight = existing != null ? existing.GetComponent<Light>() : null;
        if (directionalLight != null)
        {
            managedDirectionalLight = directionalLight;
            return directionalLight;
        }

        if (profile.preferExistingDirectionalLight)
        {
            directionalLight = FindBestExistingDirectionalLight(runtimeSceneRoot, lightName);
            if (directionalLight != null)
            {
                MoveLightUnderRuntimeRootIfSafe(directionalLight, runtimeSceneRoot);
                managedDirectionalLight = directionalLight;
                return directionalLight;
            }
        }

        GameObject lightObject = new GameObject(lightName);
        if (runtimeSceneRoot != null && runtimeSceneRoot.gameObject.scene.IsValid())
        {
            SceneManager.MoveGameObjectToScene(lightObject, runtimeSceneRoot.gameObject.scene);
        }

        lightObject.transform.SetParent(parent, false);
        managedDirectionalLight = lightObject.AddComponent<Light>();
        return managedDirectionalLight;
    }

    private static void ApplyDirectionalLight(LevelAtmosphereProfile profile, Light directionalLight)
    {
        if (directionalLight == null)
            return;

        UniversalAdditionalLightData additionalLightData = directionalLight.GetComponent<UniversalAdditionalLightData>();
        if (additionalLightData == null)
        {
            additionalLightData = directionalLight.gameObject.AddComponent<UniversalAdditionalLightData>();
        }

        directionalLight.gameObject.SetActive(true);
        directionalLight.enabled = true;
        directionalLight.type = LightType.Directional;
        directionalLight.color = profile.directionalLightColor;
        directionalLight.intensity = profile.directionalLightIntensity;
        directionalLight.bounceIntensity = profile.directionalLightIndirectMultiplier;
        directionalLight.shadowStrength = profile.directionalLightShadowStrength;
        directionalLight.shadows = profile.directionalLightShadows;
        directionalLight.cullingMask = profile.directionalLightCullingMask;
        directionalLight.renderMode = LightRenderMode.Auto;
        directionalLight.transform.localPosition = Vector3.zero;
        directionalLight.transform.rotation = Quaternion.Euler(profile.directionalLightEulerAngles);
        additionalLightData.usePipelineSettings = profile.useUrpPipelineLightSettings;
    }

    private void ApplyRenderSettings(LevelAtmosphereProfile profile, Light sunLight)
    {
        Material skybox = ResolveSkybox(profile);
        if (skybox != null)
        {
            RenderSettings.skybox = skybox;
        }

        if (sunLight != null)
        {
            RenderSettings.sun = sunLight;
        }

        if (profile.applyAmbient)
        {
            RenderSettings.ambientMode = profile.ambientMode;
            RenderSettings.ambientLight = profile.ambientLightColor;
            RenderSettings.ambientSkyColor = profile.ambientSkyColor;
            RenderSettings.ambientEquatorColor = profile.ambientEquatorColor;
            RenderSettings.ambientGroundColor = profile.ambientGroundColor;
            RenderSettings.ambientIntensity = profile.ambientIntensity;
            RenderSettings.subtractiveShadowColor = profile.subtractiveShadowColor;
        }

        RenderSettings.defaultReflectionMode = profile.defaultReflectionMode;
        RenderSettings.defaultReflectionResolution = profile.defaultReflectionResolution;
        RenderSettings.reflectionIntensity = profile.reflectionIntensity;

        if (profile.applyUnityFog)
        {
            RenderSettings.fog = profile.unityFogEnabled;
            RenderSettings.fogColor = profile.unityFogColor;
            RenderSettings.fogMode = profile.unityFogMode;
            RenderSettings.fogDensity = profile.unityFogDensity;
            RenderSettings.fogStartDistance = profile.unityFogStartDistance;
            RenderSettings.fogEndDistance = profile.unityFogEndDistance;
        }

        if (profile.updateDynamicGI)
        {
            DynamicGI.UpdateEnvironment();
        }
    }

    private System.Collections.IEnumerator BlendToProfile(LevelAtmosphereProfile profile, Light sunLight, AtmosphereState startState)
    {
        if (sunLight != null)
        {
            ApplyDirectionalLight(profile, sunLight);
        }

        ApplyRenderSettings(profile, sunLight);

        float duration = Mathf.Max(0.01f, profile.blendDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float t = profile.blendCurve != null && profile.blendCurve.length > 0
                ? Mathf.Clamp01(profile.blendCurve.Evaluate(normalizedTime))
                : normalizedTime;
            ApplyInterpolatedState(profile, sunLight, startState, t);
            yield return null;
        }

        ApplyDirectionalLight(profile, sunLight);
        ApplyRenderSettings(profile, sunLight);
        blendRoutine = null;
    }

    private static void ApplyInterpolatedState(LevelAtmosphereProfile profile, Light sunLight, AtmosphereState startState, float t)
    {
        if (sunLight != null && profile.manageDirectionalLight)
        {
            sunLight.color = Color.Lerp(startState.directionalLightColor, profile.directionalLightColor, t);
            sunLight.intensity = Mathf.Lerp(startState.directionalLightIntensity, profile.directionalLightIntensity, t);
            sunLight.bounceIntensity = Mathf.Lerp(startState.directionalLightIndirectMultiplier, profile.directionalLightIndirectMultiplier, t);
            sunLight.shadowStrength = Mathf.Lerp(startState.directionalLightShadowStrength, profile.directionalLightShadowStrength, t);
            sunLight.transform.rotation = Quaternion.Slerp(startState.directionalLightRotation, Quaternion.Euler(profile.directionalLightEulerAngles), t);
        }

        if (profile.applyAmbient)
        {
            RenderSettings.ambientLight = Color.Lerp(startState.ambientLightColor, profile.ambientLightColor, t);
            RenderSettings.ambientSkyColor = Color.Lerp(startState.ambientSkyColor, profile.ambientSkyColor, t);
            RenderSettings.ambientEquatorColor = Color.Lerp(startState.ambientEquatorColor, profile.ambientEquatorColor, t);
            RenderSettings.ambientGroundColor = Color.Lerp(startState.ambientGroundColor, profile.ambientGroundColor, t);
            RenderSettings.ambientIntensity = Mathf.Lerp(startState.ambientIntensity, profile.ambientIntensity, t);
            RenderSettings.subtractiveShadowColor = Color.Lerp(startState.subtractiveShadowColor, profile.subtractiveShadowColor, t);
        }

        if (profile.applyUnityFog)
        {
            RenderSettings.fogColor = Color.Lerp(startState.fogColor, profile.unityFogColor, t);
            RenderSettings.fogDensity = Mathf.Lerp(startState.fogDensity, profile.unityFogDensity, t);
            RenderSettings.fogStartDistance = Mathf.Lerp(startState.fogStartDistance, profile.unityFogStartDistance, t);
            RenderSettings.fogEndDistance = Mathf.Lerp(startState.fogEndDistance, profile.unityFogEndDistance, t);
        }
    }

    private void ApplyFogOfWar(LevelAtmosphereProfile profile)
    {
        if (profile == null || profile.fogOfWarProfile == null)
            return;

        ResolveFogOfWarController();
        if (fogOfWarController == null)
        {
            Log($"Fog of War profile '{profile.fogOfWarProfile.name}' assigned, but no LevelFogOfWarController was found.");
            return;
        }

        fogOfWarController.Apply(profile.fogOfWarProfile, currentRuntimeSceneRoot);
    }

    private void DisableDuplicateDirectionalLights(LevelAtmosphereProfile profile, Light keepLight)
    {
        if (!profile.disableDuplicateDirectionalLights || keepLight == null)
            return;

        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null || light == keepLight || light.type != LightType.Directional)
                continue;

            light.enabled = false;
            Log($"Disabled duplicate directional light '{light.name}'.");
        }
    }

    private Light FindBestExistingDirectionalLight(Transform runtimeSceneRoot, string lightName)
    {
        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Light sameSceneCandidate = null;
        Light namedCandidate = null;
        Light fallbackCandidate = null;
        Scene targetScene = runtimeSceneRoot != null ? runtimeSceneRoot.gameObject.scene : SceneManager.GetActiveScene();

        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null || light.type != LightType.Directional)
                continue;

            if (runtimeSceneRoot != null && light.transform.IsChildOf(runtimeSceneRoot))
                return light;

            if (light.gameObject.scene == targetScene)
            {
                if (string.Equals(light.name, lightName, System.StringComparison.OrdinalIgnoreCase))
                    return light;

                sameSceneCandidate ??= light;
            }

            if (string.Equals(light.name, lightName, System.StringComparison.OrdinalIgnoreCase))
                namedCandidate ??= light;

            fallbackCandidate ??= light;
        }

        return sameSceneCandidate != null ? sameSceneCandidate : namedCandidate != null ? namedCandidate : fallbackCandidate;
    }

    private static void MoveLightUnderRuntimeRootIfSafe(Light directionalLight, Transform runtimeSceneRoot)
    {
        if (!Application.isPlaying || directionalLight == null || runtimeSceneRoot == null)
            return;

        if (directionalLight.transform.IsChildOf(runtimeSceneRoot))
            return;

        if (directionalLight.transform == runtimeSceneRoot || runtimeSceneRoot.IsChildOf(directionalLight.transform))
            return;

        if (directionalLight.gameObject.scene != runtimeSceneRoot.gameObject.scene)
        {
            directionalLight.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(directionalLight.gameObject, runtimeSceneRoot.gameObject.scene);
        }

        directionalLight.transform.SetParent(runtimeSceneRoot, true);
    }

    private Transform ResolveCurrentRuntimeSceneRoot()
    {
        if (currentRuntimeSceneRoot != null)
            return currentRuntimeSceneRoot;

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isLoaded)
        {
            GameObject[] roots = activeScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root != null && root.name == "Level_Runtime")
                {
                    currentRuntimeSceneRoot = root.transform;
                    return currentRuntimeSceneRoot;
                }
            }
        }

        return null;
    }

    private void HandleAtmosphereProfileChanged(LevelAtmosphereProfile changedProfile)
    {
        if (!Application.isPlaying || !liveApplyProfileChanges || changedProfile == null)
            return;

        LevelAtmosphereProfile activeProfile = currentProfile != null ? currentProfile : defaultProfile;
        if (activeProfile != changedProfile)
            return;

        QueueEditorApplyCurrentAtmosphere();
    }

    private Material ResolveSkybox(LevelAtmosphereProfile profile)
    {
        switch (profile.skyboxMode)
        {
            case LevelSkyboxMode.KeepCurrent:
                return RenderSettings.skybox;
            case LevelSkyboxMode.UseMaterial:
                return profile.skyboxMaterial != null ? profile.skyboxMaterial : RenderSettings.skybox;
            case LevelSkyboxMode.ProceduralColors:
                return GetProceduralSkybox(profile);
            default:
                return RenderSettings.skybox;
        }
    }

    private Material GetProceduralSkybox(LevelAtmosphereProfile profile)
    {
        if (runtimeProceduralSkybox == null)
        {
            Shader shader = Shader.Find(ProceduralSkyboxShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Level Atmosphere] Could not find shader '{ProceduralSkyboxShaderName}'. Existing skybox kept.", this);
                return RenderSettings.skybox;
            }

            runtimeProceduralSkybox = new Material(shader)
            {
                name = "Runtime Procedural Skybox"
            };
        }

        SetColorIfPresent(runtimeProceduralSkybox, SkyTintProperty, profile.proceduralSkyTint);
        SetColorIfPresent(runtimeProceduralSkybox, GroundColorProperty, profile.proceduralGroundColor);
        SetFloatIfPresent(runtimeProceduralSkybox, ExposureProperty, profile.proceduralExposure);
        SetFloatIfPresent(runtimeProceduralSkybox, AtmosphereThicknessProperty, profile.proceduralAtmosphereThickness);
        return runtimeProceduralSkybox;
    }

    private static void SetColorIfPresent(Material material, string propertyName, Color value)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, value);
        }
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private void Log(string message)
    {
        if (logAtmosphere)
        {
            Debug.Log($"[Level Atmosphere] {message}", this);
        }
    }

    private static bool ShouldClearManagedLight(Light directionalLight, Transform runtimeSceneRoot)
    {
        if (directionalLight == null)
            return false;

        if (runtimeSceneRoot == null)
            return true;

        return directionalLight.transform == runtimeSceneRoot ||
            directionalLight.transform.IsChildOf(runtimeSceneRoot);
    }

    private static void DestroyRuntime(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void QueueEditorApplyCurrentAtmosphere()
    {
#if UNITY_EDITOR
        if (editorApplyQueued)
            return;

        editorApplyQueued = true;
        EditorApplication.delayCall += ApplyQueuedEditorAtmosphere;
#endif
    }

#if UNITY_EDITOR
    private void ApplyQueuedEditorAtmosphere()
    {
        editorApplyQueued = false;
        if (this == null || !Application.isPlaying || !liveApplyProfileChanges)
            return;

        ApplyCurrentAtmosphere();
    }
#endif

    private void ResolveFogOfWarController()
    {
        if (fogOfWarController != null)
            return;

        if (LevelSystemController.Instance != null && LevelSystemController.Instance.FogOfWar != null)
        {
            fogOfWarController = LevelSystemController.Instance.FogOfWar;
            return;
        }

        Transform runtimeRoot = currentRuntimeSceneRoot != null ? currentRuntimeSceneRoot : ResolveCurrentRuntimeSceneRoot();
        if (runtimeRoot != null)
        {
            fogOfWarController = runtimeRoot.GetComponentInChildren<LevelFogOfWarController>(true);
            if (fogOfWarController != null)
                return;
        }

        fogOfWarController = FindFirstObjectByType<LevelFogOfWarController>(FindObjectsInactive.Include);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos || managedDirectionalLight == null)
            return;

        Gizmos.color = Color.yellow;
        Vector3 position = managedDirectionalLight.transform.position;
        Vector3 direction = managedDirectionalLight.transform.forward;
        Gizmos.DrawLine(position, position + direction * 5f);
        Gizmos.DrawWireSphere(position, 0.35f);
    }

    private readonly struct AtmosphereState
    {
        public readonly Color directionalLightColor;
        public readonly float directionalLightIntensity;
        public readonly float directionalLightIndirectMultiplier;
        public readonly float directionalLightShadowStrength;
        public readonly Quaternion directionalLightRotation;
        public readonly Color ambientLightColor;
        public readonly Color ambientSkyColor;
        public readonly Color ambientEquatorColor;
        public readonly Color ambientGroundColor;
        public readonly float ambientIntensity;
        public readonly Color subtractiveShadowColor;
        public readonly Color fogColor;
        public readonly float fogDensity;
        public readonly float fogStartDistance;
        public readonly float fogEndDistance;

        private AtmosphereState(Light sunLight)
        {
            directionalLightColor = sunLight != null ? sunLight.color : Color.white;
            directionalLightIntensity = sunLight != null ? sunLight.intensity : 1f;
            directionalLightIndirectMultiplier = sunLight != null ? sunLight.bounceIntensity : 1f;
            directionalLightShadowStrength = sunLight != null ? sunLight.shadowStrength : 1f;
            directionalLightRotation = sunLight != null ? sunLight.transform.rotation : Quaternion.identity;
            ambientLightColor = RenderSettings.ambientLight;
            ambientSkyColor = RenderSettings.ambientSkyColor;
            ambientEquatorColor = RenderSettings.ambientEquatorColor;
            ambientGroundColor = RenderSettings.ambientGroundColor;
            ambientIntensity = RenderSettings.ambientIntensity;
            subtractiveShadowColor = RenderSettings.subtractiveShadowColor;
            fogColor = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity;
            fogStartDistance = RenderSettings.fogStartDistance;
            fogEndDistance = RenderSettings.fogEndDistance;
        }

        public static AtmosphereState Capture(Light sunLight)
        {
            return new AtmosphereState(sunLight);
        }
    }
}
