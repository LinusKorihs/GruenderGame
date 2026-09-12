using UnityEngine;
using UnityEngine.Rendering;

public enum LevelSkyboxMode
{
    KeepCurrent,
    UseMaterial,
    ProceduralColors
}

[CreateAssetMenu(menuName = "SO/PCG/Profiles/Tuning/Atmosphere/Atmosphere Profile", fileName = "SO_LevelAtmosphere")]
public sealed class LevelAtmosphereProfile : ScriptableObject
{
    public static event System.Action<LevelAtmosphereProfile> ProfileChanged;

    [Header("Skybox")]
    [Tooltip("Keep the current skybox, use a material asset, or build a procedural runtime skybox from the colors below.")]
    public LevelSkyboxMode skyboxMode = LevelSkyboxMode.ProceduralColors;

    [Tooltip("Used when Skybox Mode is Use Material.")]
    public Material skyboxMaterial;

    [ColorUsage(false, true)]
    public Color proceduralSkyTint = new Color(0.52f, 0.58f, 0.7f, 1f);

    [ColorUsage(false, true)]
    public Color proceduralGroundColor = new Color(0.12f, 0.14f, 0.16f, 1f);

    [Range(0f, 8f)]
    public float proceduralExposure = 1f;

    [Range(0f, 5f)]
    public float proceduralAtmosphereThickness = 1f;

    [Header("Directional Light")]
    [Tooltip("Creates or updates a directional light in the active runtime level root.")]
    public bool manageDirectionalLight = true;

    [Tooltip("Uses an existing directional light in the active runtime scene before creating a new one.")]
    public bool preferExistingDirectionalLight = true;

    [Tooltip("Disables other active directional lights after this profile has selected its managed sun light.")]
    public bool disableDuplicateDirectionalLights = true;

    public string directionalLightName = "Directional Light";

    public Vector3 directionalLightEulerAngles = new Vector3(50f, -30f, 0f);

    [ColorUsage(false, true)]
    public Color directionalLightColor = new Color(1f, 0.95686275f, 0.8392157f, 1f);

    [Min(0f)]
    public float directionalLightIntensity = 1f;

    [Min(0f)]
    public float directionalLightIndirectMultiplier = 1f;

    [Range(0f, 1f)]
    public float directionalLightShadowStrength = 1f;

    public LightShadows directionalLightShadows = LightShadows.Soft;

    public LayerMask directionalLightCullingMask = ~0;

    [Tooltip("Lets URP use the pipeline asset settings for the generated directional light.")]
    public bool useUrpPipelineLightSettings = true;

    [Header("Transition")]
    [Tooltip("Blends light, ambient and Unity fog values when switching from one atmosphere profile to another at runtime.")]
    public bool useRuntimeBlend;

    [Min(0f)]
    public float blendDuration = 1f;

    public AnimationCurve blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Ambient")]
    public bool applyAmbient = true;
    public AmbientMode ambientMode = AmbientMode.Trilight;

    [ColorUsage(false, true)]
    public Color ambientLightColor = new Color(0.212f, 0.227f, 0.259f, 1f);

    [ColorUsage(false, true)]
    public Color ambientSkyColor = new Color(0.212f, 0.227f, 0.259f, 1f);

    [ColorUsage(false, true)]
    public Color ambientEquatorColor = new Color(0.114f, 0.125f, 0.133f, 1f);

    [ColorUsage(false, true)]
    public Color ambientGroundColor = new Color(0.047f, 0.043f, 0.035f, 1f);

    [Min(0f)]
    public float ambientIntensity = 1f;

    [ColorUsage(false, true)]
    public Color subtractiveShadowColor = new Color(0.42f, 0.478f, 0.627f, 1f);

    [Header("Reflections")]
    public DefaultReflectionMode defaultReflectionMode = DefaultReflectionMode.Skybox;

    [Range(16, 2048)]
    public int defaultReflectionResolution = 128;

    [Min(0f)]
    public float reflectionIntensity = 1f;

    [Header("Unity Fog")]
    [Tooltip("This is normal Unity scene fog, not gameplay Fog of War.")]
    public bool applyUnityFog = true;

    public bool unityFogEnabled;

    [ColorUsage(false, true)]
    public Color unityFogColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    public FogMode unityFogMode = FogMode.ExponentialSquared;

    [Min(0f)]
    public float unityFogDensity = 0.01f;

    public float unityFogStartDistance;
    public float unityFogEndDistance = 300f;

    [Header("Fog Of War")]
    [Tooltip("Optional gameplay Fog of War profile. Applied through a LevelFogOfWarController when one exists in the scene/LevelSystem.")]
    public LevelFogOfWarProfile fogOfWarProfile;

    [Header("Runtime Update")]
    [Tooltip("Required after playmode skybox changes so Unity refreshes the ambient probe.")]
    public bool updateDynamicGI = true;

    private void OnValidate()
    {
        ProfileChanged?.Invoke(this);
    }
}
