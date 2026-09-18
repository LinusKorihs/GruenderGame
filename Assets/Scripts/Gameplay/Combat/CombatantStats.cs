using System;
using System.Collections.Generic;
using UnityEngine;

public class CombatantStats : MonoBehaviour
{
    [Header("Base")]
    [SerializeField] private CombatStatsProfile baseProfile;

    [Header("Runtime")]
    [SerializeField] private float currentHealth;

    [Header("Death")]
    [SerializeField] private bool despawnOnDeath = true;
    [SerializeField] private float despawnDelay = 0f;

    private readonly List<SourcedModifier> persistentModifiers = new List<SourcedModifier>(); // Modifiers that persist independently of status effects (e.g., from equipment, buffs, etc.)
    private readonly List<ActiveStatusEffect> activeEffects = new List<ActiveStatusEffect>(); // Currently active status effects on this combatant
    private int lastPlayerHitSoundIndex;

    public float CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0f; // When true, ApplyDamage is silently ignored. Set by enemies that are invincible in certain states (e.g. ShellSpinner inside the shell).
    public bool IsInvincible { get; set; }

    public event Action<float, float> HealthChanged; // (currentHealth, maxHealth)
    public event Action<float> DamageTaken;          // (finalDamage) — fired after each successful hit
    public event Action Died;

    private void Awake()
    {
        if (currentHealth <= 0f)
        {
            currentHealth = GetStat(CombatStatType.MaxHealth);
        }
    }

    private void Start()
    {
        EnemyWorldHealthBar.AttachIfNormalEnemy(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        float now = Time.time;

        for (int i = activeEffects.Count - 1; i >= 0; i--) // Iterate backwards since expired effects may be removed
        {
            ActiveStatusEffect effect = activeEffects[i];
            effect.RemainingTime -= dt;

            if (effect.Definition != null && effect.Definition.TickInterval > 0f && now >= effect.NextTickTime)
            {
                if (effect.Definition.PeriodicDamage > 0f)
                {
                    ApplyDamage(effect.Definition.PeriodicDamage);
                }

                if (effect.Definition.PeriodicHeal > 0f)
                {
                    Heal(effect.Definition.PeriodicHeal);
                }

                effect.NextTickTime = now + effect.Definition.TickInterval;
            }

            if (effect.RemainingTime <= 0f)
            {
                activeEffects.RemoveAt(i);
            }
            else
            {
                activeEffects[i] = effect;
            }
        }
    }

    public float GetStat(CombatStatType type)
    {
        float baseValue = baseProfile != null ? baseProfile.GetBaseStat(type) : GetFallbackBaseValue(type); // Get base value from profile or fallback if not defined
        float additive = 0f;
        float multiplicative = 1f;

        for (int i = 0; i < persistentModifiers.Count; i++) // Apply persistent modifiers first (e.g., from equipment, buffs, etc.)
        {
            CombatStatModifierData modifier = persistentModifiers[i].Modifier;
            if (modifier.Type != type) continue; // Only apply modifiers that match the requested stat type

            if (modifier.Operation == StatModifierOperation.Add)
            {
                additive += modifier.Value; // Additive modifiers are summed
            }
            else
            {
                multiplicative *= modifier.Value; // Multiplicative modifiers are multiplied together (e.g., 1.2 for +20%, 0.8 for -20%)
            }
        }

        for (int i = 0; i < activeEffects.Count; i++) // Apply modifiers from active status effects (stacking and refreshing handled in ApplyStatusEffect)
        {
            ActiveStatusEffect effect = activeEffects[i];
            if (effect.Definition == null) continue;

            for (int m = 0; m < effect.Definition.Modifiers.Count; m++) // Iterate through each modifier in the status effect
            {
                CombatStatModifierData modifier = effect.Definition.Modifiers[m];
                if (modifier.Type != type) continue; // Only apply modifiers that match the requested stat type

                if (modifier.Operation == StatModifierOperation.Add)
                {
                    additive += modifier.Value * effect.Stacks; // Additive modifiers are multiplied by the number of stacks (e.g., +5 Damage per stack)
                }
                else
                {
                    multiplicative *= Mathf.Pow(modifier.Value, effect.Stacks); // Multiplicative modifiers are raised to the power of the number of stacks (e.g., 1.2^2 for +44% with 2 stacks)
                }
            }
        }

        float result = (baseValue + additive) * multiplicative; // Final stat calculation using baserule: (Base + Additive) * Multiplicative

        if (type == CombatStatType.MaxHealth) return Mathf.Max(1f, result); // Ensure MaxHealth is always at least 1 to prevent issues with health calculations
        if (type == CombatStatType.AttackSpeed) return Mathf.Max(0.05f, result); // Prevent AttackSpeed from dropping to 0 or negative which would break attack logic (0.05 means attacks can be at most 20x slower)
        if (type == CombatStatType.Defense) return Mathf.Max(0.1f, result); // Prevent Defense from dropping to 0 or negative which would break damage calculations
        return result;
    }

    public void SetToFullHealth()
    {
        SetHealth(GetStat(CombatStatType.MaxHealth));
    }

    public void SetHealth(float value)
    {
        float oldValue = currentHealth;
        float maxHealth = GetStat(CombatStatType.MaxHealth);
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);

        if (!Mathf.Approximately(oldValue, currentHealth))
        {
            HealthChanged?.Invoke(currentHealth, maxHealth); // Notify listeners of health change (e.g., UI updates, reactions to low health, etc.)
        }

        if (oldValue > 0f && currentHealth <= 0f)
        {
            if (GetComponentInParent<MinionCore>() != null)
                SoundManager.TryPlayId("Minion.Death", transform);
            Died?.Invoke(); // Notify listeners that the combatant has died
            if (despawnOnDeath)
            {
                // If the root is the player, destroy the whole hierarchy, not just this child.
                GameObject toDestroy = transform.root.CompareTag("Player")
                    ? transform.root.gameObject
                    : gameObject;
                Destroy(toDestroy, Mathf.Max(0f, despawnDelay));
            }
        }
    }

