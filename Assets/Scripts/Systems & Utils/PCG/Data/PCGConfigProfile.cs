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
        }

        return runtimeConfig;
    }
}
