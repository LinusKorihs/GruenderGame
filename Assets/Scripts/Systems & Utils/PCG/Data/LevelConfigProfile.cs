using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public enum LevelProfileType
{
    PCG,
    Tutorial,
    Boss,
    Menu,
    End
}

[CreateAssetMenu(menuName = "SO/PCG/Profiles/Level Config Profile", fileName = "LevelConfigProfile")]
public class LevelConfigProfile : ScriptableObject
{
    [Header("Level Identity")]
    public string levelId = "level_01";
    public string levelName = "Level 1";
    [Min(1)] public int levelIndex = 1;
    public LevelProfileType levelType = LevelProfileType.PCG;

    [Header("PCG")]
    public PCGConfigProfile pcgConfigProfile;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(levelName)) return levelName;
            if (!string.IsNullOrWhiteSpace(levelId)) return levelId;
            return name;
        }
    }

    private void OnValidate()
    {
        levelIndex = Mathf.Max(1, levelIndex);
    }

    [ContextMenu("Validate Level Profile")]
    private void ValidateLevelProfile()
    {
        PCGProfileValidator.ValidateAndLog(this, this);
    }
}

public enum PCGProfileValidationSeverity
{
    Info,
    Warning,
    Error
}

[Serializable]
public sealed class PCGProfileValidationMessage
{
    public PCGProfileValidationSeverity severity;
    public string message;
}

public sealed class PCGProfileValidationResult
{
    private readonly List<PCGProfileValidationMessage> messages = new List<PCGProfileValidationMessage>();

    public IReadOnlyList<PCGProfileValidationMessage> Messages => messages;
    public int InfoCount { get; private set; }
    public int WarningCount { get; private set; }
    public int ErrorCount { get; private set; }
    public bool HasErrors => ErrorCount > 0;

    public void AddInfo(string message)
    {
        Add(PCGProfileValidationSeverity.Info, message);
    }

    public void AddWarning(string message)
    {
        Add(PCGProfileValidationSeverity.Warning, message);
    }

    public void AddError(string message)
    {
        Add(PCGProfileValidationSeverity.Error, message);
    }

    public void LogToConsole(Object context)
    {
        Debug.Log(
            $"[Level Profile Validator] Result: {ErrorCount} error(s), {WarningCount} warning(s), {InfoCount} info message(s).",
            context);

        for (int i = 0; i < messages.Count; i++)
        {
            PCGProfileValidationMessage entry = messages[i];
            string text = $"[Level Profile Validator] {entry.message}";

            switch (entry.severity)
            {
                case PCGProfileValidationSeverity.Error:
                    Debug.LogError(text, context);
                    break;
                case PCGProfileValidationSeverity.Warning:
                    Debug.LogWarning(text, context);
                    break;
                default:
                    Debug.Log(text, context);
                    break;
            }
        }
    }

    private void Add(PCGProfileValidationSeverity severity, string message)
    {
        messages.Add(new PCGProfileValidationMessage
        {
            severity = severity,
            message = message
        });

        switch (severity)
        {
            case PCGProfileValidationSeverity.Error:
                ErrorCount++;
                break;
            case PCGProfileValidationSeverity.Warning:
                WarningCount++;
                break;
            default:
                InfoCount++;
                break;
        }
    }
}

public static class PCGProfileValidator
{
    public static PCGProfileValidationResult ValidateAndLog(LevelConfigProfile profile, Object context = null)
    {
        PCGProfileValidationResult result = Validate(profile);
        result.LogToConsole(context != null ? context : profile);
        return result;
    }

    public static PCGProfileValidationResult Validate(LevelConfigProfile profile)
    {
        PCGProfileValidationResult result = new PCGProfileValidationResult();

        if (profile == null)
        {
            result.AddError("LevelConfigProfile is missing.");
            return result;
        }

        ValidateLevelProfile(profile, result);

        if (profile.levelType != LevelProfileType.PCG)
        {
            result.AddInfo($"{profile.name}: LevelType is {profile.levelType}. PCG validation is informational until non-PCG modules exist.");
            return result;
        }

        PCGConfigProfile pcgProfile = profile.pcgConfigProfile;
        if (pcgProfile == null)
        {
            result.AddError($"{profile.name}: PCGConfigProfile is missing.");
            return result;
        }

        ValidatePCGProfile(pcgProfile, result);
        return result;
    }

    private static void ValidateLevelProfile(LevelConfigProfile profile, PCGProfileValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(profile.levelId))
        {
            result.AddWarning($"{profile.name}: levelId is empty. Saves and flow logic will need a stable id later.");
        }

