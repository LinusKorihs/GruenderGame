using UnityEngine;

[CreateAssetMenu(menuName = "SO/Minions/Minion Settings")]
public class MinionSettings : ScriptableObject
{
    [Header("Tick Rates")]
    [Tooltip("How often the minion updates its state and behavior (in seconds).")]
    public MinionTickRates TickRates;

    [Header("Object Tags")]
    [Tooltip("Tag used to identify enemy objects.")]
    public string EnemyTag = "Enemy";
    [Tooltip("Tag used to identify ally objects.")]
    public string AllyTag = "Ally";
    [Tooltip("Tag used to identify breakable environment objects.")]
    public string BreakableTag = "Breakable";

    [Header("Role Settings")]
    [Tooltip("Settings specific to melee minions.")]
    public RoleSettings Melee;
    [Tooltip("Settings specific to ranged minions.")]
    public RoleSettings Ranged;
    [Tooltip("Settings specific to support minions.")]
    public SupportRoleSettings Support;

    /// <summary>Returns the RoleSettings for the given role type, or null if not found.</summary>
    public RoleSettings GetForRole(MinionRoleType roleType)
    {
        switch (roleType)
        {
            case MinionRoleType.Melee:   return Melee;
            case MinionRoleType.Ranged:  return Ranged;
            case MinionRoleType.Support: return Support;
            default:                     return null;
        }
    }
}

// Behaviour values shared by all roles: movement, navigation, physics, and combat checks.
[System.Serializable]
public class MinionBehaviourSettings
{
    [Header("Combat")]
    [Tooltip("Cooldown in seconds between ability uses for this role. Recommended: Melee 1.0 · Ranged 1.25 · Support 2.0")]
    public float AbilityCooldown = 1.0f;

    [Tooltip("For ranged minions: when enabled, retreat if the target enters MinRange. When disabled, stand still and keep attacking once in MaxRange.")]
    public bool EnableRangedKiting = true;

    [Header("Movement")]
    [Tooltip("Base movement speed in units per second.")]
    public float MoveSpeed = 4f;
    [Tooltip("How fast the minion rotates to face its target (higher = snappier).")]
    public float RotationSpeed = 10f;
    [Tooltip("Distance at which the minion stops when following the player.")]
    public float FollowStopDistance = 1.75f;

    [Header("Navigation (NavMesh)")]
    [Tooltip("When true, pathfinding uses the baked NavMesh to route around obstacles.")]
    public bool UseNavMeshNavigation = true;
    [Tooltip("How often the NavMesh path is recalculated (in seconds). Lower = more responsive, higher = cheaper.")]
    public float NavRepathInterval = 0.2f;
    [Tooltip("Radius within which the target position is snapped onto the NavMesh.")]
    public float NavTargetSampleRadius = 1.25f;
    [Tooltip("Distance threshold for advancing to the next NavMesh waypoint.")]
    public float NavWaypointTolerance = 0.25f;
    [Tooltip("When true, the minion recalls to the player if the destination cannot be reached.")]
    public bool RecallOnPathFailure = true;
    [Tooltip("Minimum seconds between consecutive path-failure recall attempts.")]
    public float PathFailureCooldown = 0.75f;

    [Header("Minion Separation")]
    [Tooltip("When true, minions push away from each other to avoid stacking.")]
    public bool UseLocalSeparation = true;
    [Tooltip("Radius within which nearby minions cause a push-apart force.")]
    public float SeparationRadius = 0.9f;
    [Tooltip("Multiplier controlling how strongly minions repel each other.")]
    public float SeparationStrength = 7f;
    [Tooltip("Maximum distance a minion can be pushed per frame by separation forces.")]
    public float MaxSeparationStep = 0.3f;
    [Tooltip("Layers checked during separation overlap (should include minion colliders).")]
    public LayerMask SeparationMask = ~0;

    [Header("Grounding")]
    [Tooltip("When true, the minion is snapped to the ground surface every frame.")]
    public bool SnapToGround = true;
    [Tooltip("Layers treated as ground for snap raycasting.")]
    public LayerMask GroundMask = ~0;
    [Tooltip("Height above the minion pivot from which the downward ground ray starts.")]
    public float GroundRayStartHeight = 1.5f;
    [Tooltip("Maximum length of the downward ground snap ray.")]
    public float GroundRayLength = 8f;
    [Tooltip("Additional vertical offset applied after snapping to the detected ground point.")]
    public float GroundOffset = 0f;

    [Header("Gravity")]
    [Tooltip("When true, applies simple downward gravity when the minion is not grounded.")]
    public bool UseSimpleGravity = true;
    [Tooltip("Downward acceleration in units/sec² applied while the minion is airborne.")]
    public float GravityAcceleration = 25f;
    [Tooltip("Maximum downward speed the minion can reach while falling.")]
    public float MaxFallSpeed = 40f;

    [Header("Line of Sight")]
    [Tooltip("Layers that can block line of sight between the minion and its target (e.g. walls, terrain).")]
    public LayerMask LineOfSightBlockMask = ~0;
    [Tooltip("Vertical offset above the pivot used when casting the line-of-sight ray.")]
    public float LineOfSightHeightOffset = 0.8f;
    [Tooltip("When true, abilities are suppressed whenever the target is not in line of sight.")]
    public bool RequireLineOfSightForAllAttacks = true;
    [Tooltip("When true, the minion stops combat and returns to follow/idle instead of trying to reposition when line of sight is blocked.")]
    public bool ReturnToFollowWhenLineOfSightBlocked = false;

    [Header("Auto Targeting")]
    [Tooltip("When true, the minion automatically picks up nearby combat targets without a player command.")]
    public bool AutoAssignCombatCommands = false;
    [Tooltip("Search radius used when auto-assigning a combat target.")]
    public float AutoTargetRadius = 35f;

    [Header("Ranged Projectile")]
    [Tooltip("Prefab spawned when this minion fires a ranged attack. Requires a MinionProjectile component. Leave empty for instant-hit damage.")]
    public GameObject ProjectilePrefab;
    [Tooltip("When true, spawned projectiles home in on the target until they hit.")]
    public bool UseHomingProjectiles = true;
    [Tooltip("Travel speed of spawned projectiles in units per second.")]
    public float ProjectileSpeed = 10f;
}

// Base settings shared by all roles.
[System.Serializable]
public class RoleSettings
{
    [Tooltip("Range thresholds controlling approach, attack window, and retreat distances.")]
    public RangePolicy RangePolicy;
    [Tooltip("Movement, navigation, physics, and combat behaviour for this role.")]
    public MinionBehaviourSettings Behaviour;
}

// Extended settings for support-specific behavior.
[System.Serializable]
public class SupportRoleSettings : RoleSettings
{
    [Header("Support Role Specific")]
    [Tooltip("The starting mode for support minions.")]
    public SupportMode StartMode = SupportMode.Heal;
    [Tooltip("Status effect applied to allied targets when in Buff mode.")]
    public StatusEffectDefinition SupportBuffEffect;
    [Tooltip("Status effect applied to enemy targets when in Debuff mode.")]
    public StatusEffectDefinition SupportDebuffEffect;
}
