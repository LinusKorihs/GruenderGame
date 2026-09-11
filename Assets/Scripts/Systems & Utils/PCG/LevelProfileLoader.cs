using Unity.AI.Navigation;
using UnityEngine;

public class LevelProfileLoader : MonoBehaviour
{
    [SerializeField] private LevelConfigProfile levelProfile;

    [SerializeField] private RoomAssemblerGenerator roomAssemblerGenerator;
    [SerializeField] private LevelContentSpawner levelContentSpawner;
    [SerializeField] private Light directionalLight;

    [SerializeField] private bool generateAfterApply;
    [SerializeField] private bool logProfileApplication = true;

    [SerializeField] private bool useGeneratedLevelHierarchy = true;
    [SerializeField] private bool parentDirectionalLightUnderLevel = true;
    [SerializeField] private string pcgRootName = "PCG_Root";
    [SerializeField] private string generatedLevelRootName = "Generated_Level";
    [SerializeField] private string roomsRootName = "Rooms";
    [SerializeField] private string contentRootName = "Content";

    public LevelConfigProfile LevelProfile => levelProfile;
    public RoomAssemblerConfig RuntimeRoomAssemblerConfig { get; private set; }
    public LevelContentSpawnConfig RuntimeSpawnConfig { get; private set; }

    private void Awake()
    {
        bool applied = ApplyLevelProfile();
        if (applied && generateAfterApply)
        {
            GenerateRoomAssembler();
        }
    }

    [ContextMenu("Validate And Apply Level Profile")]
    public void ValidateAndApplyLevelProfile()
    {
        ApplyLevelProfile();
    }