        if (profile.levelIndex < 1)
        {
            result.AddError($"{profile.name}: levelIndex must be at least 1.");
        }
    }

    private static void ValidatePCGProfile(PCGConfigProfile profile, PCGProfileValidationResult result)
    {
        if (profile.roomAssemblerBaseConfig == null)
        {
            result.AddWarning($"{profile.name}: RoomAssembler base config is missing. Room generation will be skipped/blocked, but spawn validation can continue.");
        }
        else
        {
            ValidateRoomAssemblerBase(profile.roomAssemblerBaseConfig, result);
            ValidateRoomAssemblerTuning(profile.roomAssemblerTuningProfile, result);
        }

        if (profile.roomAssemblerTuningProfile == null)
        {
            result.AddInfo($"{profile.name}: RoomAssembler tuning is missing. Base assembler fallback values will be used.");
        }

        if (profile.spawnBaseConfig == null)
        {
            result.AddWarning($"{profile.name}: Spawn base config is missing. Content spawning will be skipped/blocked, but room generation can continue.");
        }
        else
        {
            ValidateSpawnBase(profile.spawnBaseConfig, result);
            ValidateSpawnTuning(profile.spawnBaseConfig, profile.spawnTuningProfile, result);
        }

        if (profile.spawnTuningProfile == null)
        {
            result.AddInfo($"{profile.name}: Spawn tuning is missing. Base spawn fallback values will be used.");
        }
    }

    private static void ValidateRoomAssemblerBase(RoomAssemblerConfig config, PCGProfileValidationResult result)
    {
        if (config.startRoom == null)
            result.AddError($"{config.name}: startRoom is missing.");
        else if (config.startRoom.prefab == null)
            result.AddError($"{config.name}: startRoom '{config.startRoom.name}' has no prefab.");

        if (config.endRoom == null)
            result.AddError($"{config.name}: endRoom is missing.");
        else if (config.endRoom.prefab == null)
            result.AddError($"{config.name}: endRoom '{config.endRoom.name}' has no prefab.");

        if (config.roomPool == null || config.roomPool.Count == 0)
        {
            result.AddError($"{config.name}: roomPool is empty.");
        }
        else
        {
            for (int i = 0; i < config.roomPool.Count; i++)
            {
                RoomDefinition room = config.roomPool[i];
                if (room == null)
                {
                    result.AddWarning($"{config.name}: roomPool entry {i} is empty.");
                    continue;
                }

                if (room.prefab == null)
                {
                    result.AddWarning($"{config.name}: roomPool entry '{room.name}' has no prefab.");
                }
            }
        }

        if (config.wallCapRoom == null)
            result.AddWarning($"{config.name}: wallCapRoom is missing. Capping can still use dead ends, but fallback wall capping is unavailable.");

        if (config.roomOverlapMask == 0)
            result.AddWarning($"{config.name}: roomOverlapMask is 0.");

        if (config.maxRooms < config.minRooms)
            result.AddWarning($"{config.name}: base maxRooms is below minRooms. Runtime tuning may override this.");

        if (config.attemptsPerOpenSocket < 1 || config.maxGenerationRetries < 1)
            result.AddWarning($"{config.name}: base attempts/retries contain values below 1. Runtime tuning may override this.");
    }

    private static void ValidateRoomAssemblerTuning(RoomAssemblerTuningProfile tuning, PCGProfileValidationResult result)
    {
        if (tuning == null) return;

        if (tuning.maxRooms < tuning.minRooms)
            result.AddWarning($"{tuning.name}: maxRooms is below minRooms. Runtime apply will clamp maxRooms.");

        if (tuning.maxEndDistanceRooms < tuning.minEndDistanceRooms)
            result.AddWarning($"{tuning.name}: maxEndDistanceRooms is below minEndDistanceRooms. Runtime apply will clamp the max value.");

        if (tuning.attemptsPerOpenSocket < 1 || tuning.maxGenerationRetries < 1)
            result.AddError($"{tuning.name}: attempts and retries must be at least 1.");
    }

    private static void ValidateSpawnBase(LevelContentSpawnConfig config, PCGProfileValidationResult result)
    {
        if (config.playerPrefab == null)
            result.AddWarning($"{config.name}: playerPrefab is missing.");

        ValidateBasePool(config.minionPool, "Minion", config.name, result);
        ValidateBasePool(config.enemyPool, "Enemy", config.name, result);
        ValidateBasePool(config.itemPool, "Item", config.name, result);

        ValidateBudget(config.enemyBudget, "Enemy base budget", config.name, result);
        ValidateBudget(config.itemBudget, "Item base budget", config.name, result);

        if (config.enemyStatMultiplierByLevel == null)
            result.AddWarning($"{config.name}: enemyStatMultiplierByLevel is missing.");
    }

    private static void ValidateSpawnTuning(
        LevelContentSpawnConfig baseConfig,
        SpawnTuningProfile tuning,
        PCGProfileValidationResult result)
    {
        if (tuning == null) return;

        ValidateBudget(tuning.enemyBudget, "Enemy tuning budget", tuning.name, result);
        ValidateBudget(tuning.itemBudget, "Item tuning budget", tuning.name, result);
        ValidateTuningPool(baseConfig.minionPool, tuning.minionPool, "Minion", tuning.name, result);
        ValidateTuningPool(baseConfig.enemyPool, tuning.enemyPool, "Enemy", tuning.name, result);
        ValidateTuningPool(baseConfig.itemPool, tuning.itemPool, "Item", tuning.name, result);

        if (tuning.enemyStatMultiplierByLevel == null)
            result.AddWarning($"{tuning.name}: enemyStatMultiplierByLevel is missing.");
    }

    private static void ValidateBasePool(
        List<WeightedSpawnEntry> pool,
        string label,
        string ownerName,
        PCGProfileValidationResult result)
    {
        if (pool == null || pool.Count == 0)
        {
            result.AddWarning($"{ownerName}: {label} pool is empty.");
            return;
        }

        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pool.Count; i++)
        {
            WeightedSpawnEntry entry = pool[i];
            if (entry == null)
            {
                result.AddWarning($"{ownerName}: {label} pool entry {i} is empty.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.id))
            {
                result.AddWarning($"{ownerName}: {label} pool entry {i} has no id. Tuning cannot target it.");
            }
            else if (!ids.Add(entry.id))
            {
                result.AddWarning($"{ownerName}: {label} pool has duplicate id '{entry.id}'.");
            }

            if (entry.prefab == null)
                result.AddWarning($"{ownerName}: {label} pool entry '{entry.id}' has no prefab.");

            if (entry.maxLevel < entry.minLevel)
                result.AddWarning($"{ownerName}: {label} pool entry '{entry.id}' has maxLevel below minLevel.");
        }
    }

    private static void ValidateTuningPool(
        List<WeightedSpawnEntry> basePool,
        List<WeightedSpawnEntryTuning> tuningPool,
        string label,
        string ownerName,
        PCGProfileValidationResult result)
    {
        if (tuningPool == null || tuningPool.Count == 0)
            return;

        HashSet<string> baseIds = CollectIds(basePool);
        HashSet<string> tuningIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < tuningPool.Count; i++)
        {
            WeightedSpawnEntryTuning tuning = tuningPool[i];
            if (tuning == null)
            {
                result.AddWarning($"{ownerName}: {label} tuning entry {i} is empty.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(tuning.id))
            {
                result.AddWarning($"{ownerName}: {label} tuning entry {i} has no id.");
                continue;
            }

            if (!tuningIds.Add(tuning.id))
            {
                result.AddWarning($"{ownerName}: {label} tuning has duplicate id '{tuning.id}'.");
            }

            if (!baseIds.Contains(tuning.id))
            {
                result.AddWarning($"{ownerName}: {label} tuning id '{tuning.id}' was not found in the base pool.");
            }

            if (tuning.maxLevel < tuning.minLevel)
            {
                result.AddWarning($"{ownerName}: {label} tuning id '{tuning.id}' has maxLevel below minLevel. Runtime apply will clamp the max value.");
            }
        }
    }

    private static void ValidateBudget(
        SpawnBudget budget,
        string label,
        string ownerName,
        PCGProfileValidationResult result)
    {
        if (budget == null)
        {
            result.AddWarning($"{ownerName}: {label} is missing.");
            return;
        }

        if (budget.maxPerRoom < budget.minPerRoom)
            result.AddWarning($"{ownerName}: {label} maxPerRoom is below minPerRoom.");

        if (budget.maxPerLevel < budget.minPerLevel)
            result.AddWarning($"{ownerName}: {label} maxPerLevel is below minPerLevel.");
    }

    private static HashSet<string> CollectIds(List<WeightedSpawnEntry> pool)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (pool == null) return ids;

        for (int i = 0; i < pool.Count; i++)
        {
            WeightedSpawnEntry entry = pool[i];
            if (entry != null && !string.IsNullOrWhiteSpace(entry.id))
                ids.Add(entry.id);
        }

        return ids;
    }
}
