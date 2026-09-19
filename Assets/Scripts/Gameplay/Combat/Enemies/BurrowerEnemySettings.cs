using UnityEngine;

/* <Summary / Notes>
    Shared configuration for the Burrower enemy (Enemy 3 — ground trap / aerial grabber).
    Create via Assets > Create > SO > Combat > Enemy > Burrower Enemy Settings.
*/
[CreateAssetMenu(menuName = "SO/Combat/Enemy/Burrower")]
public class BurrowerEnemySettings : ScriptableObject
{
    [Header("Tags")]
    public string PlayerTag = "Player";
    public string MinionTag = "Ally";

    [Header("Detection (Aerial)")]
    [Tooltip("Radius used while airborne to find attack targets.")]
    public float DetectRadius = 14f;
    public float ForgetRadius = 20f;
    public LayerMask DetectMask = ~0;

    [Header("Snap Trigger (Burrowed)")]
    [Tooltip("Radius that triggers the snap when a player or minion walks over the enemy.")]
    public float SnapRadius = 1.2f;
    [Tooltip("Damage dealt to the triggering target by the snap.")]
    public float SnapDamage = 8f;
    [Tooltip("Duration of the snap pause before the enemy starts emerging.")]
    public float SnapPauseDuration = 0.3f;

    [Header("Emerging")]
    [Tooltip("Upward movement speed while rising out of the ground.")]
    public float EmergeRiseSpeed = 5f;

    [Header("Flight")]
    [Tooltip("Horizontal and vertical movement speed while airborne.")]
    public float FlySpeed = 7f;
    [Tooltip("Height above spawn point the enemy hovers at between attacks.")]
    public float HoverHeight = 5f;
    public float RotationSpeed = 8f;
    [Tooltip("Seconds the burrower hovers with no targets in range before retreating underground and resetting to step 1.")]
    public float NoTargetTimeout = 3f;

    [Header("Dive Attack (Player / any target)")]
    [Tooltip("Vertical speed during the dive.")]
    public float DiveSpeed = 9f;
    [Tooltip("Damage dealt on impact with the dive target.")]
    public float DiveDamage = 12f;
    [Tooltip("Distance at which the dive 'connects' with the target.")]
    public float DiveHitDistance = 1.2f;
    [Tooltip("Duration the enemy is stunned on the ground after a dive (player hit).")]
    public float DiveLandedDuration = 0.8f;

    [Header("Grab Attack (Minion)")]
    [Tooltip("Speed used when descending to grab a minion.")]
    public float GrabDescentSpeed = 8f;
    [Tooltip("Damage per second while carrying a minion. At 2.5 damage/s for 4 seconds, one full grab deals 10 damage before defense.")]
    public float GrabDamagePerSecond = 2.5f;
    [Tooltip("How long the minion is held before being released.")]
    public float GrabDuration = 4f;
    [Tooltip("Height above spawn point the enemy ascends to while carrying a minion.")]
    public float CarryHeight = 6f;
    [Tooltip("How long the burrower hovers in place after the grabbed minion dies before flying off.")]
    public float PostGrabCooldown = 0.3f;

    [Header("Burrowing (HP Retreat)")]
    [Tooltip("Normalised HP percentage (0–1) at which the enemy retreats underground.")]
    [Range(0f, 1f)]
    public float BurrowHealthThreshold = 0.3f;
    [Tooltip("Downward speed while burrowing back into the ground.")]
    public float BurrowDescentSpeed = 5f;
    [Tooltip("How long the enemy rests underground before re-emerging automatically.")]
    public float BurrowRestDuration = 3f;
    [Tooltip("Vertical distance below the spawn Y the enemy sinks to when fully burrowed.")]
    public float BurrowDepth = 1.5f;
    [Tooltip("HP regenerated per second while fully burrowed underground.")]
    public float HpRegenPerSecond = 5f;
}