    public float ApplyDamage(float amount) // Returns the actual damage taken after defense is applied
    {
        if (amount <= 0f || IsDead || IsInvincible) return 0f;

        float defense = GetStat(CombatStatType.Defense);
        float finalDamage = amount / defense; // Defense acts as a divisor (e.g., 10 damage with 2 defense results in 5 final damage)
        SetHealth(currentHealth - finalDamage);
        DamageTaken?.Invoke(finalDamage);
        if (transform.root.CompareTag("Player"))
        {
            int nextHitSoundIndex = UnityEngine.Random.Range(1, 4);
            if (nextHitSoundIndex == lastPlayerHitSoundIndex)
                nextHitSoundIndex = nextHitSoundIndex % 3 + 1;
            lastPlayerHitSoundIndex = nextHitSoundIndex;
            SoundManager.TryPlayId($"Player.Hit.{nextHitSoundIndex}", transform);
        }
        return finalDamage;
    }

    public float Heal(float amount)
    {
        if (amount <= 0f || IsDead) return 0f;

        float before = currentHealth;
        SetHealth(currentHealth + amount); // Heal amount is added to current health, but SetHealth will clamp it to max health
        return currentHealth - before;
    }

    public void AddPersistentModifier(string sourceId, CombatStatModifierData modifier) // Adds a persistent modifier that is independent of status effects (e.g., from equipment, buffs, etc.)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) sourceId = "unknown";

        persistentModifiers.Add(new SourcedModifier
        {
            SourceId = sourceId,
            Modifier = modifier
        });
    }

    public void RemovePersistentModifiers(string sourceId) // Removes all persistent modifiers from a specific source (e.g., when unequipping an item, losing a buff, etc.)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;

        for (int i = persistentModifiers.Count - 1; i >= 0; i--)
        {
            if (persistentModifiers[i].SourceId == sourceId)
            {
                persistentModifiers.RemoveAt(i);
            }
        }
    }

    public void ApplyStatusEffect(StatusEffectDefinition definition) // Applies a status effect to the combatant, handling stacking and duration refresh
    {
        if (definition == null) return;

        for (int i = 0; i < activeEffects.Count; i++) // Check if the same status effect is already active (based on EffectId) to handle stacking and refreshing
        {
            ActiveStatusEffect effect = activeEffects[i];
            if (effect.Definition != definition) continue;

            effect.Stacks = Mathf.Clamp(effect.Stacks + 1, 1, Mathf.Max(1, definition.MaxStacks));
            if (definition.RefreshDurationOnReapply)
            {
                effect.RemainingTime = Mathf.Max(0.01f, definition.Duration);
            }

            activeEffects[i] = effect;
            return;
        }

        activeEffects.Add(new ActiveStatusEffect
        {
            Definition = definition,
            RemainingTime = Mathf.Max(0.01f, definition.Duration),
            NextTickTime = Time.time + Mathf.Max(0.01f, definition.TickInterval),
            Stacks = 1
        });
    }

    private static float GetFallbackBaseValue(CombatStatType type)
    {
        switch (type)
        {
            case CombatStatType.MaxHealth:
                return 100f;
            case CombatStatType.Damage:
                return 10f;
            case CombatStatType.HealPower:
                return 8f;
            case CombatStatType.MoveSpeed:
                return 4f;
            case CombatStatType.AttackSpeed:
                return 1f;
            case CombatStatType.Defense:
                return 1f;
            default:
                return 0f;
        }
    }

    [Serializable]
    private struct SourcedModifier
    {
        public string SourceId;
        public CombatStatModifierData Modifier;
    }

    private struct ActiveStatusEffect
    {
        public StatusEffectDefinition Definition;
        public float RemainingTime;
        public float NextTickTime;
        public int Stacks;
    }
}
