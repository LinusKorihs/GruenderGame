using System;
using System.Collections;
using PCG.RoomAssembler.Data;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class LevelStartRunFlowController : MonoBehaviour
{
    public const int MaxSelectableMinions = RunSetupData.DefaultMaxTotal;
    public const int MaxSelectableSupportMinions = RunSetupData.MaxSupportTotal;
    private const string DefaultRunSceneName = "2. Linus Run";
    private const string DefaultLobbyStaticLayoutResourcesPath = "LevelFlow/SO_StaticLayout_Tutorial";
    private const float ExitTestDoorApproachDistance = 1.2f;
    private const float ExitTestNavMeshSampleRadius = 0.9f;
#if UNITY_EDITOR
    private const string LevelSystemPrefabPath = "Assets/Prefabs/Systems/Levels/PF_LevelSystem.prefab";
#endif

    public static LevelStartRunFlowController Instance { get; private set; }
    public static event Action<bool> LevelTransitionStateChanged;

    [Header("Scene Flow")]
    [SerializeField] private bool loadDedicatedRunSceneBeforeGeneration = true;
    [SerializeField] private string runSceneName = DefaultRunSceneName;

    private RoomAssemblerGenerator assembler;
    private LevelContentSpawner contentSpawner;
    private PlayerMinionCommander commander;
    private GameObject player;
    private GameObject runAssemblerRoot;

    private GameObject startButtonObject;
    private GameObject exitObject;
    private RunMinionSelectionUI selectionUI;
    private Canvas selectionCanvas;
    private TMP_Text totalText;
    private Button startRunButton;
    private SelectionRow meleeRow;
    private SelectionRow rangedRow;
    private SelectionRow supportRow;

    private bool runStarted;
    private bool transitioningLevel;
    private bool pendingGenerationAfterRunSceneLoad;
    private bool selectionControlLockActive;
    private bool lobbyPreparationRunning;
    private string pendingRunSceneName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoaded()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapActiveScene()
    {
        TryBootstrap(SceneManager.GetActiveScene());
    }

    public static bool ShouldDeferAutoGeneration(Scene scene)
    {
        return IsLevelStartSceneName(scene.name);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureForScene(scene);
    }

    private static void TryBootstrap(Scene scene)
    {
        EnsureForScene(scene);
    }

    public static LevelStartRunFlowController EnsureForScene(Scene scene)
    {
        if (!IsLevelStartSceneName(scene.name)) return null;

        LevelStartRunFlowController existing = FindFirstObjectByType<LevelStartRunFlowController>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        TryCreateLevelSystemForStartScene(scene);

        existing = FindFirstObjectByType<LevelStartRunFlowController>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        GameObject fallback = new GameObject(nameof(LevelStartRunFlowController));
        if (scene.IsValid() && scene.isLoaded)
        {
            SceneManager.MoveGameObjectToScene(fallback, scene);
        }

        Debug.LogWarning(
            $"[RunFlow] Start scene '{scene.name}' has no LevelStartRunFlowController. " +
            "Created a runtime fallback. Add PF_LevelSystem to the scene if this should be authored permanently.",
            fallback);
        return fallback.AddComponent<LevelStartRunFlowController>();
    }

    private static void TryCreateLevelSystemForStartScene(Scene scene)
    {
        if (!Application.isPlaying)
            return;

        if (LevelSystemController.Instance != null ||
            FindFirstObjectByType<LevelSystemController>(FindObjectsInactive.Include) != null ||
            LevelFlowController.Instance != null ||
            FindFirstObjectByType<LevelFlowController>(FindObjectsInactive.Include) != null)
        {
            return;
        }

#if UNITY_EDITOR
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelSystemPrefabPath);
        if (prefab == null)
            return;

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
        {
            instance = UnityEngine.Object.Instantiate(prefab);
        }

        if (instance == null)
            return;

        instance.name = "PF_LevelSystem";
        Debug.Log($"[RunFlow] Created PF_LevelSystem fallback for Start scene '{scene.name}'.", instance);
#endif
    }

    private static bool IsLevelStartSceneName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return false;

        string normalized = sceneName
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(".", string.Empty);

        return string.Equals(normalized, "LevelStart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Start", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "WerkschauStart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "LinusStart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1LinusStart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "LinusTutorial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1LinusTutorial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Tutorial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1Tutorial", StringComparison.OrdinalIgnoreCase);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(transform.root != null ? transform.root.gameObject : gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        LevelFlowController flow = LevelFlowController.EnsureInstance();
        flow.RegisterRuntimeObject(gameObject);

        Scene activeScene = SceneManager.GetActiveScene();
        if (IsLevelStartSceneName(activeScene.name))
        {
            StartCoroutine(PrepareLobbyWhenSceneIsReady(activeScene));
        }
    }

    private void OnDestroy()
    {
        SetSelectionControlsLocked(false);

        if (Instance == this)
        {
            Instance = null;
        }

        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    public void OpenSelectionUI()
    {
        EnsureSelectionUI();
        if (selectionUI == null || selectionCanvas == null || totalText == null || startRunButton == null ||
            meleeRow == null || rangedRow == null || supportRow == null)
        {
            Debug.LogWarning(
                "[RunFlow] Cannot open minion selection because no valid RunMinionSelectionUI exists in the Start scene.",
                this);
            return;
        }

        LoadRowsFromRunSetup();
        UpdateSelectionUI();

        selectionUI.SetVisible(true);
        SetSelectionControlsLocked(true);
        Time.timeScale = 0f;
    }

    public void StartSelectedRun(int melee, int ranged, int support)
    {
        RunSetupData data = RunSetupData.EnsureInstance();
        data.levelIndex = 1;
        data.SetMinionCounts(melee, ranged, support, MaxSelectableMinions);
        LevelFlowController.EnsureInstance().BeginRun();

        runStarted = true;
        SetSelectionControlsLocked(false);
        Time.timeScale = 1f;

        if (selectionUI != null)
        {
            selectionUI.SetVisible(false);
        }

        if (startButtonObject != null)
        {
            startButtonObject.SetActive(false);
            startButtonObject = null;
        }

        if (loadDedicatedRunSceneBeforeGeneration && ShouldDeferAutoGeneration(SceneManager.GetActiveScene()))
        {
            LoadRunSceneThenGenerate();
        }
        else
        {
            GenerateCurrentLevel();
        }
    }

    private void StartRunFromSelectionUI()
    {
        if (meleeRow == null || rangedRow == null || supportRow == null)
            return;

        StartSelectedRun(meleeRow.Value, rangedRow.Value, supportRow.Value);
    }

    private void SetSelectionControlsLocked(bool locked)
    {
        if (locked == selectionControlLockActive)
            return;

        selectionControlLockActive = locked;

        if (locked)
            PlayerControlLock.PushLock(this);
        else
            PlayerControlLock.PopLock(this);
    }

    public void AdvanceToNextLevel()
    {
        if (!runStarted || transitioningLevel) return;

        StartCoroutine(AdvanceToNextLevelRoutine());
    }

    private IEnumerator AdvanceToNextLevelRoutine()
    {
        SetTransitioningLevel(true);
        DisableExitInteraction();

        // OnTriggerEnter is still inside Unity's physics callback. Waiting one frame
        // keeps the room clear/regenerate path out of that callback.
        yield return null;

        UpdateRunSetupMinionCountsFromLiveParty("level exit");

        LevelFlowAdvanceAction flowAction = LevelFlowController.Instance != null
            ? LevelFlowController.Instance.AdvanceAfterCurrentLevelExit()
            : LevelFlowAdvanceAction.NotHandled;

        if (flowAction == LevelFlowAdvanceAction.LoadingScene ||
            flowAction == LevelFlowAdvanceAction.Complete ||
            flowAction == LevelFlowAdvanceAction.Blocked)
        {
            SetTransitioningLevel(false);
            yield break;
        }

        if (flowAction == LevelFlowAdvanceAction.NotHandled)
        {
            RunSetupData data = RunSetupData.EnsureInstance();
            data.levelIndex = Mathf.Max(1, data.levelIndex + 1);
        }
        else if (flowAction == LevelFlowAdvanceAction.GeneratePCGLevel &&
                 LevelFlowController.Instance != null &&
                 LevelFlowController.Instance.PrepareCurrentStepSceneForGeneration(out string flowSceneName, out bool sceneLoadPending))
        {
            if (sceneLoadPending)
            {
                pendingRunSceneName = flowSceneName;
                pendingGenerationAfterRunSceneLoad = true;
                yield break;
            }

            CompleteRunSceneLoad(SceneManager.GetActiveScene());
            yield return LevelFlowController.Instance.UnloadPreviousRuntimeSceneBeforeGeneration();
        }

        GenerateCurrentLevel();
        SetTransitioningLevel(false);
    }

    private void ResolveSceneReferences()
    {
        assembler = FindFirstObjectByType<RoomAssemblerGenerator>();
        contentSpawner = assembler != null && assembler.contentSpawner != null
            ? assembler.contentSpawner
            : FindFirstObjectByType<LevelContentSpawner>();

        commander = FindFirstObjectByType<PlayerMinionCommander>();
        player = commander != null ? PlayerRootResolver.FromCommander(commander) : FindTaggedPlayerRoot();
        EnsurePlayerKelpVisual();
    }

    private void LoadRunSceneThenGenerate()
    {
        ResolveSceneReferences();

        if (player != null)
        {
            player = PlayerRootResolver.FromGameObject(player);
            player.transform.SetParent(null, true);
            DontDestroyOnLoad(player);
        }

        PrepareRunAssemblerForSceneLoad();

        LevelFlowController flow = LevelFlowController.Instance;
        if (flow != null &&
            flow.PrepareCurrentStepSceneForGeneration(out string flowSceneName, out bool sceneLoadPending))
        {
            if (sceneLoadPending)
            {
                pendingRunSceneName = flowSceneName;
                pendingGenerationAfterRunSceneLoad = true;
                return;
            }

            StartCoroutine(CompleteRunSceneLoadThenGenerate(SceneManager.GetActiveScene()));
            return;
        }

        pendingRunSceneName = ResolveTargetRunSceneName();
        pendingGenerationAfterRunSceneLoad = true;
        SceneManager.LoadScene(pendingRunSceneName, LoadSceneMode.Single);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsLevelStartSceneName(scene.name))
        {
            StartCoroutine(PrepareLobbyWhenSceneIsReady(scene));
        }

        if (!pendingGenerationAfterRunSceneLoad) return;
        if (!string.Equals(scene.name, pendingRunSceneName, StringComparison.OrdinalIgnoreCase)) return;

        pendingGenerationAfterRunSceneLoad = false;
        pendingRunSceneName = null;

        StartCoroutine(CompleteRunSceneLoadThenGenerate(scene));
    }

    private IEnumerator CompleteRunSceneLoadThenGenerate(Scene scene)
    {
        CompleteRunSceneLoad(scene);

        if (LevelFlowController.Instance != null)
        {
            yield return LevelFlowController.Instance.UnloadPreviousRuntimeSceneBeforeGeneration();
        }

        GenerateCurrentLevel();
        SetTransitioningLevel(false);
    }

    private void SetTransitioningLevel(bool value)
    {
        if (transitioningLevel == value)
            return;

        transitioningLevel = value;
        LevelTransitionStateChanged?.Invoke(value);
    }

    private void CompleteRunSceneLoad(Scene scene)
    {
        if (player != null)
        {
            MoveRootToScene(player, scene);
        }

        if (runAssemblerRoot != null)
        {
            LevelFlowController flow = LevelFlowController.Instance;
            if (!IsPersistentLevelSystemObject(flow, runAssemblerRoot))
            {
                MoveRootToScene(runAssemblerRoot, scene);
            }
        }
        else if (assembler != null)
        {
            if (!IsPersistentLevelSystemObject(LevelFlowController.Instance, assembler.gameObject))
            {
                MoveRootToScene(assembler.gameObject, scene);
            }
        }

        DisableSceneCamerasNotOwnedByPlayer(scene);
    }

    private static bool IsPersistentLevelSystemObject(LevelFlowController flow, GameObject target)
    {
        if (flow == null || target == null)
            return false;

        GameObject runtimeRoot = flow.RuntimeRoot;
        if (runtimeRoot == null)
            return false;

        return target == runtimeRoot || target.transform.IsChildOf(runtimeRoot.transform);
    }

    private static void MoveRootToScene(GameObject target, Scene scene)
    {
        if (target == null || !scene.IsValid() || !scene.isLoaded || target.scene == scene)
            return;

        if (target.transform.parent != null)
        {
            target.transform.SetParent(null, true);
        }

        SceneManager.MoveGameObjectToScene(target, scene);
    }

    private string ResolveTargetRunSceneName()
    {
        LevelFlowStep step = LevelFlowController.Instance != null ? LevelFlowController.Instance.CurrentStep : null;
        if (step != null && step.stepType == LevelFlowStepType.PCG && !string.IsNullOrWhiteSpace(step.sceneName))
        {
            return step.sceneName;
        }

        return string.IsNullOrWhiteSpace(runSceneName) ? DefaultRunSceneName : runSceneName;
    }

    private void PrepareRunAssemblerForSceneLoad()
    {
        if (assembler == null)
        {
            runAssemblerRoot = null;
            return;
        }

        LevelProfileLoader loader = assembler.GetComponentInParent<LevelProfileLoader>();
        if (loader != null)
        {
            loader.ClearGeneratedLevelContent();
            runAssemblerRoot = loader.gameObject;
            runAssemblerRoot.SetActive(true);
            KeepRootAcrossSceneLoad(runAssemblerRoot);
            return;
        }

        assembler.autoGenerateOnStart = false;
        contentSpawner = assembler.contentSpawner != null
            ? assembler.contentSpawner
            : assembler.GetComponentInChildren<LevelContentSpawner>(true);

        if (contentSpawner != null)
        {
            contentSpawner.ClearSpawnedObjects();
        }

        assembler.transform.SetParent(null, true);
        runAssemblerRoot = assembler.gameObject;
        runAssemblerRoot.SetActive(true);
        KeepRootAcrossSceneLoad(runAssemblerRoot);
    }

    private static void KeepRootAcrossSceneLoad(GameObject target)
    {
        if (target == null || target.transform.parent != null)
            return;

        DontDestroyOnLoad(target);
    }

    private void PrepareLobby()
    {
        RunSetupData data = RunSetupData.EnsureInstance();
        data.ResetRun();
        LevelFlowController.Instance?.RegisterRuntimeObject(data.gameObject);

        ResolveSceneReferences();
        EnsureLobbyPlayer();
        ResolveSceneReferences();

        if (contentSpawner != null && contentSpawner.Config != null && player != null)
        {
            contentSpawner.SpawnMinionPartyNearPlayer(
                player,
                data.typeA,
                data.typeB,
                data.typeC,
                Environment.TickCount);
        }

        BindSceneStartButton();
    }

    private void EnsureLobbyPlayer()
    {
        if (player != null)
            return;

        StaticLevelLayoutProfile lobbyProfile = Resources.Load<StaticLevelLayoutProfile>(DefaultLobbyStaticLayoutResourcesPath);
        GameObject playerPrefab = lobbyProfile != null ? lobbyProfile.playerPrefab : null;
        if (playerPrefab == null)
        {
            Debug.LogWarning("[RunFlow] Cannot prepare Start scene because no lobby player prefab was found.", this);
            return;
        }

        ResolveLobbyPlayerSpawn(lobbyProfile, out Vector3 position, out Quaternion rotation);

        GameObject playerInstance = Instantiate(playerPrefab, position, rotation);
        playerInstance.name = "Player";

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isLoaded && playerInstance.scene != activeScene)
        {
            SceneManager.MoveGameObjectToScene(playerInstance, activeScene);
        }

        player = PlayerRootResolver.FromGameObject(playerInstance);
        commander = player != null ? player.GetComponentInChildren<PlayerMinionCommander>() : null;
        EnsurePlayerKelpVisual();
    }

    private static void ResolveLobbyPlayerSpawn(StaticLevelLayoutProfile lobbyProfile, out Vector3 position, out Quaternion rotation)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        PCGSpawnPoint[] spawnPoints = FindObjectsByType<PCGSpawnPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            PCGSpawnPoint spawnPoint = spawnPoints[i];
            if (spawnPoint == null ||
                spawnPoint.kind != PCGSpawnPointKind.Player ||
                spawnPoint.gameObject.scene != activeScene)
            {
                continue;
            }

            position = spawnPoint.transform.position;
            rotation = spawnPoint.transform.rotation;
            return;
        }

        position = lobbyProfile != null ? lobbyProfile.playerFallbackLocalPosition : Vector3.up;
        rotation = Quaternion.identity;
    }

    private void GenerateCurrentLevel()
    {
        ResolveSceneReferences();

        if (assembler == null)
        {
            LevelFlowController flow = LevelFlowController.EnsureInstance();
            if (flow.TryGetPCGTargets(out RoomAssemblerGenerator flowAssembler, out LevelContentSpawner flowSpawner))
            {
                assembler = flowAssembler;
                contentSpawner = flowSpawner;
            }

            if (assembler == null)
            {
                Debug.LogWarning("[RunFlow] Cannot generate level because no RoomAssemblerGenerator was found.", this);
                return;
            }
        }

        RunSetupData data = RunSetupData.EnsureInstance();
        bool flowApplied = LevelFlowController.Instance != null
            && LevelFlowController.Instance.PrepareCurrentPCGLevel(assembler, contentSpawner);

        if (!flowApplied && contentSpawner != null)
        {
            contentSpawner.LevelIndex = Mathf.Max(1, data.levelIndex);
        }

        if (commander != null)
        {
            commander.ClearRegisteredMinions();
        }

        ClearExitObject();

        var metrics = assembler.GenerateWithMetrics(runIndex: Mathf.Max(0, data.levelIndex - 1));
        if (metrics == null || !metrics.success)
        {
            Debug.LogWarning($"[RunFlow] PCG generation for level {data.levelIndex} did not report success.", this);
            return;
        }

        RetryContentSpawningIfEmpty(data);
        ResolveSceneReferences();
        EnsureSelectedMinionPartyNearPlayer(data);
        ResolveSceneReferences();
        PlaceExitInEndRoom();
        LevelFlowController.Instance?.UnloadPreviousRuntimeSceneIfReady();
    }

    [ContextMenu("Move Player Near Generated Exit")]
    public void MovePlayerNearGeneratedExitForTesting()
    {
        ResolveSceneReferences();

        if (player == null)
        {
            Debug.LogWarning("[RunFlow] Cannot move player near exit because no player was found.", this);
            return;
        }

        ClearExitObject();
        PlaceExitInEndRoom();

        if (exitObject == null)
        {
            Debug.LogWarning("[RunFlow] Cannot move player near exit because no exit exists.", this);
            return;
        }

        PlacedRoom endRoom = FindEndRoom();
        Vector3 targetPosition = endRoom != null
            ? GetConnectedRoomApproachPosition(endRoom, exitObject.transform.position)
            : exitObject.transform.position + Vector3.back + Vector3.up * 0.25f;

        Quaternion targetRotation = player.transform.rotation;
        Transform body = PlayerRootResolver.BodyTransform(player);
        if (body != null)
        {
            Vector3 lookDirection = exitObject.transform.position - targetPosition;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.001f)
                targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }

        MovePlayerBodyTo(player, targetPosition, targetRotation);
        MoveActiveMinionsNearPlayer(player, registerWithCommander: true);
        Debug.Log("[RunFlow] Moved player and minions near generated exit for flow testing.", this);
    }

    public void EnsureSelectedMinionsForCurrentScene()
    {
        RunSetupData data = RunSetupData.Instance;
        if (data == null || data.TotalMinions <= 0)
            return;

        ResolveSceneReferences();
        EnsureSelectedMinionPartyNearPlayer(data);
    }

    private void RetryContentSpawningIfEmpty(RunSetupData data)
    {
        if (assembler == null || contentSpawner == null || contentSpawner.Config == null)
            return;

        if (CountActiveSpawnedObjects(contentSpawner) > 0)
            return;

        if (assembler.LastPlacedRooms == null || assembler.LastPlacedRooms.Count == 0)
            return;

        Debug.LogWarning(
            $"[RunFlow] Level {Mathf.Max(1, data.levelIndex)} generated with empty Content. Retrying content spawn once.",
            this);

        contentSpawner.SpawnForGeneratedRooms(assembler.LastPlacedRooms, assembler.LastRunSeed);

        if (CountActiveSpawnedObjects(contentSpawner) == 0)
        {
            Debug.LogWarning(
                $"[RunFlow] Level {Mathf.Max(1, data.levelIndex)} Content is still empty after retry. " +
                "Check spawnpoints, budgets, allowedContentIds, and the active SpawnTuningProfile.",
                this);
        }
    }

    private IEnumerator PrepareLobbyWhenSceneIsReady(Scene scene)
    {
        if (lobbyPreparationRunning)
            yield break;

        lobbyPreparationRunning = true;

        yield return null;

        if (!scene.IsValid() || !scene.isLoaded || !IsLevelStartSceneName(scene.name))
        {
            lobbyPreparationRunning = false;
            yield break;
        }

        LevelFlowController flow = LevelFlowController.EnsureInstance();
        flow.PrepareStaticStepForScene(scene);
        flow.RegisterRuntimeObject(gameObject);

        PrepareLobby();
        RefreshLobbyFogOfWar(flow, scene);
        lobbyPreparationRunning = false;
    }

    private void RefreshLobbyFogOfWar(LevelFlowController flow, Scene scene)
    {
        LevelFogOfWarController fogOfWar = ResolveFogOfWarController();

        if (fogOfWar != null && player != null)
        {
            Transform revealTarget = PlayerRootResolver.BodyTransform(player);
            fogOfWar.SetRevealTarget(revealTarget != null ? revealTarget : player.transform);
        }

        flow?.RefreshCurrentStepAtmosphereForScene(scene);
    }

    private static LevelFogOfWarController ResolveFogOfWarController()
    {
        if (LevelSystemController.Instance != null && LevelSystemController.Instance.FogOfWar != null)
            return LevelSystemController.Instance.FogOfWar;

        return FindFirstObjectByType<LevelFogOfWarController>(FindObjectsInactive.Include);
    }

    private void EnsureSelectedMinionPartyNearPlayer(RunSetupData data)
    {
        int selectedMelee = Mathf.Max(0, data.typeA);
        int selectedRanged = Mathf.Max(0, data.typeB);
        int selectedSupport = Mathf.Max(0, data.typeC);
        int selectedTotal = selectedMelee + selectedRanged + selectedSupport;
        if (selectedTotal <= 0)
            return;

        if (player == null)
        {
            Debug.LogWarning("[RunFlow] Cannot ensure selected minions because no player was found.", this);
            return;
        }

        CountActiveLiveMinions(out int activeMelee, out int activeRanged, out int activeSupport, out int activeTotal);
        int missingMelee = Mathf.Max(0, selectedMelee - activeMelee);
        int missingRanged = Mathf.Max(0, selectedRanged - activeRanged);
        int missingSupport = Mathf.Max(0, selectedSupport - activeSupport);

        if (missingMelee + missingRanged + missingSupport > 0)
        {
            if (contentSpawner == null || contentSpawner.Config == null)
            {
                Debug.LogWarning("[RunFlow] Cannot respawn missing selected minions because LevelContentSpawner config is missing.", this);
                return;
            }

            contentSpawner.SpawnMinionPartyNearPlayer(
                player,
                missingMelee,
                missingRanged,
                missingSupport,
                Environment.TickCount,
                clearExistingGeneratedContent: false);

            Debug.Log(
                $"[RunFlow] Added missing minions for level {Mathf.Max(1, data.levelIndex)} " +
                $"({missingMelee}/{missingRanged}/{missingSupport}).",
                this);
        }
        else if (activeTotal > selectedTotal)
        {
            Debug.LogWarning(
                $"[RunFlow] Found {activeTotal} live minions, but run setup selected {selectedTotal}. " +
                "Keeping existing minions instead of deleting runtime state.",
                this);
        }

        MoveActiveMinionsNearPlayer(player, registerWithCommander: true);
    }

    private void UpdateRunSetupMinionCountsFromLiveParty(string reason)
    {
        RunSetupData data = RunSetupData.Instance;
        if (data == null || data.TotalMinions <= 0)
            return;

        CountActiveLiveMinions(out int liveMelee, out int liveRanged, out int liveSupport, out int liveTotal);
        if (liveTotal >= data.TotalMinions &&
            liveMelee == data.typeA &&
            liveRanged == data.typeB &&
            liveSupport == data.typeC)
        {
            return;
        }

        int oldMelee = data.typeA;
        int oldRanged = data.typeB;
        int oldSupport = data.typeC;
        data.SetMinionCounts(liveMelee, liveRanged, liveSupport, data.maxTotal);

        Debug.Log(
            $"[RunFlow] Updated run minion survivors before {reason}: " +
            $"{oldMelee}/{oldRanged}/{oldSupport} -> {data.typeA}/{data.typeB}/{data.typeC}.",
            this);
    }

    private static int CountActiveSpawnedObjects(LevelContentSpawner spawner)
    {
        if (spawner == null || spawner.SpawnedObjects == null)
            return 0;

        int count = 0;
        for (int i = 0; i < spawner.SpawnedObjects.Count; i++)
        {
            GameObject spawnedObject = spawner.SpawnedObjects[i];
            if (spawnedObject != null && spawnedObject.activeInHierarchy)
                count++;
        }

        return count;
    }

    private static void CountActiveLiveMinions(out int melee, out int ranged, out int support, out int total)
    {
        melee = 0;
        ranged = 0;
        support = 0;
        total = 0;

        MinionCore[] minions = FindObjectsByType<MinionCore>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < minions.Length; i++)
        {
            MinionCore minion = minions[i];
            if (!IsLiveMinion(minion))
                continue;

            total++;
            switch (minion.RoleType)
            {
                case MinionRoleType.Ranged:
                    ranged++;
                    break;
                case MinionRoleType.Support:
                    support++;
                    break;
                default:
                    melee++;
                    break;
            }
        }
    }

    private void MoveActiveMinionsNearPlayer(GameObject playerRoot, bool registerWithCommander)
    {
        if (playerRoot == null)
            return;

        Transform playerBody = PlayerRootResolver.BodyTransform(playerRoot);
        if (playerBody == null)
            playerBody = playerRoot.transform;

        PlayerMinionCommander targetCommander = playerRoot.GetComponentInChildren<PlayerMinionCommander>();
        if (targetCommander == null)
            targetCommander = FindFirstObjectByType<PlayerMinionCommander>();

        if (registerWithCommander && targetCommander != null)
            targetCommander.ClearRegisteredMinions();

        MinionCore[] minions = FindObjectsByType<MinionCore>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int total = 0;
        for (int i = 0; i < minions.Length; i++)
        {
            if (IsLiveMinion(minions[i]))
                total++;
        }

        int slot = 0;
        for (int i = 0; i < minions.Length; i++)
        {
            MinionCore minion = minions[i];
            if (!IsLiveMinion(minion))
                continue;

            GameObject root = ResolveGeneratedMinionRoot(minion);
            MoveRootToScene(root, playerRoot.scene);
            ParentMinionUnderCurrentContent(root);
            Vector3 position = GetFormationPositionNearPlayer(playerBody, slot, Mathf.Max(1, total));
            MoveActor(root, position, playerBody.rotation);
            minion.SetFollowTarget(playerBody);
            minion.SetRecallCommand();

            if (registerWithCommander && targetCommander != null)
                targetCommander.RegisterMinion(minion);

            slot++;
        }
    }

    private static Vector3 GetFormationPositionNearPlayer(Transform playerBody, int index, int total)
    {
        Vector3 forward = playerBody.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float angle = total <= 1 ? 0f : (index / (float)total) * Mathf.PI * 2f;
        float radius = 1.4f + Mathf.Floor(index / 8f) * 0.7f;
        Vector3 radial = right * Mathf.Cos(angle) - forward * Mathf.Sin(angle);
        Vector3 position = playerBody.position - forward * 1.5f + radial * radius;

        if (TryResolveNearbyPlayerNavMeshPosition(playerBody, position, index, total, out Vector3 navMeshPosition))
        {
            return navMeshPosition;
        }

        return position;
    }

    private static bool TryResolveNearbyPlayerNavMeshPosition(
        Transform playerBody,
        Vector3 preferredPosition,
        int index,
        int total,
        out Vector3 navMeshPosition)
    {
        navMeshPosition = preferredPosition;

        if (playerBody == null)
            return false;

        if (TryResolveWalkablePathPosition(playerBody.position, preferredPosition, 1.1f, out navMeshPosition))
            return true;

        Vector3 forward = playerBody.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float baseAngle = total <= 1 ? 0f : (index / (float)Mathf.Max(1, total)) * Mathf.PI * 2f;

        for (int ring = 0; ring < 3; ring++)
        {
            float radius = 1.2f + ring * 0.7f;
            int samples = 8 + ring * 4;

            for (int sample = 0; sample < samples; sample++)
            {
                float angle = baseAngle + (sample / (float)samples) * Mathf.PI * 2f;
                Vector3 radial = right * Mathf.Cos(angle) - forward * Mathf.Sin(angle);
                Vector3 candidate = playerBody.position + radial * radius;

                if (TryResolveWalkablePathPosition(playerBody.position, candidate, 0.85f, out navMeshPosition))
                    return true;
            }
        }

        return false;
    }

    private static bool TryResolveWalkablePathPosition(Vector3 startPosition, Vector3 requestedPosition, float sampleRadius, out Vector3 navMeshPosition)
    {
        navMeshPosition = requestedPosition;
        int areaMask = WalkableNavMeshAreaMask();
        float radius = Mathf.Max(0.05f, sampleRadius);

        if (!NavMesh.SamplePosition(requestedPosition, out NavMeshHit destinationHit, radius, areaMask))
            return false;

        Vector3 delta = destinationHit.position - requestedPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude > radius * radius)
            return false;

        if (!NavMesh.SamplePosition(startPosition, out NavMeshHit startHit, Mathf.Max(1f, radius), areaMask))
        {
            navMeshPosition = destinationHit.position;
            return true;
        }

        Vector3 flatDelta = destinationHit.position - startHit.position;
        flatDelta.y = 0f;
        if (flatDelta.sqrMagnitude <= 0.1f * 0.1f)
        {
            navMeshPosition = destinationHit.position;
            return true;
        }

        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(startHit.position, destinationHit.position, areaMask, path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        navMeshPosition = destinationHit.position;
        return true;
    }

    private static Vector3 GetConnectedRoomApproachPosition(PlacedRoom endRoom, Vector3 exitPosition)
    {
        PlacedRoom connectedRoom = FindBestConnectedRoom(endRoom, exitPosition);
        if (connectedRoom == null || connectedRoom.root == null)
            return GetExitApproachPosition(endRoom, exitPosition);

        if (!TryFindConnectionSockets(endRoom, connectedRoom, out SocketMarker endSocket, out SocketMarker connectedSocket))
        {
            endSocket = FindConnectedSocketClosestTo(endRoom, exitPosition);
            Vector3 referencePosition = endSocket != null ? endSocket.CenterWorld : exitPosition;
            connectedSocket = FindConnectedSocketClosestTo(connectedRoom, referencePosition);
        }

        Vector3 directionIntoConnectedRoom = Vector3.zero;
        Vector3 position = connectedRoom.root.transform.position + Vector3.up * 0.25f;

        if (connectedSocket != null)
        {
            directionIntoConnectedRoom = -connectedSocket.ForwardWorld;
            directionIntoConnectedRoom.y = 0f;
            if (directionIntoConnectedRoom.sqrMagnitude > 0.001f)
            {
                directionIntoConnectedRoom.Normalize();
                float[] distances = { ExitTestDoorApproachDistance, 1.65f, 2.1f };
                for (int i = 0; i < distances.Length; i++)
                {
                    Vector3 candidate = connectedSocket.CenterWorld + directionIntoConnectedRoom * distances[i] + Vector3.up * 0.25f;
                    candidate = ClampToRoomBounds(connectedRoom, candidate, 0.45f);
                    if (TryProjectToRoomNavMesh(connectedRoom, candidate, out Vector3 navMeshPosition))
                        return navMeshPosition;
                }

                position = connectedSocket.CenterWorld + directionIntoConnectedRoom * ExitTestDoorApproachDistance + Vector3.up * 0.25f;
            }
        }

        position = ClampToRoomBounds(connectedRoom, position, 0.45f);
        return TryProjectToRoomNavMesh(connectedRoom, position, out Vector3 fallbackNavMeshPosition)
            ? fallbackNavMeshPosition
            : position;
    }

    private static Vector3 GetExitApproachPosition(PlacedRoom endRoom, Vector3 exitPosition)
    {
        Vector3 directionFromEntrance = Vector3.zero;
        SocketMarker[] sockets = endRoom.root.GetComponentsInChildren<SocketMarker>(true);
        for (int i = 0; i < sockets.Length; i++)
        {
            SocketMarker socket = sockets[i];
            if (socket == null)
                continue;

            if (endRoom.connectedSocketInstanceIds.Contains(socket.GetInstanceID()))
            {
                directionFromEntrance = exitPosition - socket.CenterWorld;
                directionFromEntrance.y = 0f;
                break;
            }
        }

        if (directionFromEntrance.sqrMagnitude < 0.001f)
        {
            directionFromEntrance = endRoom.root.transform.forward;
            directionFromEntrance.y = 0f;
        }

        if (directionFromEntrance.sqrMagnitude < 0.001f)
            directionFromEntrance = Vector3.forward;

        directionFromEntrance.Normalize();
        Vector3 position = exitPosition - directionFromEntrance * 0.85f + Vector3.up * 0.25f;
        position = ClampToRoomBounds(endRoom, position, 0.75f);
        return TryProjectToRoomNavMesh(endRoom, position, out Vector3 navMeshPosition)
            ? navMeshPosition
            : position;
    }

    private static bool TryFindConnectionSockets(PlacedRoom firstRoom, PlacedRoom secondRoom, out SocketMarker firstSocket, out SocketMarker secondSocket)
    {
        firstSocket = null;
        secondSocket = null;

        if (firstRoom?.root == null || secondRoom?.root == null)
            return false;

        SocketMarker[] firstSockets = firstRoom.root.GetComponentsInChildren<SocketMarker>(true);
        SocketMarker[] secondSockets = secondRoom.root.GetComponentsInChildren<SocketMarker>(true);
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < firstSockets.Length; i++)
        {
            SocketMarker candidateFirst = firstSockets[i];
            if (candidateFirst == null || !firstRoom.connectedSocketInstanceIds.Contains(candidateFirst.GetInstanceID()))
                continue;

            Vector3 firstForward = Flatten(candidateFirst.ForwardWorld);
            if (firstForward.sqrMagnitude > 0.001f)
                firstForward.Normalize();

            for (int s = 0; s < secondSockets.Length; s++)
            {
                SocketMarker candidateSecond = secondSockets[s];
                if (candidateSecond == null || !secondRoom.connectedSocketInstanceIds.Contains(candidateSecond.GetInstanceID()))
                    continue;

                Vector3 secondForward = Flatten(candidateSecond.ForwardWorld);
                if (secondForward.sqrMagnitude > 0.001f)
                    secondForward.Normalize();

                float centerDistance = Vector3.Distance(candidateFirst.CenterWorld, candidateSecond.CenterWorld);
                float directionPenalty = firstForward.sqrMagnitude > 0.001f && secondForward.sqrMagnitude > 0.001f
                    ? Mathf.Abs(Vector3.Dot(firstForward, secondForward) + 1f)
                    : 0f;
                float score = centerDistance + directionPenalty * 0.5f;

                if (score < bestScore)
                {
                    bestScore = score;
                    firstSocket = candidateFirst;
                    secondSocket = candidateSecond;
                }
            }
        }

        return firstSocket != null && secondSocket != null;
    }

    private static PlacedRoom FindBestConnectedRoom(PlacedRoom endRoom, Vector3 exitPosition)
    {
        if (endRoom == null || endRoom.connectedRooms == null)
            return null;

        PlacedRoom bestRoom = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < endRoom.connectedRooms.Count; i++)
        {
            PlacedRoom candidate = endRoom.connectedRooms[i];
            if (candidate == null || candidate.root == null || candidate.isCap)
                continue;

            float distance = (GetRoomCenter(candidate.root) - exitPosition).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestRoom = candidate;
            }
        }

        return bestRoom;
    }

    private static SocketMarker FindConnectedSocketClosestTo(PlacedRoom room, Vector3 worldPosition)
    {
        if (room?.root == null)
            return null;

        SocketMarker bestSocket = null;
        float bestDistance = float.PositiveInfinity;
        SocketMarker[] sockets = room.root.GetComponentsInChildren<SocketMarker>(true);

        for (int i = 0; i < sockets.Length; i++)
        {
            SocketMarker socket = sockets[i];
            if (socket == null || !room.connectedSocketInstanceIds.Contains(socket.GetInstanceID()))
                continue;

            float distance = (socket.CenterWorld - worldPosition).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSocket = socket;
            }
        }

        return bestSocket;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static Vector3 ClampToRoomBounds(PlacedRoom room, Vector3 position, float margin)
    {
        if (room?.root == null)
            return position;

        Transform boundsTransform = room.root.transform.Find("Bounds");
        if (boundsTransform == null || !boundsTransform.TryGetComponent(out BoxCollider boundsCollider))
            return position;

        Vector3 local = boundsTransform.InverseTransformPoint(position);
        Vector3 halfSize = boundsCollider.size * 0.5f;
        float safeMargin = Mathf.Max(0f, margin);
        local.x = Mathf.Clamp(local.x, boundsCollider.center.x - halfSize.x + safeMargin, boundsCollider.center.x + halfSize.x - safeMargin);
        local.z = Mathf.Clamp(local.z, boundsCollider.center.z - halfSize.z + safeMargin, boundsCollider.center.z + halfSize.z - safeMargin);
        return boundsTransform.TransformPoint(local);
    }

    private static bool TryProjectToRoomNavMesh(PlacedRoom room, Vector3 position, out Vector3 navMeshPosition)
    {
        navMeshPosition = position;
        if (!NavMesh.SamplePosition(position, out NavMeshHit hit, ExitTestNavMeshSampleRadius, WalkableNavMeshAreaMask()))
            return false;

        Vector3 sampled = hit.position + Vector3.up * 0.25f;
        if (!IsInsideRoomBounds(room, sampled, 0.1f))
            return false;

        navMeshPosition = sampled;
        return true;
    }

    private static int WalkableNavMeshAreaMask()
    {
        int notWalkable = NavMesh.GetAreaFromName("Not Walkable");
        return notWalkable >= 0 ? NavMesh.AllAreas & ~(1 << notWalkable) : NavMesh.AllAreas;
    }

    private static bool IsInsideRoomBounds(PlacedRoom room, Vector3 position, float margin)
    {
        if (room?.root == null)
            return true;

        Transform boundsTransform = room.root.transform.Find("Bounds");
        if (boundsTransform == null || !boundsTransform.TryGetComponent(out BoxCollider boundsCollider))
            return true;

        Vector3 local = boundsTransform.InverseTransformPoint(position);
        Vector3 halfSize = boundsCollider.size * 0.5f;
        float safeMargin = Mathf.Max(0f, margin);

        return local.x >= boundsCollider.center.x - halfSize.x + safeMargin
            && local.x <= boundsCollider.center.x + halfSize.x - safeMargin
            && local.z >= boundsCollider.center.z - halfSize.z + safeMargin
            && local.z <= boundsCollider.center.z + halfSize.z - safeMargin;
    }

    private static void MoveActor(GameObject actor, Vector3 position, Quaternion rotation)
    {
        if (actor == null)
            return;

        CharacterController[] controllers = actor.GetComponentsInChildren<CharacterController>();
        bool[] controllerStates = new bool[controllers.Length];
        for (int i = 0; i < controllers.Length; i++)
        {
            controllerStates[i] = controllers[i] != null && controllers[i].enabled;
            if (controllers[i] != null) controllers[i].enabled = false;
        }

        NavMeshAgent[] agents = actor.GetComponentsInChildren<NavMeshAgent>();
        bool[] agentStates = new bool[agents.Length];
        for (int i = 0; i < agents.Length; i++)
        {
            agentStates[i] = agents[i] != null && agents[i].enabled;
            if (agents[i] != null) agents[i].enabled = false;
        }

        actor.transform.SetPositionAndRotation(position, rotation);

        Rigidbody[] rigidbodies = actor.GetComponentsInChildren<Rigidbody>();
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rb = rigidbodies[i];
            if (rb == null || rb.isKinematic) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Physics.SyncTransforms();

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] != null) agents[i].enabled = agentStates[i];
        }

        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null) controllers[i].enabled = controllerStates[i];
        }
    }

    private static void MovePlayerBodyTo(GameObject playerRoot, Vector3 bodyPosition, Quaternion rotation)
    {
        if (playerRoot == null)
            return;

        Transform body = PlayerRootResolver.BodyTransform(playerRoot);
        if (body == null || body == playerRoot.transform)
        {
            MoveActor(playerRoot, bodyPosition, rotation);
            return;
        }

        Vector3 rootPosition = playerRoot.transform.position + (bodyPosition - body.position);
        MoveActor(playerRoot, rootPosition, playerRoot.transform.rotation);
        body.rotation = rotation;
        Physics.SyncTransforms();
    }

    private static GameObject ResolveGeneratedMinionRoot(MinionCore minion)
    {
        if (minion == null)
            return null;

        PCGGeneratedContentMarker marker = minion.GetComponentInParent<PCGGeneratedContentMarker>();
        return marker != null ? marker.gameObject : minion.gameObject;
    }

    private void ParentMinionUnderCurrentContent(GameObject minionRoot)
    {
        if (minionRoot == null || contentSpawner == null)
            return;

        Transform minionsRoot = contentSpawner.GetOrCreateMinionsContentRoot();
        if (minionsRoot == null)
            return;

        Transform minionTransform = minionRoot.transform;
        if (minionTransform == minionsRoot || minionTransform.IsChildOf(minionsRoot))
            return;

        minionTransform.SetParent(minionsRoot, true);
    }

    private static bool IsLiveMinion(MinionCore minion)
    {
        if (minion == null || !minion.gameObject.activeInHierarchy)
            return false;

        CombatantStats stats = minion.GetComponentInParent<CombatantStats>();
        return stats == null || !stats.IsDead;
    }

    private void BindSceneStartButton()
    {
        RunStartTrigger trigger = FindSceneStartTrigger();
        if (trigger == null)
        {
            Debug.LogWarning(
                "[RunFlow] Start scene has no RunStartTrigger. Place PF_RunStartButton in the Start scene.",
                this);
            return;
        }

        startButtonObject = trigger.gameObject;
        startButtonObject.SetActive(true);

        Collider[] colliders = startButtonObject.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0)
        {
            Debug.LogWarning(
                "[RunFlow] PF_RunStartButton has no Collider. The player cannot trigger the minion selection.",
                startButtonObject);
        }
    }

    private void PlaceExitInEndRoom()
    {
        ClearExitObject();

        PlacedRoom endRoom = FindEndRoom();
        if (endRoom == null || endRoom.root == null)
        {
            Debug.LogWarning("[RunFlow] Cannot place next-level exit because no generated end room was found.", this);
            return;
        }

        RunLevelExitTrigger trigger = FindEndRoomExitTrigger(endRoom.root);
        if (trigger == null)
        {
            Debug.LogWarning(
                "[RunFlow] Generated end room has no RunLevelExitTrigger. Add PF_LevelExitTrigger to the end-room prefab.",
                endRoom.root);
            return;
        }

        exitObject = trigger.gameObject;
        exitObject.SetActive(true);

        Collider[] colliders = exitObject.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0)
        {
            Debug.LogWarning(
                "[RunFlow] PF_LevelExitTrigger has no Collider. The player cannot trigger the level transition.",
                exitObject);
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = true;
            colliders[i].isTrigger = true;
        }

        trigger.Initialize(this);
    }

    private static RunStartTrigger FindSceneStartTrigger()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        RunStartTrigger[] triggers = FindObjectsByType<RunStartTrigger>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        RunStartTrigger fallback = null;
        for (int i = 0; i < triggers.Length; i++)
        {
            RunStartTrigger trigger = triggers[i];
            if (trigger == null)
                continue;

            if (fallback == null)
                fallback = trigger;

            if (trigger.gameObject.scene == activeScene)
                return trigger;
        }

        return fallback;
    }

    private static RunLevelExitTrigger FindEndRoomExitTrigger(GameObject endRoomRoot)
    {
        if (endRoomRoot == null)
            return null;

        RunLevelExitTrigger[] triggers = endRoomRoot.GetComponentsInChildren<RunLevelExitTrigger>(true);
        return triggers.Length > 0 ? triggers[0] : null;
    }

    private PlacedRoom FindEndRoom()
    {
        if (assembler == null || assembler.LastPlacedRooms == null) return null;

        foreach (PlacedRoom room in assembler.LastPlacedRooms)
        {
            if (room?.root == null || room.isCap) continue;
            if (room.def == assembler.config.endRoom) return room;
            if (room.def != null && room.def.isEnd) return room;
            if (room.root.name.IndexOf("END_", StringComparison.OrdinalIgnoreCase) >= 0) return room;
        }

        return null;
    }

    private void DisableSceneCamerasNotOwnedByPlayer(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null || root == player || root == runAssemblerRoot || (assembler != null && root == assembler.gameObject)) continue;

            Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
            for (int c = 0; c < cameras.Length; c++)
            {
                cameras[c].enabled = false;
            }

            AudioListener[] listeners = root.GetComponentsInChildren<AudioListener>(true);
            for (int l = 0; l < listeners.Length; l++)
            {
                listeners[l].enabled = false;
            }
        }
    }

    private static Vector3 GetRoomCenter(GameObject roomRoot)
    {
        if (roomRoot == null)
            return Vector3.zero;

        Transform boundsTransform = roomRoot.transform.Find("Bounds");
        if (boundsTransform != null && boundsTransform.TryGetComponent(out BoxCollider boundsCollider))
        {
            return boundsCollider.bounds.center;
        }

        Renderer[] renderers = roomRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.center;
        }

        return roomRoot.transform.position;
    }

    private void ClearExitObject()
    {
        if (exitObject != null)
        {
            Collider[] colliders = exitObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        exitObject = null;
    }

    private void DisableExitInteraction()
    {
        if (exitObject == null) return;

        Collider[] colliders = exitObject.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }

    private void EnsurePlayerKelpVisual()
    {
        if (player == null) return;

        PlayerKelpVisualInstaller installer = player.GetComponentInChildren<PlayerKelpVisualInstaller>(true);
        if (installer == null)
        {
            installer = player.AddComponent<PlayerKelpVisualInstaller>();
        }

        installer.InstallIfNeeded();
    }

    private void EnsureSelectionUI()
    {
        if (selectionCanvas != null) return;

        EnsureEventSystem();

        RunMinionSelectionUI ui = FindSceneSelectionUI();
        if (ui == null)
        {
            Debug.LogWarning(
                "[RunFlow] Start scene has no RunMinionSelectionUI. Place PF_RunMinionSelectionUI in the Start scene.",
                this);
            return;
        }

        if (!TryBindSelectionUI(ui))
        {
            Debug.LogWarning(
                "[RunFlow] PF_RunMinionSelectionUI is missing one or more bindings. Check Canvas, Total, StartButton, and row buttons.",
                ui);
            return;
        }

        selectionUI.SetVisible(false);
    }

    private bool TryBindSelectionUI(RunMinionSelectionUI ui)
    {
        if (ui == null)
            return false;

        ui.ResolveReferences();
        if (!ui.IsValid)
            return false;

        selectionUI = ui;
        selectionCanvas = ui.Canvas;
        totalText = ui.TotalText;
        startRunButton = ui.StartButton;
        meleeRow = CreateSelectionRow(ui.MeleeRow);
        rangedRow = CreateSelectionRow(ui.RangedRow);
        supportRow = CreateSelectionRow(ui.SupportRow);

        if (meleeRow == null || rangedRow == null || supportRow == null)
            return false;

        BindSelectionRowButtons(meleeRow, isSupportRow: false);
        BindSelectionRowButtons(rangedRow, isSupportRow: false);
        BindSelectionRowButtons(supportRow, isSupportRow: true);

        startRunButton.onClick.RemoveListener(StartRunFromSelectionUI);
        startRunButton.onClick.AddListener(StartRunFromSelectionUI);

        return true;
    }

    private static RunMinionSelectionUI FindSceneSelectionUI()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        RunMinionSelectionUI[] candidates = FindObjectsByType<RunMinionSelectionUI>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        RunMinionSelectionUI fallback = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            RunMinionSelectionUI candidate = candidates[i];
            if (candidate == null)
                continue;

            if (fallback == null)
                fallback = candidate;

            if (candidate.gameObject.scene == activeScene)
                return candidate;
        }

        return fallback;
    }

    private static SelectionRow CreateSelectionRow(RunMinionSelectionUI.RowBinding binding)
    {
        if (binding == null || !binding.IsValid)
            return null;

        return new SelectionRow(binding.ValueText, binding.MinusButton, binding.PlusButton);
    }

    private void BindSelectionRowButtons(SelectionRow row, bool isSupportRow)
    {
        row.BindButtons(
            () =>
            {
                row.SetValue(row.Value - 1);
                UpdateSelectionUI();
            },
            () =>
            {
                if (GetSelectedTotal() >= MaxSelectableMinions) return;
                if (isSupportRow && row.Value >= MaxSelectableSupportMinions) return;
                row.SetValue(row.Value + 1);
                UpdateSelectionUI();
            });
    }

    private void LoadRowsFromRunSetup()
    {
        RunSetupData data = RunSetupData.EnsureInstance();
        data.SetMinionCounts(data.typeA, data.typeB, data.typeC, MaxSelectableMinions);

        meleeRow.SetValue(data.typeA);
        rangedRow.SetValue(data.typeB);
        supportRow.SetValue(data.typeC);
    }

    private void UpdateSelectionUI()
    {
        int total = GetSelectedTotal();
        totalText.text = $"{total}/{MaxSelectableMinions}  Support {supportRow.Value}/{MaxSelectableSupportMinions}";
        startRunButton.interactable = total >= 0 && total <= MaxSelectableMinions;

        bool canAdd = total < MaxSelectableMinions;
        meleeRow.SetCanAdd(canAdd);
        rangedRow.SetCanAdd(canAdd);
        supportRow.SetCanAdd(canAdd && supportRow.Value < MaxSelectableSupportMinions);
    }

    private int GetSelectedTotal()
    {
        return meleeRow.Value + rangedRow.Value + supportRow.Value;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    private static GameObject FindTaggedPlayerRoot()
    {
        try
        {
            GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                return PlayerRootResolver.FromTransform(taggedPlayer.transform);
            }
        }
        catch
        {
            // The tag is not guaranteed in stripped test scenes.
        }

        return null;
    }

    private sealed class SelectionRow
    {
        private readonly TMP_Text valueText;
        private readonly Button minusButton;
        private readonly Button plusButton;

        public int Value { get; private set; }

        public SelectionRow(TMP_Text valueText, Button minusButton, Button plusButton)
        {
            this.valueText = valueText;
            this.minusButton = minusButton;
            this.plusButton = plusButton;
        }

        public void BindButtons(Action minus, Action plus)
        {
            minusButton.onClick.AddListener(() => minus?.Invoke());
            plusButton.onClick.AddListener(() => plus?.Invoke());
        }

        public void SetValue(int value)
        {
            Value = Mathf.Max(0, value);
            valueText.text = Value.ToString();
            minusButton.interactable = Value > 0;
        }

        public void SetCanAdd(bool canAdd)
        {
            plusButton.interactable = canAdd;
        }
    }
}
