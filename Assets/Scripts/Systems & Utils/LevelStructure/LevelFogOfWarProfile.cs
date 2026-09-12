using UnityEngine;

public enum LevelFogOfWarRenderMode
{
    ScreenOverlay,
    WorldOverlay
}

[CreateAssetMenu(menuName = "SO/PCG/Profiles/Tuning/Atmosphere/Fog Of War Profile", fileName = "SO_LevelFogOfWar")]
public sealed class LevelFogOfWarProfile : ScriptableObject
{
    public static event System.Action<LevelFogOfWarProfile> ProfileChanged;

    [Header("State")]
    public bool enabled = true;
    public LevelFogOfWarRenderMode renderMode = LevelFogOfWarRenderMode.ScreenOverlay;
    public bool clearOnApply = true;
    public bool updateEveryFrame = true;

    [Min(0.02f)]
    public float updateInterval = 0.05f;

    [Header("Reveal")]
    [Tooltip("For screen-space fog, darkens the view by world distance from the player instead of using a fixed screen circle.")]
    public bool usePlayerDistanceFog = true;

    [Min(0f)]
    public float revealRadius = 14f;

    [Min(0f)]
    public float edgeSoftness = 12f;

    [Range(0f, 1f)]
    public float visibleAlpha = 0f;

    [Range(0f, 1f)]
    public float exploredAlpha = 0.25f;

    [Range(0f, 1f)]
    public float unexploredAlpha = 0.62f;

    public Color fogColor = Color.black;

    [Header("Screen Overlay")]
    [Tooltip("Uses the URP camera depth texture to darken rendered world surfaces by distance from the reveal target.")]
    public bool useDepthTextureFog = true;

    [Tooltip("Keeps the reveal centered in the camera. Use this for player-follow cameras to avoid fog wobble from small camera pitch changes.")]
    public bool lockScreenRevealToViewCenter = true;

    [Tooltip("Uses a more exact world-plane projection for screen fog. This is heavier and can wobble with camera pitch changes.")]
    public bool projectScreenFogOnWorldPlane;

    [Tooltip("Small viewport movement that is ignored before rebuilding the screen fog texture.")]
    [Range(0f, 0.1f)]
    public float screenUpdateThreshold = 0.01f;

    [Tooltip("Screen-space reveal radius used by the lightweight screen overlay.")]
    [Range(0.02f, 1f)]
    public float screenRevealRadius = 0.28f;

    public int screenSortingOrder = 120;

    [Tooltip("Y offset from the reveal target used as the distance fog ground plane.")]
    public float screenDistancePlaneYOffset;

    [Header("World")]
    public Vector3 worldCenter = Vector3.zero;
    public Vector2 worldSize = new Vector2(140f, 140f);
    public float overlayHeight = 0.15f;

    [Range(64, 1024)]
    public int textureResolution = 192;

    [Header("Target")]
    public bool autoFindPlayerByTag = true;
    public string playerTag = "Player";
    public bool hideOverlayUntilRevealTargetFound = true;

    [Header("Debug")]
    public bool drawDebugGizmos = true;

    private void OnValidate()
    {
        updateInterval = Mathf.Max(0.02f, updateInterval);
        revealRadius = Mathf.Max(0f, revealRadius);
        edgeSoftness = Mathf.Max(0f, edgeSoftness);
        screenUpdateThreshold = Mathf.Clamp(screenUpdateThreshold, 0f, 0.1f);
        screenRevealRadius = Mathf.Clamp(screenRevealRadius, 0.02f, 1f);
        worldSize.x = Mathf.Max(1f, worldSize.x);
        worldSize.y = Mathf.Max(1f, worldSize.y);
        ProfileChanged?.Invoke(this);
    }
}
