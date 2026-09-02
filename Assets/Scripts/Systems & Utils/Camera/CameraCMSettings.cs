using System;
using UnityEngine;

public enum CameraStage
{
    Wide,
    Default,
    Close
}

[Serializable]
public struct CameraStageSettings
{
    [Tooltip("Name shown in debug logs and editor buttons.")]
    public string displayName;

    [Tooltip("Distance from the camera target. Higher values zoom the camera out.")]
    public float radius;

    [Tooltip("Vertical orbit angle in degrees. Higher values place the camera more above the player, lower values closer behind the player.")]
    public float vertical;

    [Tooltip("Cinemachine lens field of view for this stage. Higher values show more of the room but add more perspective distortion.")]
    public float fieldOfView;

    [Tooltip("Offset added to the followed camera target. X/Z shift the framing in world space, Y raises or lowers the framing.")]
    public Vector3 targetOffset;

    [Tooltip("Lowest allowed manual vertical orbit angle for this stage. Lower values move the camera closer behind the player.")]
    public float minVertical;

    [Tooltip("Highest allowed manual vertical orbit angle for this stage. Higher values move the camera more above the player.")]
    public float maxVertical;

    [Tooltip("If enabled, manual left/right camera rotation is clamped for this stage.")]
    public bool limitHorizontalRotation;

    [Tooltip("Left horizontal limit in degrees for this stage. Negative values are left of the zero angle.")]
    public float minHorizontal;

    [Tooltip("Right horizontal limit in degrees for this stage. Positive values are right of the zero angle.")]
    public float maxHorizontal;

    [HideInInspector] public float targetOffsetY;

    public static CameraStageSettings Create(string displayName, float radius, float vertical, float fieldOfView, float targetOffsetY)
    {
        return new CameraStageSettings
        {
            displayName = displayName,
            radius = radius,
            vertical = vertical,
            fieldOfView = fieldOfView,
            targetOffset = new Vector3(0f, targetOffsetY, 0f),
            minVertical = 5f,
            maxVertical = 85f,
            limitHorizontalRotation = false,
            minHorizontal = -120f,
            maxHorizontal = 120f,
            targetOffsetY = targetOffsetY
        };
    }
}

[CreateAssetMenu(menuName = "SO/Camera/CM Settings", fileName = "CameraCMSettings")]
public class CameraCMSettings : ScriptableObject
{
    [Header("Stages")]
    public CameraStageSettings wideStage = CameraStageSettings.Create("Wide", 12f, 70f, 40f, 1.25f);
    public CameraStageSettings defaultStage = CameraStageSettings.Create("Default", 8f, 58f, 40f, 1f);
    public CameraStageSettings closeStage = CameraStageSettings.Create("Close", 5f, 35f, 40f, 0.75f);

    [Header("Tuning")]
    [Tooltip("How quickly the camera blends when changing stage, target offset, vertical angle, or FOV.")]
    public float transitionSpeed = 6f;

    [Tooltip("Legacy zoom value kept for old references. Current zoom input uses Mouse Zoom Speed and Gamepad Zoom Speed.")]
    public float zoomSpeed = 0.02f;

    [Tooltip("Smallest allowed camera radius after zoom or stage changes.")]
    public float minRadius = 2.5f;

    [Tooltip("Largest allowed camera radius after zoom or stage changes.")]
    public float maxRadius = 16f;

    [Tooltip("When enabled, changing the current stage values in Play Mode updates the scene camera without pressing a button.")]
    public bool liveStageTuning = true;

    [Header("Zoom")]
    [Tooltip("Mouse wheel zoom amount. Higher values zoom faster per scroll tick.")]
    public float mouseZoomSpeed = 0.02f;

    [Tooltip("Gamepad zoom speed per second.")]
    public float gamepadZoomSpeed = 6f;

    [Tooltip("How quickly the camera radius reaches the requested zoom distance.")]
    public float zoomSmoothing = 10f;

    [Header("Occlusion - Occluders")]
    [Tooltip("Layers that can be faded or hidden when they are between camera and player.")]
    public LayerMask occluderMask = ~0;

    [Tooltip("If enabled, occluding renderers are made transparent. If disabled, occluding renderers are hidden completely.")]
    public bool makeTransparent = true;

    [Tooltip("Target alpha while transparent occlusion is active. 0.05 is almost invisible, 1 keeps the object opaque.")]
    [Range(0.05f, 1f)] public float transparentAlpha = 0.25f;

    [Header("Occlusion - Trigger Shape")]
    [Tooltip("Thickness of the capsule checked between camera and player. Higher values catch more corners but affect more objects.")]
    public float occlusionRadius = 0.35f;

    [Tooltip("Distance ignored directly in front of the camera so the camera itself does not over-trigger occlusion.")]
    public float paddingFromCamera = 0.2f;

    [Tooltip("Distance ignored directly before the player target so objects very close to the player do not flicker as much.")]
    public float paddingFromTarget = 0.1f;

    [Header("Occlusion - Robustness")]
    [Tooltip("If disabled, unsupported shaders stay visible instead of hiding the whole renderer.")]
    public bool hideWhenTransparencyUnsupported = false;

