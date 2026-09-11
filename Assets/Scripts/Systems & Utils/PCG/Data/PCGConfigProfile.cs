using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SO/PCG/Profiles/PCG Config Profile", fileName = "PCGConfigProfile")]
public class PCGConfigProfile : ScriptableObject
{
    [Header("Room Assembler")]
    [Tooltip("Base asset that keeps stable Unity references like rooms, pools, cap rooms, and layer masks.")]
    public RoomAssemblerConfig roomAssemblerBaseConfig;

    [Tooltip("Optional runtime tuning. If missing, the base assembler config values are used unchanged.")]
    public RoomAssemblerTuningProfile roomAssemblerTuningProfile;

    [Header("Content Spawner")]
    [Tooltip("Base asset that keeps stable Unity references like player, minion, enemy, and item prefabs.")]
    public LevelContentSpawnConfig spawnBaseConfig;

    [Tooltip("Optional runtime tuning. If missing, the base spawn config values are used unchanged.")]
    public SpawnTuningProfile spawnTuningProfile;

    public RoomAssemblerConfig CreateRuntimeRoomAssemblerConfig(Object logContext = null)
    {
        Object context = logContext != null ? logContext : this;
        if (roomAssemblerBaseConfig == null)
        {
            Debug.LogWarning($"[Level Profile] {name}: RoomAssembler base config missing. Room assembler module skipped.", context);
            return null;
        }

        RoomAssemblerConfig runtimeConfig = Instantiate(roomAssemblerBaseConfig);
        runtimeConfig.name = $"{roomAssemblerBaseConfig.name}_Runtime";
        runtimeConfig.roomPool = roomAssemblerBaseConfig.roomPool != null
            ? new List<RoomDefinition>(roomAssemblerBaseConfig.roomPool)
            : new List<RoomDefinition>();

        if (roomAssemblerTuningProfile != null)
        {
            roomAssemblerTuningProfile.ApplyTo(runtimeConfig, context);
            Debug.Log(
                $"[Level Profile] {name}: created runtime RoomAssemblerConfig from base '{roomAssemblerBaseConfig.name}' " +
                $"with tuning '{roomAssemblerTuningProfile.name}'. Rooms={runtimeConfig.minRooms}-{runtimeConfig.maxRooms}, " +
                $"EndDistance={runtimeConfig.minEndDistanceRooms}-{runtimeConfig.maxEndDistanceRooms}, " +
                $"Retries={runtimeConfig.maxGenerationRetries}+{(runtimeConfig.useEmergencyFallback ? runtimeConfig.emergencyFallbackRetries : 0)}, " +
                $"Seed={(runtimeConfig.randomSeed ? "random" : runtimeConfig.seed.ToString())}.",
                context);
        }
        else
        {
            Debug.Log(
                $"[Level Profile] {name}: created runtime RoomAssemblerConfig from base '{roomAssemblerBaseConfig.name}' without tuning. Base fallback values are active.",
                context);
        }

        return runtimeConfig;
    }

    public LevelContentSpawnConfig CreateRuntimeSpawnConfig(Object logContext = null)
    {
        Object context = logContext != null ? logContext : this;
        if (spawnBaseConfig == null)
        {
            Debug.LogWarning($"[Level Profile] {name}: Spawn base config missing. Content spawner module skipped.", context);
            return null;
        }

        LevelContentSpawnConfig runtimeConfig = Instantiate(spawnBaseConfig);
        runtimeConfig.name = $"{spawnBaseConfig.name}_Runtime";
        PCGProfileCopyUtility.DetachRuntimeSpawnConfig(runtimeConfig);

        if (spawnTuningProfile != null)
        {
            spawnTuningProfile.ApplyTo(runtimeConfig, context);
            Debug.Log(
                $"[Level Profile] {name}: created runtime LevelContentSpawnConfig from base '{spawnBaseConfig.name}' " +
                $"with tuning '{spawnTuningProfile.name}'. Spawn(Player={runtimeConfig.spawnPlayer}, Minions={runtimeConfig.spawnMinions}, " +
                $"Enemies={runtimeConfig.spawnEnemies}, Items={runtimeConfig.spawnItems}), " +
                $"EnemyBudget={FormatBudget(runtimeConfig.enemyBudget)}, ItemBudget={FormatBudget(runtimeConfig.itemBudget)}, " +
                $"Scaling={runtimeConfig.scalingMode}.",
                context);
        }
        else
        {
            Debug.Log(
                $"[Level Profile] {name}: created runtime LevelContentSpawnConfig from base '{spawnBaseConfig.name}' without tuning. Base fallback values are active.",
                context);
        }

        return runtimeConfig;
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
