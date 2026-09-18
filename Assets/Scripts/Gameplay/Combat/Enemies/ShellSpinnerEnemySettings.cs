using UnityEngine;

/* <Summary / Notes>
    Shared configuration for the Shell Spinner enemy (Enemy 2 — Koopa/Armos-like).
    Create via Assets > Create > SO > Combat > Enemy > Shell Spinner Enemy Settings.
*/
[CreateAssetMenu(menuName = "SO/Combat/Enemy/Shell Spinner")]
public class ShellSpinnerEnemySettings : ScriptableObject
{
    [Header("Tags")]
    public string PlayerTag = "Player";
    public string MinionTag = "Ally";

    [Header("Detection")]
    [Tooltip("Radius within which the Spinner detects targets and wakes up.")]
    public float DetectRadius = 12f;
    [Tooltip("Target is forgotten when it leaves this radius.")]
    public float ForgetRadius = 18f;
    public LayerMask DetectMask = ~0;
    [Tooltip("Only wake up to targets the Spinner can actually see.")]
    public bool RequireLOSToDetect = true;

    [Header("Line of Sight")]
    [Tooltip("Layers treated as solid walls for LOS checks.")]
    public LayerMask LosBlockMask = ~0;
    public float LosHeightOffset = 0.8f;

    [Header("Movement")]
    [Tooltip("Rotation speed while facing a target in the Idle state.")]
    public float RotationSpeed = 8f;

    [Header("Windup / Targeting")]
    [Tooltip("Duration of the windup / targeting phase. The spinner is still vulnerable and shows an aim line.")]
    public float WindupDuration = 0.6f;
    [Tooltip("LayerMask used to snap the targeting line to the ground surface. " + "Assign the same layer(s) as your floor geometry. If empty the spinner's own Y is used as a fallback.")]
    public LayerMask GroundMask;
    [Range(2, 24)] public int TargetingLineSegments = 10;
    [Min(0f)] public float TargetingLineGroundOffset = 0.04f;
    [Min(0.5f)] public float TargetingLineRayHeight = 8f;

    [Header("Attack Choice")]
    [Range(0f, 1f)]
    [Tooltip("Chance to use a ranged attack after the first spin has completed. Spin is forced for the first attack.")]
    public float RangedAttackChance = 0.5f;
    [Tooltip("Maximum number of times the same attack type may be selected back to back.")]
    public int MaxSameAttackRepeats = 2;

    [Header("Spin Attack")]
    [Tooltip("Speed at which the spinner moves in a straight line once launched.")]
    public float SpinSpeed = 10f;
    [Tooltip("Damage dealt to any player or minion touched during the spin.")]
    public float SpinDamage = 18f;
    [Tooltip("When enabled, spin contact kills minions regardless of their current max health/defense. Player damage still uses SpinDamage.")]
    public bool OneShotMinionsOnSpin = false;
    [Tooltip("When disabled (default) the spin stops as soon as it touches a player or minion. " + "When enabled the spin passes through all targets (dealing damage to each once) " + "and only stops when hitting a wall or travelling MaxSpinRange units.")]
    public bool SpinUntilWall = false;
    [Tooltip("Maximum travel distance before the spin automatically ends. 0 = unlimited.")]
    public float MaxSpinRange = 20f;
    [Tooltip("Layers that stop the spin before the shell can tunnel into walls or props.")]
    public LayerMask SpinBlockMask = ~0;
    [Min(0f), Tooltip("Small safety gap kept in front of blocking geometry.")]
    public float SpinCollisionSkin = 0.05f;

    [Header("Hit Pause")]
    [Tooltip("Duration of the brief impact-stop when the spinner hits anything. Still in shell / invincible.")]
    public float HitPauseDuration = 0.15f;
    [Tooltip("Seconds after each spin launch during which wall collisions are ignored. " +
             "Prevents the spinner from immediately re-hitting the wall it was resting against.")]
    public float SpinCollisionGrace = 0.12f;
    [Tooltip("Knockback impulse force applied to targets hit during the spin. " +
             "Works on both Rigidbody and non-Rigidbody (CharacterController) targets.")]
    public float KnockbackForce = 8f;

    [Header("Ranged Attack")]
    [Tooltip("Fallback local-space projectile spawn offset when the ShellSpinnerEnemy has no spawn point assigned.")]
    public Vector3 ProjectileSpawnOffset = new Vector3(0f, 0.6f, 0.5f);
    [Tooltip("How long the spinner tucks in before firing. It stays in place and tracks the target.")]
    public float RangedWindupDuration = 0.35f;
    [Tooltip("Time after the final projectile before the spinner exits the ranged attack and goes dizzy.")]
    public float RangedRecoveryDuration = 0.35f;
    [Tooltip("Projectile lifetime in seconds.")]
    public float ProjectileLifetime = 6f;
    [Tooltip("Visual/gameplay scale multiplier applied to spawned projectile instances.")]
    public float ProjectileScaleMultiplier = 1f;
    [Range(0.05f, 1f), Tooltip("Collider radius multiplier after projectile scaling. Use a lower value when the visible mesh is much smaller than its root scale.")]
    public float ProjectileHitboxRadiusMultiplier = 1f;
    [Tooltip("When true, spawned projectiles keep steering toward the current target.")]
    public bool UseHomingProjectiles = true;

    [Header("Ranged Attack - Fast Fire")]
    public GameObject FastProjectilePrefab;
    public int FastProjectileCount = 3;
    public float FastProjectileInterval = 0.18f;
    public float FastProjectileDamage = 6f;
    public float FastProjectileSpeed = 12f;

    [Header("Ranged Attack - Heavy Shot")]
    public GameObject HeavyProjectilePrefab;
    public int HeavyProjectileCount = 2;
    public float HeavyProjectileInterval = 0.65f;
    public float HeavyProjectileDamage = 14f;
    public float HeavyProjectileSpeed = 7f;
    [Tooltip("Impulse pushed backwards after each heavy shot. Only applies when the spinner has a non-kinematic Rigidbody.")]
    public float HeavyShotRecoilForce = 2f;

    [Header("Shell Exit")]
    [Tooltip("Duration of the exiting-shell animation. Spinner is vulnerable from the very start of this state.")]
    public float ExitShellDuration = 0.6f;

    [Header("Dizzy")]
    [Tooltip("How long the spinner stands dizzy and defenceless after exiting the shell.")]
    public float DizzyDuration = 2.5f;
    [Tooltip("Variant: how long the spinner stands dizzy while firing projectiles.")]
    public float DizzyProjDuration = 1f;

    [Header("Wake Up")]
    [Tooltip("Duration of the waking-up / shaking-off-dizziness animation before the next shell entry.")]
    public float WakeUpDuration = 0.4f;
}
