using System.Collections.Generic;
using UnityEngine;

public static class PCGProfileCopyUtility
{
    public static void DetachRuntimeSpawnConfig(LevelContentSpawnConfig config)
    {
        if (config == null) return;

        config.enemyBudget = CloneBudget(config.enemyBudget);
        config.itemBudget = CloneBudget(config.itemBudget);
        config.minionPool = CloneSpawnPool(config.minionPool);
        config.enemyPool = CloneSpawnPool(config.enemyPool);
        config.itemPool = CloneSpawnPool(config.itemPool);
        config.enemyStatMultiplierByLevel = CloneCurve(config.enemyStatMultiplierByLevel);
        config.scaledEnemyStats = CloneCombatStats(config.scaledEnemyStats);
    }

    public static SpawnBudget CloneBudget(SpawnBudget source)
    {
        if (source == null) return null;

        return new SpawnBudget
        {
            minPerRoom = source.minPerRoom,
            maxPerRoom = source.maxPerRoom,
            minPerLevel = source.minPerLevel,
            maxPerLevel = source.maxPerLevel,
            additionalMinPerLevel = source.additionalMinPerLevel,
            additionalMaxPerLevel = source.additionalMaxPerLevel
        };
    }

    public static List<WeightedSpawnEntry> CloneSpawnPool(List<WeightedSpawnEntry> source)
    {
        List<WeightedSpawnEntry> clone = new List<WeightedSpawnEntry>();
        if (source == null) return clone;

        for (int i = 0; i < source.Count; i++)
        {
            WeightedSpawnEntry entry = source[i];
            if (entry == null)
            {
                clone.Add(null);
                continue;
            }

            clone.Add(new WeightedSpawnEntry
            {
                id = entry.id,
                prefab = entry.prefab,
                weight = entry.weight,
                minLevel = entry.minLevel,
                maxLevel = entry.maxLevel,
                maxPerLevel = entry.maxPerLevel
            });
        }

        return clone;
    }

    public static AnimationCurve CloneCurve(AnimationCurve source)
    {
        if (source == null) return null;

        AnimationCurve clone = new AnimationCurve(source.keys)
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };

        return clone;
    }

    public static List<CombatStatType> CloneCombatStats(List<CombatStatType> source)
    {
        return source != null
            ? new List<CombatStatType>(source)
            : new List<CombatStatType>();
    }
}
