using Unity.AI.Navigation;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LevelProfileLoader : MonoBehaviour
{
    [Header("Profiles")]
    [SerializeField] private LevelConfigProfile levelProfile;
    [SerializeField] private LevelAtmosphereProfile levelAtmosphereProfile;

    [SerializeField, HideInInspector] private RoomAssemblerGenerator roomAssemblerGenerator;
    [SerializeField, HideInInspector] private LevelContentSpawner levelContentSpawner;
    [SerializeField, HideInInspector] private LevelAtmosphereController levelAtmosphereController;

    [Header("Generation")]
    [SerializeField] private bool generateAfterApply;
    [SerializeField] private bool logProfileApplication = true;

    [Header("Runtime Hierarchy")]
    [SerializeField] private bool useGeneratedLevelHierarchy = true;
    [SerializeField] private string pcgRootName = "PCG_Root";
    [SerializeField] private string generatedLevelRootName = "Generated_Level";
    [SerializeField] private string roomsRootName = "Rooms";
    [SerializeField] private string contentRootName = "Content";

    public LevelConfigProfile LevelProfile => levelProfile;
    public LevelAtmosphereProfile LevelAtmosphereProfile => levelAtmosphereProfile;
    public RoomAssemblerGenerator RoomAssemblerGenerator => roomAssemblerGenerator;
    public LevelContentSpawner LevelContentSpawner => levelContentSpawner;
    public LevelAtmosphereController LevelAtmosphereController => levelAtmosphereController;
    public RoomAssemblerConfig RuntimeRoomAssemblerConfig { get; private set; }
    public LevelContentSpawnConfig RuntimeSpawnConfig { get; private set; }

    private Transform runtimeLevelRootOverride;

    private void Awake()
    {
        ResolveMissingTargets();
        ResolveAtmosphereController();
        DisableRoomAssemblerAutoGeneration();

        if (levelProfile == null && levelAtmosphereProfile == null)
        {
            return;
        }

        bool profileApplied = levelProfile == null || ApplyLevelProfile();
        ApplyLevelAtmosphereProfile();
        if (profileApplied && levelProfile != null && generateAfterApply)
        {
            GenerateRoomAssembler();
        }
    }

    [ContextMenu("Validate And Apply Level Profile")]
    public void ValidateAndApplyLevelProfile()
    {
        bool applied = ApplyLevelProfile();
        ApplyLevelAtmosphereProfile();
        Log($"Validate and apply finished. LevelProfileApplied={applied}.");
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
        DisableRoomAssemblerAutoGeneration();

        Log(
            $"Targets: RoomAssemblerGenerator={(roomAssemblerGenerator != null ? roomAssemblerGenerator.name : "missing")}, " +
            $"LevelContentSpawner={(levelContentSpawner != null ? levelContentSpawner.name : "missing")}.");

        ConfigureRuntimeHierarchy();
        ApplyLevelIndex();

        if (levelProfile.levelType != LevelProfileType.PCG)
        {
            Log($"'{levelProfile.DisplayName}' is {levelProfile.levelType}. PCG config apply skipped.");
            return true;
        }

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

    public bool ApplyLevelProfile(LevelConfigProfile profile)
    {
        levelProfile = profile;
        return ApplyLevelProfile();
    }

    public void SetLevelAtmosphereProfile(LevelAtmosphereProfile profile, bool applyImmediately = true)
    {
        levelAtmosphereProfile = profile;
        if (applyImmediately)
        {
            ApplyLevelAtmosphereProfile();
        }
    }

    public bool ApplyLevelAtmosphereProfile(LevelAtmosphereProfile profile = null, Transform runtimeLevelRoot = null)
    {
        LevelAtmosphereProfile activeProfile = ResolveActiveAtmosphereProfile(profile);
        if (activeProfile == null)
        {
            Log("No LevelAtmosphereProfile assigned. Atmosphere apply skipped.");
            return false;
        }

        ResolveAtmosphereController();
        if (levelAtmosphereController == null)
        {
            Debug.LogWarning("[Level Profile] Atmosphere apply skipped because no LevelAtmosphereController was found in the LevelSystem or active scene.", this);
            return false;
        }

        levelAtmosphereProfile = activeProfile;
        levelAtmosphereController.Apply(activeProfile, runtimeLevelRoot != null ? runtimeLevelRoot : runtimeLevelRootOverride);
        Log($"Applied atmosphere profile '{activeProfile.name}'.");
        return true;
    }

    public void ConfigureTargets(RoomAssemblerGenerator roomAssembler, LevelContentSpawner contentSpawner, Light levelLight = null)
    {
        if (roomAssembler != null)
        {
            roomAssemblerGenerator = roomAssembler;
        }

        if (contentSpawner != null)
        {
            levelContentSpawner = contentSpawner;
        }

        _ = levelLight;
    }

    public void ConfigureAtmosphere(LevelAtmosphereController controller)
    {
        if (controller != null)
        {
            levelAtmosphereController = controller;
        }
    }

    public void SetRuntimeLevelRoot(Transform levelRoot)
    {
        runtimeLevelRootOverride = levelRoot;

        if (levelRoot != null && useGeneratedLevelHierarchy)
        {
            ResolveMissingTargets();
            ConfigureRuntimeHierarchy();
        }
    }

    public void ResolveSceneTargets()
    {
        ResolveMissingTargets();
        ResolveAtmosphereController();
    }

    [ContextMenu("Validate Level Profile")]
    public void ValidateLevelProfile()
    {
        PCGProfileValidator.ValidateAndLog(levelProfile, this);
        ValidateAtmosphereProfile();
    }

    [ContextMenu("Validate Atmosphere Profile")]
    public void ValidateAtmosphereProfile()
    {
        LevelAtmosphereProfile activeProfile = ResolveActiveAtmosphereProfile(null);
        if (activeProfile == null)
        {
            Log("No LevelAtmosphereProfile assigned. This is valid when the active LevelFlow step also has none.");
            return;
        }

        ResolveAtmosphereController();
        if (levelAtmosphereController == null)
        {
            Debug.LogWarning($"[Level Profile] Atmosphere profile '{activeProfile.name}' is assigned, but no LevelAtmosphereController was found.", this);
            return;
        }

        Log($"Atmosphere profile '{activeProfile.name}' is ready for '{levelAtmosphereController.name}'.");
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (levelProfile == null)
        {
            Debug.LogWarning("[Level Profile] Generate skipped because no LevelConfigProfile is assigned. Assign a profile on PF_LevelLoader first.", this);
            return;
        }

        bool canGenerate = ApplyLevelProfile();
        if (!canGenerate)
        {
            Debug.LogWarning("[Level Profile] Generate skipped because profile validation blocked the apply step.", this);
            return;
        }

        ApplyLevelAtmosphereProfile();

        if (levelProfile.levelType != LevelProfileType.PCG)
        {
            Log($"Generate skipped because '{levelProfile.DisplayName}' is {levelProfile.levelType}, not PCG.");
            return;
        }

        GenerateRoomAssembler();
    }

    [ContextMenu("Clear Generated Level Content")]
    public void ClearGeneratedLevelContent()
    {
        ResolveMissingTargets();
        ResolveAtmosphereController();

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
        }
        else
        {
            Log(
                $"Cleared generated level content. Rooms={clearedRooms}, Content={contentClearCount}, " +
                $"NavMeshSurfaces={clearedNavMeshSurfaces}.");
        }

        ClearRuntimeAtmosphereArtifacts();
        ClearGeneratedHierarchyRoots();
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
            roomAssemblerGenerator = GetComponentInChildren<RoomAssemblerGenerator>(true);
        }

        if (levelContentSpawner == null)
        {
            levelContentSpawner = GetComponentInChildren<LevelContentSpawner>(true);
        }

        Transform searchRoot = runtimeLevelRootOverride != null ? runtimeLevelRootOverride : transform.root;
        if (roomAssemblerGenerator == null && searchRoot != null)
        {
            roomAssemblerGenerator = searchRoot.GetComponentInChildren<RoomAssemblerGenerator>(true);
        }

        if (levelContentSpawner == null && searchRoot != null)
        {
            levelContentSpawner = searchRoot.GetComponentInChildren<LevelContentSpawner>(true);
        }

        if (roomAssemblerGenerator == null)
        {
            roomAssemblerGenerator = FindFirstObjectByType<RoomAssemblerGenerator>(FindObjectsInactive.Include);
        }

        if (levelContentSpawner == null)
        {
            levelContentSpawner = FindFirstObjectByType<LevelContentSpawner>(FindObjectsInactive.Include);
        }

    }

    private void ResolveAtmosphereController()
    {
        if (levelAtmosphereController != null)
            return;

        if (LevelSystemController.Instance != null && LevelSystemController.Instance.LevelAtmosphere != null)
        {
            levelAtmosphereController = LevelSystemController.Instance.LevelAtmosphere;
            return;
        }

        Transform searchRoot = runtimeLevelRootOverride != null ? runtimeLevelRootOverride : transform.root;
        if (searchRoot != null)
        {
            levelAtmosphereController = searchRoot.GetComponentInChildren<LevelAtmosphereController>(true);
            if (levelAtmosphereController != null)
                return;
        }

        levelAtmosphereController = FindFirstObjectByType<LevelAtmosphereController>(FindObjectsInactive.Include);
    }

    private LevelAtmosphereProfile ResolveActiveAtmosphereProfile(LevelAtmosphereProfile requestedProfile)
    {
        if (requestedProfile != null)
            return requestedProfile;

        if (levelAtmosphereProfile != null)
            return levelAtmosphereProfile;

        LevelFlowStep currentStep = LevelFlowController.Instance != null ? LevelFlowController.Instance.CurrentStep : null;
        return currentStep != null ? currentStep.atmosphereProfile : null;
    }

    private void ConfigureRuntimeHierarchy()
    {
        if (!useGeneratedLevelHierarchy)
            return;

        Transform hierarchyRoot = GetRuntimeHierarchyRoot();
        Transform pcgRoot = GetOrCreateChild(hierarchyRoot, pcgRootName);
        Transform generatedRoot = GetOrCreateChild(pcgRoot, generatedLevelRootName);
        Transform roomsRoot = GetOrCreateChild(generatedRoot, roomsRootName);
        Transform contentRoot = GetOrCreateChild(generatedRoot, contentRootName);

        if (roomAssemblerGenerator != null)
        {
            if (runtimeLevelRootOverride != null && runtimeLevelRootOverride != transform)
            {
                ParentRoomAssemblerUnderLoader();
            }
            else
            {
                ParentRoomAssemblerUnderPcgRoot(pcgRoot);
            }

            roomAssemblerGenerator.parent = roomsRoot;
        }

        if (levelContentSpawner != null)
        {
            levelContentSpawner.SetContentParent(contentRoot, true);
        }

        RemoveUnusedLocalGeneratedHierarchy();

        Log(
            $"Runtime hierarchy ready: {GetPath(pcgRoot)}/{roomAssemblerGenerator?.name ?? "RoomAssembler missing"} " +
            $"and {GetPath(generatedRoot)}.");
    }

    private void FindRuntimeContainers(out Transform roomsRoot, out Transform contentRoot)
    {
        roomsRoot = null;
        contentRoot = null;

        Transform hierarchyRoot = GetRuntimeHierarchyRoot();
        Transform pcgRoot = FindChild(hierarchyRoot, pcgRootName);
        Transform generatedRoot = pcgRoot != null
            ? FindChild(pcgRoot, generatedLevelRootName)
            : FindChild(hierarchyRoot, generatedLevelRootName);

        if (generatedRoot == null)
            return;

        roomsRoot = FindChild(generatedRoot, roomsRootName);
        contentRoot = FindChild(generatedRoot, contentRootName);
    }

    private void ClearRuntimeAtmosphereArtifacts()
    {
        if (levelAtmosphereController != null)
        {
            levelAtmosphereController.ClearRuntimeObjects(runtimeLevelRootOverride);
            return;
        }

        if (LevelSystemController.Instance != null && LevelSystemController.Instance.FogOfWar != null)
        {
            LevelSystemController.Instance.FogOfWar.ClearRuntimeObjects();
        }
    }

    private void ClearGeneratedHierarchyRoots()
    {
        if (!useGeneratedLevelHierarchy)
            return;

        Transform hierarchyRoot = GetRuntimeHierarchyRoot();
        Transform pcgRoot = FindChild(hierarchyRoot, pcgRootName);
        Transform generatedRoot = pcgRoot != null
            ? FindChild(pcgRoot, generatedLevelRootName)
            : FindChild(hierarchyRoot, generatedLevelRootName);

        if (pcgRoot != null)
        {
            if (ContainsPersistentGeneratorTarget(pcgRoot))
            {
                if (generatedRoot != null && !ContainsPersistentGeneratorTarget(generatedRoot))
                {
                    string generatedRootPath = GetPath(generatedRoot);
                    DestroyRuntime(generatedRoot.gameObject);
                    Log($"Removed generated hierarchy '{generatedRootPath}'.");
                }

                Log($"Kept '{GetPath(pcgRoot)}' because it contains the persistent RoomAssembler target.");
                return;
            }

            string pcgRootPath = GetPath(pcgRoot);
            DestroyRuntime(pcgRoot.gameObject);
            Log($"Removed generated hierarchy '{pcgRootPath}'.");
            return;
        }

        if (generatedRoot != null && !ContainsPersistentGeneratorTarget(generatedRoot))
        {
            string generatedRootPath = GetPath(generatedRoot);
            DestroyRuntime(generatedRoot.gameObject);
            Log($"Removed generated hierarchy '{generatedRootPath}'.");
        }
    }

    private bool ContainsPersistentGeneratorTarget(Transform target)
    {
        if (target == null)
            return false;

        return roomAssemblerGenerator != null && roomAssemblerGenerator.transform.IsChildOf(target);
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

        if (!CanReparentRoomAssembler(roomAssemblerTransform, pcgRoot))
            return;

        roomAssemblerTransform.SetParent(pcgRoot, true);
        Log($"Moved '{roomAssemblerTransform.name}' under '{GetPath(pcgRoot)}'.");
    }

    private void ParentRoomAssemblerUnderLoader()
    {
        if (roomAssemblerGenerator == null)
            return;

        Transform roomAssemblerTransform = roomAssemblerGenerator.transform;
        if (roomAssemblerTransform.parent == transform)
            return;

        if (roomAssemblerTransform == transform || transform.IsChildOf(roomAssemblerTransform))
        {
            Debug.LogWarning(
                "[Level Profile] RoomAssemblerGenerator cannot be parented under the LevelProfileLoader because it is on this GameObject or one of its parents.",
                this);
            return;
        }

        if (!CanReparentRoomAssembler(roomAssemblerTransform, transform))
            return;

        roomAssemblerTransform.SetParent(transform, true);
        Log($"Kept '{roomAssemblerTransform.name}' under persistent loader while its output targets the active runtime scene.");
    }

    private bool CanReparentRoomAssembler(Transform roomAssemblerTransform, Transform targetParent)
    {
        if (roomAssemblerTransform == null || targetParent == null)
            return false;

#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(roomAssemblerTransform))
        {
            Log(
                $"Skipped moving '{roomAssemblerTransform.name}' under '{GetPath(targetParent)}' because it belongs to a Prefab instance. " +
                "Its generated output still targets the runtime hierarchy.");
            return false;
        }
#endif

        return true;
    }

    private void RemoveUnusedLocalGeneratedHierarchy()
    {
        if (runtimeLevelRootOverride == null || runtimeLevelRootOverride == transform)
            return;

        Transform localPcgRoot = FindChild(transform, pcgRootName);
        if (localPcgRoot == null)
            return;

        if (roomAssemblerGenerator != null && roomAssemblerGenerator.transform.IsChildOf(localPcgRoot))
            return;

        DestroyRuntime(localPcgRoot.gameObject);
        Log($"Removed unused local '{pcgRootName}' under '{GetPath(transform)}'.");
    }

    private Transform GetRuntimeHierarchyRoot()
    {
        return runtimeLevelRootOverride != null ? runtimeLevelRootOverride : transform;
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

    private static void DestroyRuntime(GameObject target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
        {
            target.SetActive(false);
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void DisableRoomAssemblerAutoGeneration()
    {
        if (roomAssemblerGenerator == null || !roomAssemblerGenerator.autoGenerateOnStart)
            return;

        roomAssemblerGenerator.autoGenerateOnStart = false;
        Log($"Disabled auto generation on '{roomAssemblerGenerator.name}'. Use PF_LevelLoader/LevelProfileLoader generation instead.");
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
