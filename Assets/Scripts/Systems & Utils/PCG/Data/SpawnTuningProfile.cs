using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WeightedSpawnEntryTuning
{
    [Tooltip("Matches the id on a WeightedSpawnEntry in the base spawn config.")]
    public string id;

    [Min(0)] public int weight = 10;
    [Min(1)] public int minLevel = 1;
    [Min(1)] public int maxLevel = 999;
    [Min(0)] public int maxPerLevel = 999;
}

[CreateAssetMenu(menuName = "SO/PCG/Profiles/Tuning/Spawn Tuning Profile", fileName = "SpawnTuningProfile")]
public class SpawnTuningProfile : ScriptableObject
{
    [Header("Spawn Toggles")]
    public bool spawnPlayer = true;
    public bool spawnMinions = true;
    public bool spawnEnemies = true;
    public bool spawnItems = true;

    [Header("Enemy Budget")]
    public SpawnBudget enemyBudget = new SpawnBudget { minPerRoom = 0, maxPerRoom = 3, minPerLevel = 5, maxPerLevel = 15 };

    [Header("Item Budget")]
    public SpawnBudget itemBudget = new SpawnBudget { minPerRoom = 0, maxPerRoom = 1, minPerLevel = 1, maxPerLevel = 5 };

    [Header("Minion Pool Tuning")]
    public List<WeightedSpawnEntryTuning> minionPool = new List<WeightedSpawnEntryTuning>();

    [Header("Enemy Pool Tuning")]
    public List<WeightedSpawnEntryTuning> enemyPool = new List<WeightedSpawnEntryTuning>();

    [Header("Item Pool Tuning")]
    public List<WeightedSpawnEntryTuning> itemPool = new List<WeightedSpawnEntryTuning>();

    [Header("Difficulty Scaling")]
    public LevelDifficultyScalingMode scalingMode = LevelDifficultyScalingMode.SpawnAmounts;
    public AnimationCurve enemyStatMultiplierByLevel = AnimationCurve.Linear(1f, 1f, 10f, 2f);
    public List<CombatStatType> scaledEnemyStats = new List<CombatStatType>
    {
        CombatStatType.MaxHealth,
        CombatStatType.Damage
    };

    [Header("Debug")]
    public bool log;

    public void ApplyTo(LevelContentSpawnConfig target, UnityEngine.Object logContext = null)
    {
        if (target == null)
        {
            Debug.LogWarning("[Level Profile] Spawn tuning skipped because the target config is missing.", logContext != null ? logContext : this);
            return;
        }

        UnityEngine.Object context = logContext != null ? logContext : this;

        target.spawnPlayer = spawnPlayer;
        target.spawnMinions = spawnMinions;
        target.spawnEnemies = spawnEnemies;
        target.spawnItems = spawnItems;
        target.enemyBudget = PCGProfileCopyUtility.CloneBudget(enemyBudget) ?? new SpawnBudget();
        target.itemBudget = PCGProfileCopyUtility.CloneBudget(itemBudget) ?? new SpawnBudget();
        target.scalingMode = scalingMode;
        target.enemyStatMultiplierByLevel = PCGProfileCopyUtility.CloneCurve(enemyStatMultiplierByLevel);
        target.scaledEnemyStats = PCGProfileCopyUtility.CloneCombatStats(scaledEnemyStats);
        target.log = log;

        ApplyPoolTuning(target.minionPool, minionPool, "Minion", context);
        ApplyPoolTuning(target.enemyPool, enemyPool, "Enemy", context);
        ApplyPoolTuning(target.itemPool, itemPool, "Item", context);

        Debug.Log(
            $"[Level Profile] Applied SpawnTuning '{name}' to '{target.name}'. " +
            $"Spawn(Player={target.spawnPlayer}, Minions={target.spawnMinions}, Enemies={target.spawnEnemies}, Items={target.spawnItems}), " +
            $"EnemyBudget={FormatBudget(target.enemyBudget)}, ItemBudget={FormatBudget(target.itemBudget)}, Scaling={target.scalingMode}, " +
            $"PoolTuning(Minions={CountEntries(minionPool)}, Enemies={CountEntries(enemyPool)}, Items={CountEntries(itemPool)}).",
            context);
    }

