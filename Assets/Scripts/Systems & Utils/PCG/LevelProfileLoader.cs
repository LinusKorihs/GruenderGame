using UnityEngine;

public class LevelProfileLoader : MonoBehaviour
{
    [Header("Profile")]
    [SerializeField] private LevelConfigProfile levelProfile;

    [Header("Targets")]
    [SerializeField] private RoomAssemblerGenerator roomAssemblerGenerator;
    [SerializeField] private LevelContentSpawner levelContentSpawner;

    [Header("Runtime")]
    [SerializeField] private bool applyOnAwake = true;
    [SerializeField] private bool autoFindMissingTargets = true;
    [SerializeField] private bool logProfileApplication = true;

    public LevelConfigProfile LevelProfile => levelProfile;
    public RoomAssemblerConfig RuntimeRoomAssemblerConfig { get; private set; }
    public LevelContentSpawnConfig RuntimeSpawnConfig { get; private set; }

    private void Awake()
    {
        if (applyOnAwake)
        {
            ApplyLevelProfile();
        }
    }

    [ContextMenu("Apply Level Profile")]
    public void ApplyLevelProfile()
    {
        RuntimeRoomAssemblerConfig = null;
        RuntimeSpawnConfig = null;

        if (levelProfile == null)
        {
            Debug.LogWarning("[Level Profile] No LevelConfigProfile assigned. Existing scene configs remain unchanged.", this);
            return;
        }

        Log(
            $"Applying '{levelProfile.DisplayName}' (Id='{levelProfile.levelId}', Index={levelProfile.levelIndex}, Type={levelProfile.levelType}).");

        if (autoFindMissingTargets)
        {
            ResolveMissingTargets();
        }

        Log(
            $"Targets: RoomAssemblerGenerator={(roomAssemblerGenerator != null ? roomAssemblerGenerator.name : "missing")}, " +
            $"LevelContentSpawner={(levelContentSpawner != null ? levelContentSpawner.name : "missing")}.");

        ApplyLevelIndex();

        if (levelProfile.pcgConfigProfile == null)
        {
            Debug.LogWarning($"[Level Profile] {levelProfile.DisplayName}: no PCGConfigProfile assigned. PCG modules skipped.", this);
            return;
        }

        ApplyRoomAssemblerConfig(levelProfile.pcgConfigProfile);
        ApplySpawnConfig(levelProfile.pcgConfigProfile);

        if (roomAssemblerGenerator != null && levelContentSpawner != null && roomAssemblerGenerator.contentSpawner == null)
        {
            roomAssemblerGenerator.contentSpawner = levelContentSpawner;
            Log($"Linked '{levelContentSpawner.name}' as content spawner on '{roomAssemblerGenerator.name}'.");
        }

        Log(
            $"Finished '{levelProfile.DisplayName}'. " +
            $"AssemblerRuntime={(RuntimeRoomAssemblerConfig != null ? RuntimeRoomAssemblerConfig.name : "none")}, " +
            $"SpawnRuntime={(RuntimeSpawnConfig != null ? RuntimeSpawnConfig.name : "none")}.");
    }

    public void SetLevelProfile(LevelConfigProfile profile, bool applyImmediately = true)
    {
        levelProfile = profile;
        if (applyImmediately)
        {
            ApplyLevelProfile();
        }
    }

    private void ApplyRoomAssemblerConfig(PCGConfigProfile pcgConfigProfile)
    {
        RuntimeRoomAssemblerConfig = pcgConfigProfile.CreateRuntimeRoomAssemblerConfig(this);
        if (RuntimeRoomAssemblerConfig == null)
        {
            if (roomAssemblerGenerator != null)
            {
                roomAssemblerGenerator.SetRuntimeConfig(null);
            }

            return;
        }

        if (roomAssemblerGenerator == null)
        {
            Debug.LogWarning($"[Level Profile] {levelProfile.DisplayName}: RoomAssemblerGenerator target missing. Runtime assembler config was created but not applied.", this);
            return;
        }

        roomAssemblerGenerator.SetRuntimeConfig(RuntimeRoomAssemblerConfig);
        Log($"Applied runtime assembler config '{RuntimeRoomAssemblerConfig.name}' to '{roomAssemblerGenerator.name}'.");
    }

    private void ApplySpawnConfig(PCGConfigProfile pcgConfigProfile)
    {
        RuntimeSpawnConfig = pcgConfigProfile.CreateRuntimeSpawnConfig(this);
        if (RuntimeSpawnConfig == null)
        {
            if (levelContentSpawner != null)
            {
                levelContentSpawner.SetRuntimeConfig(null);
            }

            return;
        }

        if (levelContentSpawner == null)
        {
            Debug.LogWarning($"[Level Profile] {levelProfile.DisplayName}: LevelContentSpawner target missing. Runtime spawn config was created but not applied.", this);
            return;
        }

        levelContentSpawner.SetRuntimeConfig(RuntimeSpawnConfig);
        levelContentSpawner.LevelIndex = levelProfile.levelIndex;
        Log($"Applied runtime spawn config '{RuntimeSpawnConfig.name}' to '{levelContentSpawner.name}' with LevelIndex={levelContentSpawner.LevelIndex}.");
    }

    private void ApplyLevelIndex()
    {
        if (levelContentSpawner != null)
        {
            levelContentSpawner.LevelIndex = levelProfile.levelIndex;
            Log($"Set '{levelContentSpawner.name}' LevelIndex={levelContentSpawner.LevelIndex}.");
        }
    }

    private void ResolveMissingTargets()
    {
        if (roomAssemblerGenerator == null)
        {
            roomAssemblerGenerator = GetComponentInChildren<RoomAssemblerGenerator>();
        }

        if (levelContentSpawner == null)
        {
            levelContentSpawner = GetComponentInChildren<LevelContentSpawner>();
        }

        if (roomAssemblerGenerator == null)
        {
            roomAssemblerGenerator = FindFirstObjectByType<RoomAssemblerGenerator>();
        }

        if (levelContentSpawner == null)
        {
            levelContentSpawner = FindFirstObjectByType<LevelContentSpawner>();
        }
    }

    private void Log(string message)
    {
        if (!logProfileApplication) return;
        Debug.Log($"[Level Profile] {message}", this);
    }
}
