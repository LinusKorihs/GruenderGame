using System.Collections.Generic;
using UnityEngine;

// Shared runtime contract for minion abilities.
public abstract class AbilityBase
{
    public string Id { get; protected set; }
    public float Cooldown { get; protected set; }
    public float Range { get; protected set; }
    public TargetType TargetType { get; protected set; }

    // Set by MinionAbilitySystem; gates all Debug output inside Execute.
    internal System.Action<string> Logger;

    protected float lastUseTime = -999f;

    // Checks if cooldown is ready. Attack speed shortens cooldowns.
    public virtual bool IsReady(float currentTime, CombatantStats casterStats)
    {
        float finalCooldown = Cooldown;
        if (casterStats != null)
        {
            float attackSpeed = casterStats.GetStat(CombatStatType.AttackSpeed);
            finalCooldown /= Mathf.Max(0.05f, attackSpeed);
        }

        return currentTime >= lastUseTime + finalCooldown;
    }

    // Checks if the target type and distance are valid.
    public virtual bool CanUse(Transform caster, Transform target, float currentTime, CombatantStats casterStats)
    {
        if (caster == null || target == null) return false;
        if (!IsReady(currentTime, casterStats)) return false;

        // Match movement/combat logic by ignoring vertical offset when checking range.
        Vector3 toTarget = target.position - caster.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        return distance <= Range;
    }

    // Executes the ability.
    public bool TryUse(Transform caster, Transform target, float currentTime, CombatantStats casterStats)
    {
        if (!CanUse(caster, target, currentTime, casterStats)) return false;

        Execute(caster, target, casterStats);
        lastUseTime = currentTime;
        return true;
    }

    // Concrete behavior lives here.
    protected abstract void Execute(Transform caster, Transform target, CombatantStats casterStats);
}

// Simple melee damage ability.
public class MeleeAttackAbility : AbilityBase
{
    public MeleeAttackAbility(float range, float cooldown)
    {
        Id = "MeleeAttack";
        Range = range;
        Cooldown = cooldown;
        TargetType = TargetType.Enemy;
    }

    protected override void Execute(Transform caster, Transform target, CombatantStats casterStats)
    {
        float damage = casterStats != null ? casterStats.GetStat(CombatStatType.Damage) : 0f;
        CombatantStats targetStats = target != null ? target.GetComponentInParent<CombatantStats>() : null;
        if (targetStats != null)
        {
            targetStats.ApplyDamage(damage);
        }

        Logger?.Invoke($"used {Id} on [{target.name}] for {damage} damage.");
    }
}

// Ranged damage ability. When a projectilePrefab is assigned, spawns a MinionProjectile;
// otherwise falls back to instant-hit damage.
public class RangedAttackAbility : AbilityBase
{
    private const float ProjectileVisualScale = 3f;

    private readonly GameObject projectilePrefab;
    private readonly bool       useHoming;
    private readonly float      projectileSpeed;

    public RangedAttackAbility(
        float      range,
        float      cooldown,
        GameObject projectilePrefab = null,
        bool       useHoming        = true,
        float      projectileSpeed  = 10f)
    {
        Id                   = "RangedAttack";
        Range                = range;
        Cooldown             = cooldown;
        TargetType           = TargetType.Enemy;
        this.projectilePrefab = projectilePrefab;
        this.useHoming        = useHoming;
        this.projectileSpeed  = projectileSpeed;
    }

    protected override void Execute(Transform caster, Transform target, CombatantStats casterStats)
    {
        float damage = casterStats != null ? casterStats.GetStat(CombatStatType.Damage) : 0f;

        if (projectilePrefab != null)
        {
            Vector3 spawnPos = caster.position + Vector3.up * 0.5f;
            GameObject projectileObject = Object.Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
            projectileObject.transform.localScale *= ProjectileVisualScale;

            MinionProjectile projectile = projectileObject.GetComponent<MinionProjectile>();

            if (projectile != null)
            {
                // ownerTag = "Ally" so the projectile won't damage other ally minions.
                projectile.Initialize(target, damage, projectileSpeed, useHoming, "Ally", 6f);
                return;
            }
        }

        // Instant-hit fallback when no prefab is assigned.
        CombatantStats targetStats = target != null ? target.GetComponentInParent<CombatantStats>() : null;
        if (targetStats != null)
        {
            targetStats.ApplyDamage(damage);
        }

        Logger?.Invoke($"fired {Id} at [{target.name}] for {damage} damage (instant fallback).");
    }
}

// Support ability that changes behavior based on mode.
public class SupportAbility : AbilityBase
{
    private readonly SupportMode supportMode;
    private readonly StatusEffectDefinition supportEffect;

    public SupportAbility(float castRange, float cooldown, SupportMode mode, StatusEffectDefinition effectDefinition = null)
    {
        supportMode = mode;
        supportEffect = effectDefinition;
        Id = $"Support_{mode}";
        Range = castRange;
        Cooldown = cooldown;

        // Heal/Buff usually target allies, Debuff targets enemies.
        TargetType = mode == SupportMode.Debuff ? TargetType.Enemy : TargetType.Ally;
    }