    public bool ApplyLevelProfile()
    {
        RuntimeRoomAssemblerConfig = null;
        RuntimeSpawnConfig = null;

        if (levelProfile == null)
        {
            Debug.LogWarning("[Level Profile] No LevelConfigProfile assigned. Existing scene configs remain unchanged.", this);
            return false;
        }

        Log(
            $"Applying '{levelProfile.DisplayName}' (Id='{levelProfile.levelId}', Index={levelProfile.levelIndex}, Type={levelProfile.levelType}).");

        PCGProfileValidationResult validation = PCGProfileValidator.ValidateAndLog(levelProfile, this);
        if (validation.HasErrors)
        {
            Debug.LogWarning(
                $"[Level Profile] '{levelProfile.DisplayName}' has validation error(s). Apply blocked until the errors are fixed.",
                this);
            return false;
        }

        ResolveMissingTargets();

        Log(
            $"Targets: RoomAssemblerGenerator={(roomAssemblerGenerator != null ? roomAssemblerGenerator.name : "missing")}, " +
            $"LevelContentSpawner={(levelContentSpawner != null ? levelContentSpawner.name : "missing")}, " +
            $"DirectionalLight={(directionalLight != null ? directionalLight.name : "missing")}.");

        ConfigureRuntimeHierarchy();
        ApplyLevelIndex();

        if (levelProfile.pcgConfigProfile == null)
        {
            Debug.LogWarning($"[Level Profile] {levelProfile.DisplayName}: no PCGConfigProfile assigned. PCG modules skipped.", this);
            return false;
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

        if (generateAfterApply && roomAssemblerGenerator != null && roomAssemblerGenerator.autoGenerateOnStart)
        {
            Debug.LogWarning(
                "[Level Profile] Generate After Apply and RoomAssembler autoGenerateOnStart are both enabled. " +
                "This can generate twice when entering Play Mode.",
                this);
        }

        return true;
    }

    public void SetLevelProfile(LevelConfigProfile profile, bool applyImmediately = true)
    {
        levelProfile = profile;
        if (applyImmediately)
        {
            ApplyLevelProfile();
        }
    }

    [ContextMenu("Validate Level Profile")]
    public void ValidateLevelProfile()
    {
        PCGProfileValidator.ValidateAndLog(levelProfile, this);
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        bool canGenerate = true;
        if (levelProfile != null)
        {
            canGenerate = ApplyLevelProfile();
        }
        else
        {
            ResolveMissingTargets();
        }

        if (!canGenerate)
        {
            Debug.LogWarning("[Level Profile] Generate skipped because profile validation blocked the apply step.", this);
            return;
        }

        GenerateRoomAssembler();
    }

    [ContextMenu("Clear Generated Level Content")]
    public void ClearGeneratedLevelContent()
    {
        ResolveMissingTargets();

        FindRuntimeContainers(out Transform roomsRoot, out Transform contentRoot);

        int clearedNavMeshSurfaces = RemoveNavMeshSurfaces(roomsRoot);
        int clearedRooms = ClearChildren(roomsRoot);
        int contentChildrenBeforeClear = contentRoot != null ? contentRoot.childCount : 0;

        if (levelContentSpawner != null)
        {
            levelContentSpawner.ClearSpawnedObjects();
        }

        int clearedContent = ClearChildren(contentRoot);
        int contentClearCount = Mathf.Max(contentChildrenBeforeClear, clearedContent);

        if (roomsRoot == null && contentRoot == null)
        {
            Debug.LogWarning("[Level Profile] Clear skipped because no runtime Rooms or Content containers were found.", this);
            return;
        }

        Log(
            $"Cleared generated level content. Rooms={clearedRooms}, Content={contentClearCount}, " +
            $"NavMeshSurfaces={clearedNavMeshSurfaces}.");
    }

    private void GenerateRoomAssembler()
    {
        if (roomAssemblerGenerator == null)
        {
            Debug.LogWarning("[Level Profile] Generate skipped because no RoomAssemblerGenerator target is assigned.", this);
            return;
        }

        roomAssemblerGenerator.Generate();
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

        if (directionalLight == null)
        {
            directionalLight = FindDirectionalLightInChildren(transform);
        }

        if (roomAssemblerGenerator == null)
        {
            roomAssemblerGenerator = FindFirstObjectByType<RoomAssemblerGenerator>();
        }

        if (levelContentSpawner == null)
        {
            levelContentSpawner = FindFirstObjectByType<LevelContentSpawner>();
        }

        if (directionalLight == null)
        {
            directionalLight = FindFirstDirectionalLightInScene();
        }
    }

    private void ConfigureRuntimeHierarchy()
    {
        if (!useGeneratedLevelHierarchy)
            return;

        Transform pcgRoot = GetOrCreateChild(transform, pcgRootName);
        Transform generatedRoot = GetOrCreateChild(pcgRoot, generatedLevelRootName);
        Transform roomsRoot = GetOrCreateChild(generatedRoot, roomsRootName);
        Transform contentRoot = GetOrCreateChild(generatedRoot, contentRootName);

        if (roomAssemblerGenerator != null)
        {
            ParentRoomAssemblerUnderPcgRoot(pcgRoot);
            roomAssemblerGenerator.parent = roomsRoot;
        }

        if (levelContentSpawner != null)
        {
            levelContentSpawner.SetContentParent(contentRoot, true);
        }

        ParentDirectionalLightUnderLevel();

        Log(
            $"Runtime hierarchy ready: {GetPath(pcgRoot)}/{roomAssemblerGenerator?.name ?? "RoomAssembler missing"} " +
            $"and {GetPath(generatedRoot)}.");
    }

    private void FindRuntimeContainers(out Transform roomsRoot, out Transform contentRoot)
    {
        roomsRoot = null;
        contentRoot = null;

        Transform pcgRoot = FindChild(transform, pcgRootName);
        Transform generatedRoot = pcgRoot != null
            ? FindChild(pcgRoot, generatedLevelRootName)
            : FindChild(transform, generatedLevelRootName);

        if (generatedRoot == null)
            return;

        roomsRoot = FindChild(generatedRoot, roomsRootName);
        contentRoot = FindChild(generatedRoot, contentRootName);
    }

    private void ParentRoomAssemblerUnderPcgRoot(Transform pcgRoot)
    {
        if (pcgRoot == null || roomAssemblerGenerator == null)
            return;

        Transform roomAssemblerTransform = roomAssemblerGenerator.transform;
        if (roomAssemblerTransform.parent == pcgRoot)
            return;

        if (roomAssemblerTransform == transform || transform.IsChildOf(roomAssemblerTransform))
        {
            Debug.LogWarning(
                "[Level Profile] RoomAssemblerGenerator cannot be parented under the PCG root because it is on this GameObject or one of its parents. " +
                "Use a separate child GameObject for the RoomAssembler target.",
                this);
            return;
        }

        roomAssemblerTransform.SetParent(pcgRoot, true);
        Log($"Moved '{roomAssemblerTransform.name}' under '{GetPath(pcgRoot)}'.");
    }

    private void ParentDirectionalLightUnderLevel()
    {
        if (!parentDirectionalLightUnderLevel || directionalLight == null)
            return;

        Transform lightTransform = directionalLight.transform;
        if (lightTransform.parent == transform)
            return;

        if (lightTransform == transform || transform.IsChildOf(lightTransform))
        {
            Debug.LogWarning(
                "[Level Profile] Directional Light cannot be parented under the Level Profile Loader because it is on this GameObject or one of its parents.",
                this);
            return;
        }

        lightTransform.SetParent(transform, true);
        Log($"Moved '{lightTransform.name}' under '{GetPath(transform)}'.");
    }

    private static Light FindDirectionalLightInChildren(Transform root)
    {
        if (root == null)
            return null;

        Light[] childLights = root.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < childLights.Length; i++)
        {
            if (childLights[i] != null && childLights[i].type == LightType.Directional)
                return childLights[i];
        }

        return null;
    }

    private static Light FindFirstDirectionalLightInScene()
    {
        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        string safeName = string.IsNullOrWhiteSpace(childName) ? "Generated" : childName;
        Transform child = FindChild(parent, safeName);
        if (child != null)
            return child;

        GameObject childObject = new GameObject(safeName);
        child = childObject.transform;
        child.SetParent(parent, false);
        return child;
    }

    private static Transform FindChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        string safeName = string.IsNullOrWhiteSpace(childName) ? "Generated" : childName;
        return parent.Find(safeName);
    }

    private static int ClearChildren(Transform target)
    {
        if (target == null)
            return 0;

        int count = target.childCount;
        for (int i = target.childCount - 1; i >= 0; i--)
        {
            GameObject childObject = target.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                childObject.SetActive(false);
                Destroy(childObject);
            }
            else
            {
                DestroyImmediate(childObject);
            }
        }

        return count;
    }

    private static int RemoveNavMeshSurfaces(Transform target)
    {
        if (target == null)
            return 0;

        NavMeshSurface[] surfaces = target.GetComponents<NavMeshSurface>();
        for (int i = 0; i < surfaces.Length; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null)
                continue;

            surface.RemoveData();
            if (Application.isPlaying)
            {
                Destroy(surface);
            }
            else
            {
                DestroyImmediate(surface);
            }
        }

        return surfaces.Length;
    }

    private static string GetPath(Transform target)
    {
        if (target == null) return "missing";

        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private void Log(string message)
    {
        if (!logProfileApplication) return;
        Debug.Log($"[Level Profile] {message}", this);
    }
}
