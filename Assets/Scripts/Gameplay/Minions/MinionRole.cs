using UnityEngine;

// Defines the common contract for all minion roles.
public interface IMinionRole
{
    MinionRoleType RoleType { get; }

    // Returns the preferred range rules for this role.
    RangePolicy GetRangePolicy();

    // Resolves the combat phase based on distance and context.
    CombatPhase EvaluateCombatPhase(float distanceToTarget, bool hasLineOfSight, bool isAbilityReady);

    // Returns the support mode if this role uses one.
    SupportMode? GetSupportMode();
}

// Shared base class for common role data.
public abstract class MinionRoleBase : IMinionRole
{
    protected readonly RangePolicy rangePolicy;

    public abstract MinionRoleType RoleType { get; }

    protected MinionRoleBase(RangePolicy rangePolicy)
    {
        this.rangePolicy = rangePolicy;
    }

    public virtual RangePolicy GetRangePolicy()
    {
        return rangePolicy;
    }

    public abstract CombatPhase EvaluateCombatPhase(float distanceToTarget, bool hasLineOfSight, bool isAbilityReady);

    public virtual SupportMode? GetSupportMode()
    {
        return null;
    }
}

// Melee minions want to get close, attack, then recover briefly.
public class MeleeRole : MinionRoleBase
{
    public override MinionRoleType RoleType => MinionRoleType.Melee;

    public MeleeRole(RangePolicy rangePolicy) : base(rangePolicy)
    {
    }

    public override CombatPhase EvaluateCombatPhase(float distanceToTarget, bool hasLineOfSight, bool isAbilityReady)
    {
        // Melee usually does not care much about line of sight.
        if (distanceToTarget > rangePolicy.MaxRange) return CombatPhase.Approach;
        if (isAbilityReady) return CombatPhase.AttackWindow;

        return CombatPhase.Recover;
    }
}

// Ranged minions try to stay in their ideal range and reposition if needed.
public class RangedRole : MinionRoleBase
{
    private readonly bool enableKiting;
    public override MinionRoleType RoleType => MinionRoleType.Ranged;

    public RangedRole(RangePolicy rangePolicy, bool enableKiting) : base(rangePolicy)
    {
        this.enableKiting = enableKiting;
    }

    public override CombatPhase EvaluateCombatPhase(float distanceToTarget, bool hasLineOfSight, bool isAbilityReady)
    {
        // Stay in approach until the target is within the ability's actual range.
        if (distanceToTarget > rangePolicy.MaxRange) return CombatPhase.Approach;

        // Too close: move back.
        if (enableKiting && distanceToTarget < rangePolicy.MinRange - rangePolicy.RepositionTolerance) return CombatPhase.Reposition;

        // In valid range but no line of sight: reposition.
        if (!hasLineOfSight) return CombatPhase.Reposition;

        // In a good spot and ready to attack.
        if (isAbilityReady) return CombatPhase.AttackWindow;

        return CombatPhase.Recover;
    }
}

// Support minions use one active support mode at a time.
public class SupportRole : MinionRoleBase
{
    private SupportMode activeSupportMode;

    public override MinionRoleType RoleType => MinionRoleType.Support;

    public SupportRole(RangePolicy rangePolicy, SupportMode startMode) : base(rangePolicy)
    {
        activeSupportMode = startMode;
    }

    public void SetSupportMode(SupportMode newMode)
    {
        activeSupportMode = newMode;
    }

    public override SupportMode? GetSupportMode()
    {
        return activeSupportMode;
    }

    public override CombatPhase EvaluateCombatPhase(float distanceToTarget, bool hasLineOfSight, bool isAbilityReady)
    {
        // Support wants to stay in range of the ally/enemy target.
        if (distanceToTarget > rangePolicy.MaxRange + rangePolicy.RepositionTolerance) return CombatPhase.Approach;
        // Support can act at close range; backing away from the supported target looks like kiting.

        // For support, line of sight may still matter for some abilities.
        if (!hasLineOfSight) return CombatPhase.Approach;
        if (isAbilityReady) return CombatPhase.Cast;

        return CombatPhase.Recover;
    }
}

// Simple helper to create default roles.
public static class MinionRoleFactory
{
    public static IMinionRole Create(MinionRoleType roleType, MinionSettings settings)
    {
        if (settings == null || settings.Melee == null || settings.Melee.RangePolicy == null)
        {
            Debug.LogError("MinionSettings or Melee defaults are missing. Cannot create role.");
            return null;
        }

        switch (roleType)
        {
            case MinionRoleType.Melee: return new MeleeRole(CopyRangePolicy(settings.Melee.RangePolicy));

            case MinionRoleType.Ranged:
                if (settings.Ranged == null || settings.Ranged.RangePolicy == null)
                {
                    Debug.LogWarning("Ranged settings missing. Falling back to Melee defaults.");
                    return new MeleeRole(CopyRangePolicy(settings.Melee.RangePolicy));
                }

                bool enableKiting = settings.Ranged.Behaviour == null || settings.Ranged.Behaviour.EnableRangedKiting;
                return new RangedRole(CopyRangePolicy(settings.Ranged.RangePolicy), enableKiting);

            case MinionRoleType.Support:
                if (settings.Support == null || settings.Support.RangePolicy == null)
                {
                    Debug.LogWarning("Support settings missing. Falling back to Melee defaults.");
                    return new MeleeRole(CopyRangePolicy(settings.Melee.RangePolicy));
                }

                return new SupportRole(CopyRangePolicy(settings.Support.RangePolicy), settings.Support.StartMode);

            default:
                Debug.LogWarning("Unknown role type. Fallback to Melee.");
                return new MeleeRole(CopyRangePolicy(settings.Melee.RangePolicy));
        }
    }

    // Creates a safe runtime copy so ScriptableObject data is not modified. Each minion instance should have its own copy of the role data so they can diverge at runtime (e.g. support mode changes).
    private static RangePolicy CopyRangePolicy(RangePolicy source)
    {
        if (source == null)
        {
            return new RangePolicy();
        }

        return new RangePolicy
        {
            MinRange = source.MinRange,
            DesiredRange = source.DesiredRange,
            MaxRange = source.MaxRange,
            RepositionTolerance = source.RepositionTolerance
        };
    }
}