    protected override void Execute(Transform caster, Transform target, CombatantStats casterStats)
    {
        switch (supportMode)
        {
            case SupportMode.Heal:
            {
                float healAmount = casterStats != null ? casterStats.GetStat(CombatStatType.HealPower) : 0f;
                CombatantStats targetStats = target != null ? target.GetComponentInParent<CombatantStats>() : null;
                if (targetStats != null)
                {
                    targetStats.Heal(healAmount);
                }

                Logger?.Invoke($"cast Heal on [{target.name}] for {healAmount} HP.");
                break;
            }

            case SupportMode.Buff:
            case SupportMode.Debuff:
            {
                if (supportEffect != null)
                {
                    CombatantStats targetStats = target != null ? target.GetComponentInParent<CombatantStats>() : null;
                    if (targetStats != null)
                    {
                        targetStats.ApplyStatusEffect(supportEffect);
                    }
                }

                Logger?.Invoke($"cast {supportMode} on [{target.name}].");
                break;
            }
        }
    }
}

// Selects and executes abilities for the minion.
public class MinionAbilitySystem
{
    private readonly List<AbilityBase> equippedAbilities = new List<AbilityBase>();

    public IReadOnlyList<AbilityBase> EquippedAbilities => equippedAbilities;

    // Assigned by MinionCore.Initialize(); calls Log() when enableLogs is true.
    // Setting this propagates the logger to all currently equipped abilities.
    private System.Action<string> _logger;
    public System.Action<string> Logger
    {
        get => _logger;
        set
        {
            _logger = value;
            foreach (AbilityBase a in equippedAbilities) a.Logger = value;
        }
    }

    public void Clear()
    {
        equippedAbilities.Clear();
    }

    public void AddAbility(AbilityBase ability)
    {
        if (ability == null) return;
        ability.Logger = _logger;
        equippedAbilities.Add(ability);
    }

    // Rebuilds a very small default loadout based on role.
    public void BuildDefaultLoadout(
        IMinionRole role,
        StatusEffectDefinition supportBuffEffect    = null,
        StatusEffectDefinition supportDebuffEffect  = null,
        float      abilityCooldown                  = 1f,
        GameObject rangedProjectilePrefab          = null,
        bool       useHomingProjectiles             = true,
        float      projectileSpeed                  = 10f)
    {
        Clear();

        if (role == null) return;

        RangePolicy policy = role.GetRangePolicy();
        if (policy == null) return;

        switch (role.RoleType)
        {
            case MinionRoleType.Melee:
                AddAbility(new MeleeAttackAbility(policy.MaxRange, abilityCooldown));
                break;

            case MinionRoleType.Ranged:
                AddAbility(new RangedAttackAbility(
                    policy.MaxRange, abilityCooldown,
                    rangedProjectilePrefab, useHomingProjectiles, projectileSpeed));
                break;

            case MinionRoleType.Support:
            {
                SupportMode mode = role.GetSupportMode() ?? SupportMode.Heal;
                StatusEffectDefinition effect = null;
                if (mode == SupportMode.Buff) effect = supportBuffEffect;
                else if (mode == SupportMode.Debuff) effect = supportDebuffEffect;

                AddAbility(new SupportAbility(policy.MaxRange, abilityCooldown, mode, effect));
                break;
            }
        }
    }

    // Tries to use the best ability for the current target.
    public bool TryUseBestAbility(
        Transform caster,
        Transform target,
        float currentTime,
        CombatantStats casterStats,
        MinionRoleType roleType)
    {
        AbilityBase best = SelectBestAbility(caster, target, currentTime, casterStats, roleType);
        if (best == null) return false;

        return best.TryUse(caster, target, currentTime, casterStats);
    }

    public bool HasAnyReadyAbility(
        Transform caster,
        Transform target,
        float currentTime,
        CombatantStats casterStats,
        MinionRoleType roleType)
    {
        return SelectBestAbility(caster, target, currentTime, casterStats, roleType) != null;
    }

    private AbilityBase SelectBestAbility(
        Transform caster,
        Transform target,
        float currentTime,
        CombatantStats casterStats,
        MinionRoleType roleType)
    {
        AbilityBase best = null;
        float bestScore = float.MinValue;

        for (int i = 0; i < equippedAbilities.Count; i++)
        {
            AbilityBase ability = equippedAbilities[i];
            if (ability == null) continue;
            if (!IsTargetTypeCompatible(ability, caster, target)) continue;
            if (!ability.CanUse(caster, target, currentTime, casterStats)) continue;

            float score = ScoreAbility(ability, caster, target, roleType);
            if (score > bestScore)
            {
                bestScore = score;
                best = ability;
            }
        }

        return best;
    }

    private bool IsTargetTypeCompatible(AbilityBase ability, Transform caster, Transform target)
    {
        if (ability == null || target == null) return false;

        switch (ability.TargetType)
        {
            case TargetType.Enemy:
                // Enemy-type abilities can also be used on breakable objects — they deal damage the same way.
                return target.CompareTag("Enemy") || target.CompareTag("Breakable");

            case TargetType.Ally:
                return target.CompareTag("Ally") || (caster != null && target == caster);

            case TargetType.Breakable:
                return target.CompareTag("Breakable");

            default:
                return true;
        }
    }

    // Very small scoring model for now.
    private float ScoreAbility(AbilityBase ability, Transform caster, Transform target, MinionRoleType roleType)
    {
        // Use the same horizontal distance metric here so scoring stays aligned with CanUse.
        Vector3 toTarget = target.position - caster.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        float score = 0f;

        // Prefer abilities that fit the current distance better.
        score -= Mathf.Abs(ability.Range - distance);

        // Small role preference bonus.
        switch (roleType)
        {
            case MinionRoleType.Melee:
                if (ability is MeleeAttackAbility) score += 10f;
                break;

            case MinionRoleType.Ranged:
                if (ability is RangedAttackAbility) score += 10f;
                break;

            case MinionRoleType.Support:
                if (ability is SupportAbility) score += 10f;
                break;
        }

        return score;
    }
}