    private static void ApplyPoolTuning(
        List<WeightedSpawnEntry> targetPool,
        List<WeightedSpawnEntryTuning> tuningPool,
        string poolLabel,
        UnityEngine.Object context)
    {
        if (tuningPool == null || tuningPool.Count == 0)
            return;

        if (targetPool == null || targetPool.Count == 0)
        {
            Debug.LogWarning($"[Level Profile] {poolLabel} tuning was provided, but the base {poolLabel} pool is empty. Entries skipped.", context);
            return;
        }

        HashSet<string> appliedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<WeightedSpawnEntry> tunedRuntimePool = new List<WeightedSpawnEntry>();
        for (int i = 0; i < tuningPool.Count; i++)
        {
            WeightedSpawnEntryTuning tuning = tuningPool[i];
            if (tuning == null || string.IsNullOrWhiteSpace(tuning.id))
            {
                Debug.LogWarning($"[Level Profile] {poolLabel} tuning entry at index {i} has no id. Entry skipped.", context);
                continue;
            }

            if (!appliedIds.Add(tuning.id))
            {
                Debug.LogWarning($"[Level Profile] Duplicate {poolLabel} tuning id '{tuning.id}'. Later duplicate skipped.", context);
                continue;
            }

            WeightedSpawnEntry target = FindById(targetPool, tuning.id);
            if (target == null)
            {
                Debug.LogWarning($"[Level Profile] {poolLabel} tuning id '{tuning.id}' was not found in the base pool. Entry skipped.", context);
                continue;
            }

            ApplyEntryTuning(target, tuning);
            tunedRuntimePool.Add(target);
        }

        if (tunedRuntimePool.Count == 0)
        {
            Debug.LogWarning(
                $"[Level Profile] {poolLabel} tuning had no valid matching ids. Keeping the full base {poolLabel} pool as fallback.",
                context);
            return;
        }

        int originalCount = targetPool.Count;
        targetPool.Clear();
        targetPool.AddRange(tunedRuntimePool);

        Debug.Log(
            $"[Level Profile] {poolLabel} runtime pool restricted to {tunedRuntimePool.Count}/{originalCount} tuned entries.",
            context);
    }

    private static void ApplyEntryTuning(WeightedSpawnEntry target, WeightedSpawnEntryTuning tuning)
    {
        if (tuning.weight > 0)
        {
            target.weight = tuning.weight;
        }

        if (tuning.minLevel > 0)
        {
            target.minLevel = tuning.minLevel;
        }

        if (tuning.maxLevel > 0)
        {
            int effectiveMinLevel = Mathf.Max(1, target.minLevel);
            target.maxLevel = Mathf.Max(effectiveMinLevel, tuning.maxLevel);
        }

        if (tuning.maxPerLevel > 0)
        {
            target.maxPerLevel = tuning.maxPerLevel;
        }
    }

    private static WeightedSpawnEntry FindById(List<WeightedSpawnEntry> pool, string id)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            WeightedSpawnEntry entry = pool[i];
            if (entry == null) continue;
            if (string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    private static int CountEntries(List<WeightedSpawnEntryTuning> entries)
    {
        return entries != null ? entries.Count : 0;
    }

    private static string FormatBudget(SpawnBudget budget)
    {
        if (budget == null) return "missing";

        return
            $"room {budget.minPerRoom}-{budget.maxPerRoom}, " +
            $"level {budget.minPerLevel}-{budget.maxPerLevel}, " +
            $"growth +{budget.additionalMinPerLevel}/+{budget.additionalMaxPerLevel}";
    }
}