    [Tooltip("How many frames an occluder can miss trigger updates before it is force-restored.")]
    public int staleFramesToRestore = 2;

    [Header("Occlusion - Debug")]
    [Tooltip("Print camera and occlusion debug logs.")]
    public bool debugEnabled = true;

    [Tooltip("Draw the occlusion capsule line and endpoints in Scene View.")]
    public bool drawGizmos = true;

    [Header("Look (Right Stick / Mouse)")]
    [Tooltip("Legacy horizontal look sensitivity kept for old references. Current gamepad/mouse values are below.")]
    public float lookSensitivityX = 180f;

    [Tooltip("Legacy vertical look sensitivity kept for old references. Current gamepad/mouse values are below.")]
    public float lookSensitivityY = 120f;

    [Tooltip("Invert vertical camera input.")]
    public bool invertY = false;

    [Tooltip("Legacy delta-time toggle kept for old references. Current mouse/gamepad behavior is controlled below.")]
    public bool lookScaleWithDeltaTime = true;

    [HideInInspector]
    public float minVertical = 5f;

    [HideInInspector]
    public float maxVertical = 85f;

    [HideInInspector]
    public bool limitHorizontalRotation = false;

    [HideInInspector]
    public float minHorizontal = -120f;

    [HideInInspector]
    public float maxHorizontal = 120f;

    [Header("Lock-On")]
    [Tooltip("Tags that can be selected by camera lock-on.")]
    public string[] lockOnTags = new[] { "Enemy" };

    [Tooltip("Maximum distance from player or cursor origin where lock-on can find a target.")]
    public float lockOnMaxDistance = 25f;

    [Header("Lock-On Framing")]
    [Tooltip("0.5 = midpoint, lower values keep the frame closer to the player, higher values closer to the target.")]
    [Range(0f, 1f)] public float lockOnTargetWeight = 0.5f;
    [Tooltip("Local offset for the lock-on look target. X = sideways, Y = up, Z = toward target. Negative Z moves behind the player.")]
    public Vector3 lockOnFramingOffset = new Vector3(0f, 1f, -1f);

    [Tooltip("Legacy height offset kept for old scenes. Prefer Lock On Framing Offset Y for new tuning.")]
    public float lockOnHeightOffset = 1.25f;

    [Tooltip("How quickly the lock-on look target moves to its desired position.")]
    public float lockOnLookTargetSmooth = 10f;

    [Tooltip("How much extra radius is added per meter of player-target distance during lock-on.")]
    public float lockOnRadiusPerMeter = 0.18f;

    [Tooltip("Maximum extra radius lock-on can add on top of the current stage radius.")]
    public float lockOnMaxExtraRadius = 3f;

    [Header("Lock-On Camera Angle")]
    [Tooltip("Vertical orbit angle used while locked on.")]
    public float lockOnVertical = 42f;

    [Tooltip("How quickly the camera reaches the lock-on vertical angle.")]
    public float lockOnVerticalSmooth = 8f;

    [Tooltip("How quickly lock-on rotates the camera around the player to face the target.")]
    public float lockOnHorizontalSmooth = 12f;

    [Tooltip("Minimum camera radius while locked on.")]
    public float lockOnMinDistance = 6f;

    [Header("Look - Gamepad")]
    [Tooltip("Horizontal camera look speed for the right stick, in degrees per second.")]
    public float gamepadSensitivityX = 180f;

    [Tooltip("Vertical camera look speed for the right stick, in degrees per second.")]
    public float gamepadSensitivityY = 120f;

    [Header("Look - Mouse")]
    [Tooltip("Horizontal camera look multiplier for mouse delta.")]
    public float mouseSensitivityX = 0.15f;

    [Tooltip("Vertical camera look multiplier for mouse delta.")]
    public float mouseSensitivityY = 0.15f;

    [Tooltip("If enabled, mouse look is multiplied by delta time. Usually disabled for raw mouse delta.")]
    public bool mouseScaleWithDeltaTime = false;

    [HideInInspector] public float topRadius = 12f;
    [HideInInspector] public float topVertical = 75f;
    [HideInInspector] public float thirdRadius = 4f;
    [HideInInspector] public float thirdVertical = 25f;
    [HideInInspector] public float lockOnPlayerBias = 0.42f;

    public CameraStageSettings GetStage(CameraStage stage)
    {
        return stage switch
        {
            CameraStage.Wide => wideStage,
            CameraStage.Close => closeStage,
            _ => defaultStage
        };
    }

    private void OnValidate()
    {
        ValidateStageName(ref wideStage, "Wide");
        ValidateStageName(ref defaultStage, "Default");
        ValidateStageName(ref closeStage, "Close");

        minRadius = Mathf.Max(0.1f, minRadius);
        maxRadius = Mathf.Max(minRadius, maxRadius);
        minVertical = Mathf.Clamp(minVertical, 0f, 180f);
        maxVertical = Mathf.Clamp(maxVertical, minVertical, 180f);
        lockOnMinDistance = Mathf.Max(minRadius, lockOnMinDistance);
    }

    private void ValidateStageName(ref CameraStageSettings stage, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(stage.displayName)) stage.displayName = fallbackName;
    }
}
