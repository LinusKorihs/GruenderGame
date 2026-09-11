using System;
using System.Collections.Generic;
using UnityEngine;

public enum LevelDifficultyScalingMode
{
    None,
    SpawnAmounts,
    EnemyStats,
    SpawnAmountsAndEnemyStats
}

[Serializable]
public class WeightedSpawnEntry
{
    public string id;
    public GameObject prefab;
    [Min(0)] public int weight = 10;
    [Min(1)] public int minLevel = 1;
    [Min(1)] public int maxLevel = 999;
    [Min(0)] public int maxPerLevel = 999;
}

[Serializable]
public class SpawnBudget
{
    [Header("Per Room")]
    [Min(0)] public int minPerRoom;
    [Min(0)] public int maxPerRoom = 3;

    [Header("Whole Level")]
    [Min(0)] public int minPerLevel;
    [Min(0)] public int maxPerLevel = 20;

    [Header("Level Growth")]
    [Tooltip("Added to the level total minimum for each level after level 1 when spawn amount scaling is enabled.")]
    [Min(0)] public int additionalMinPerLevel;

    [Tooltip("Added to the level total maximum for each level after level 1 when spawn amount scaling is enabled.")]
    [Min(0)] public int additionalMaxPerLevel = 2;

    public int GetScaledMin(int levelIndex, bool scaleAmounts)
    {
        int levelOffset = Mathf.Max(0, levelIndex - 1);
        return Mathf.Max(0, minPerLevel + (scaleAmounts ? additionalMinPerLevel * levelOffset : 0));
    }

    public int GetScaledMax(int levelIndex, bool scaleAmounts)
    {
        int levelOffset = Mathf.Max(0, levelIndex - 1);
        int scaled = maxPerLevel + (scaleAmounts ? additionalMaxPerLevel * levelOffset : 0);
        return Mathf.Max(GetScaledMin(levelIndex, scaleAmounts), scaled);
    }
}

[CreateAssetMenu(menuName = "SO/PCG/Base/Level Content Spawn Config", fileName = "LevelContentSpawnConfig")]
public class LevelContentSpawnConfig : ScriptableObject
{
    [Header("Player")]
    public GameObject playerPrefab;
    public bool spawnPlayer = true;

    [Header("Minions")]
    public bool spawnMinions = true;
    public List<WeightedSpawnEntry> minionPool = new List<WeightedSpawnEntry>();

    [Header("Enemies")]
    public bool spawnEnemies = true;
    public SpawnBudget enemyBudget = new SpawnBudget { minPerRoom = 0, maxPerRoom = 3, minPerLevel = 5, maxPerLevel = 15 };
    public List<WeightedSpawnEntry> enemyPool = new List<WeightedSpawnEntry>();

    [Header("Items")]
    public bool spawnItems = true;
    public SpawnBudget itemBudget = new SpawnBudget { minPerRoom = 0, maxPerRoom = 1, minPerLevel = 1, maxPerLevel = 5 };
    public List<WeightedSpawnEntry> itemPool = new List<WeightedSpawnEntry>();

    [Header("Difficulty Scaling")]
    public LevelDifficultyScalingMode scalingMode = LevelDifficultyScalingMode.SpawnAmounts;

    [Tooltip("Enemy stat multipliers applied once to spawned enemies when enemy stat scaling is enabled. X = level, Y = multiplier.")]
    public AnimationCurve enemyStatMultiplierByLevel = AnimationCurve.Linear(1f, 1f, 10f, 2f);

    public List<CombatStatType> scaledEnemyStats = new List<CombatStatType>
    {
        CombatStatType.MaxHealth,
        CombatStatType.Damage
    };

    [Header("Debug")]
    public bool log;

    public bool ScalesSpawnAmounts =>
        scalingMode == LevelDifficultyScalingMode.SpawnAmounts ||
        scalingMode == LevelDifficultyScalingMode.SpawnAmountsAndEnemyStats;

    public bool ScalesEnemyStats =>
        scalingMode == LevelDifficultyScalingMode.EnemyStats ||
        scalingMode == LevelDifficultyScalingMode.SpawnAmountsAndEnemyStats;
}
