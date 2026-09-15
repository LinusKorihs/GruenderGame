using UnityEngine;

[CreateAssetMenu(menuName = "SO/Minions/Minion Commander Settings", fileName = "PlayerMinionCommanderSettings")]
public class PlayerMinionCommanderSettings : ScriptableObject
{
    [Header("Target Query")]
    public float commandAcquireRadius = 1.1f;
    public float previewAcquireRadius = 0.35f;
    [Tooltip("Extra horizontal cursor radius for enemy commands. Helps target flying enemies above the ground cursor.")]
    public float enemyCommandAcquireRadius = 2.2f;
    [Tooltip("Extra horizontal cursor radius for enemy preview tint. Keep this lower than the command radius so preview stays precise.")]
    public float enemyPreviewAcquireRadius = 1.4f;
    [Tooltip("Vertical capsule height above the ground cursor used to find airborne enemies.")]
    public float enemyAcquireHeight = 8f;
    public LayerMask commandTargetMask = ~0;
    public bool includeTriggers = false;
    public string enemyTag = "Enemy";
    public string breakableTag = "Breakable";

    [Header("Auto Find")]
    public float autoFindRefreshInterval = 0.5f;

    [Header("Target Visibility")]
    [Tooltip("Layers that block LOS between player and command targets (walls, terrain). Exclude character layers.")]
    public LayerMask cursorLOSBlockMask = ~0;
    [Tooltip("When true, targets behind walls cannot be commanded even if the cursor overlap detects them.")]
    public bool requireLineOfSightForCursorTargets = true;
    [Tooltip("Height offset used for the LOS ray from the player.")]
    public float cursorLOSHeightOffset = 0.8f;

    [Header("Call / Dismiss")]
    [Tooltip("Radius of the Call impulse wave and the maximum range for Dismiss to affect minions.")]
    public float callRange = 10f;
    [Tooltip("Maximum range for Dismiss to affect minions. Use 0 or less to fall back to callRange.")]
    public float dismissRange = 10f;
    [Tooltip("When a dismissed minion is farther than this from the player it automatically starts following again (should equal callRange).")]
    public float dismissResumeFollowRange = 10f;
    [Tooltip("Forward/back offset for the dismiss formation relative to the player facing. Negative is behind the player, 0 keeps the formation around the player.")]
    public float dismissFormationForwardOffset = 0f;
    [Tooltip("Distance between each role group's centre point in the dismiss formation (Melee / Ranged / Support spread sideways).")]
    public float dismissFormationGroupSpacing = 2f;
    [Tooltip("Spacing between individual minions within the same role group.")]
    public float dismissFormationMemberSpacing = 1.2f;
    [Tooltip("Minimum spacing between resolved dismiss slots after NavMesh snapping. Prevents several minions from collapsing onto one sampled edge point.")]
    public float dismissFormationMinResolvedSpacing = 0.6f;
    [Tooltip("Extra fallback rings searched around each role group if the preferred slot is blocked or off the NavMesh.")]
    [Range(0, 4)] public int dismissFormationFallbackRings = 2;
    [Tooltip("Color of the call/dismiss range Gizmo sphere drawn in the editor.")]
    public Color callRangeGizmoColor = new Color(0.2f, 0.7f, 1f, 0.25f);

    [Header("Position Commands")]
    [Tooltip("When true, cursor move and dismiss targets must resolve to a reachable NavMesh point before a command is issued.")]
    public bool validatePositionCommandsWithNavMesh = true;
    [Tooltip("Radius used to snap cursor/formation command positions onto the nearest NavMesh point.")]
    public float positionCommandNavSampleRadius = 1.25f;
    [Tooltip("When true, direct position commands are still allowed if the minion itself is not on any NavMesh. Keeps simple non-NavMesh test scenes usable.")]
    public bool allowDirectPositionCommandsWhenMinionOffNavMesh = true;
    [Tooltip("When true, Call also recalls minions outside the wave radius if they are in player-issued position commands or idle after a failed position command.")]
    public bool callRecoversPlayerPositionCommandsOutsideRange = true;
    [Tooltip("When true, Call recalls every registered live minion. Useful in large boss rooms where engaged minions can be outside the local pulse radius.")]
    public bool callRecallsAllRegisteredMinions = true;
    [Tooltip("When true, Dismiss sends every registered live minion into formation, even if it is outside the local dismiss radius.")]
    public bool dismissAffectsAllRegisteredMinions = true;
    [Tooltip("When true, dismissed slots keep following the player. Leave off for stable one-shot dismiss groups.")]
    public bool trackDismissFormationWithPlayer = false;

    [Header("Call / Dismiss Visuals")]
    public bool enableCallDismissPulse = true;
    public Color callPulseColor = new Color(0.25f, 0.75f, 1f, 0.9f);
    public Color dismissPulseColor = new Color(0.65f, 1f, 0.35f, 0.9f);
    [Tooltip("Seconds the pulse remains visible.")]
    public float pulseDuration = 0.55f;
    [Tooltip("Initial radius of the visible ring.")]
    public float pulseStartRadius = 0.35f;
    [Tooltip("Final ring radius. Use 0 to match callRange.")]
    public float pulseEndRadius = 0f;
    [Tooltip("Width of the expanding ring line.")]
    public float pulseRingWidth = 0.18f;
    [Tooltip("Height above the player pivot where the ring is drawn.")]
    public float pulseGroundOffset = 0.08f;
    [Tooltip("Temporary point light intensity added while the pulse expands.")]
    public float pulseLightIntensity = 1.4f;

    [Header("Formation Tracking")]
    [Tooltip("How often (in seconds) dismissed formation slots are recomputed to track player movement and rotation.")]
    public float formationUpdateInterval = 0.1f;
    [Tooltip("Minimum distance (metres) a slot must move before its target position is updated. Reduces micro-jitter.")]
    public float formationUpdateThreshold = 0.05f;

    [Header("Command Preview")]
    public bool enableCommandPreview = true;
    public Color previewNoTargetColor = new Color(0.65f, 0.65f, 0.65f, 1f);
    public Color previewAttackColor = new Color(1f, 0.25f, 0.25f, 1f);
    public Color previewSupportColor = new Color(0.2f, 0.9f, 0.45f, 1f);
    public Color previewInvalidColor = new Color(0.35f, 0.55f, 1f, 1f);
    [Range(0f, 4f)] public float previewEmissionIntensity = 0.35f;
}
