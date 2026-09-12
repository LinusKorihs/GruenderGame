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

public sealed class LevelStartRunFlowController : MonoBehaviour
{
    public const int MaxSelectableMinions = RunSetupData.DefaultMaxTotal;
    private const string DefaultRunSceneName = "2. Linus Run";

    public static LevelStartRunFlowController Instance { get; private set; }

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
    private Canvas selectionCanvas;
    private TMP_Text totalText;
    private Button startRunButton;
    private SelectionRow meleeRow;
    private SelectionRow rangedRow;
    private SelectionRow supportRow;

    private bool runStarted;
    private bool transitioningLevel;
    private bool pendingGenerationAfterRunSceneLoad;

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
        TryBootstrap(scene);
    }

    private static void TryBootstrap(Scene scene)
    {
        if (!IsLevelStartSceneName(scene.name)) return;
        if (FindFirstObjectByType<LevelStartRunFlowController>() != null) return;

        GameObject host = new GameObject(nameof(LevelStartRunFlowController));
        host.AddComponent<LevelStartRunFlowController>();
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
            || string.Equals(normalized, "LinusStart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1LinusStart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "LinusTutorial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1LinusTutorial", StringComparison.OrdinalIgnoreCase);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        LevelFlowController flow = LevelFlowController.EnsureInstance();
        flow.PrepareStaticStepForScene(SceneManager.GetActiveScene());
        flow.RegisterRuntimeObject(gameObject);
        ResolveSceneReferences();
        PrepareLobby();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    public void OpenSelectionUI()
    {
        EnsureSelectionUI();
        LoadRowsFromRunSetup();
        UpdateSelectionUI();

        selectionCanvas.gameObject.SetActive(true);
        Time.timeScale = 0f;
    }

    public void StartSelectedRun(int melee, int ranged, int support)
    {
        RunSetupData data = RunSetupData.EnsureInstance();
        data.levelIndex = 1;
        data.SetMinionCounts(melee, ranged, support, MaxSelectableMinions);
        LevelFlowController.EnsureInstance().BeginRun();

        runStarted = true;
        Time.timeScale = 1f;

        if (selectionCanvas != null)
        {
            selectionCanvas.gameObject.SetActive(false);
        }

        if (startButtonObject != null)
        {
            startButtonObject.SetActive(false);
            Destroy(startButtonObject);
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

    public void AdvanceToNextLevel()
    {
        if (!runStarted || transitioningLevel) return;

        StartCoroutine(AdvanceToNextLevelRoutine());
    }

    private IEnumerator AdvanceToNextLevelRoutine()
    {
        transitioningLevel = true;
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
            transitioningLevel = false;
            yield break;
        }

        if (flowAction == LevelFlowAdvanceAction.NotHandled)
        {
            RunSetupData data = RunSetupData.EnsureInstance();
            data.levelIndex = Mathf.Max(1, data.levelIndex + 1);
        }

        GenerateCurrentLevel();
        transitioningLevel = false;
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

        pendingGenerationAfterRunSceneLoad = true;
        SceneManager.LoadScene(ResolveTargetRunSceneName(), LoadSceneMode.Single);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!pendingGenerationAfterRunSceneLoad) return;
        if (!string.Equals(scene.name, ResolveTargetRunSceneName(), StringComparison.OrdinalIgnoreCase)) return;

        pendingGenerationAfterRunSceneLoad = false;

        if (player != null)
        {
            SceneManager.MoveGameObjectToScene(player, scene);
        }

        if (runAssemblerRoot != null)
        {
            LevelFlowController flow = LevelFlowController.Instance;
            if (flow == null || !flow.IsRuntimeLevelLoaderRoot(runAssemblerRoot))
            {
                SceneManager.MoveGameObjectToScene(runAssemblerRoot, scene);
            }
        }
        else if (assembler != null)
        {
            SceneManager.MoveGameObjectToScene(assembler.gameObject, scene);
        }

        DisableSceneCamerasNotOwnedByPlayer(scene);
        GenerateCurrentLevel();
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
            DontDestroyOnLoad(runAssemblerRoot);
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
        DontDestroyOnLoad(runAssemblerRoot);
    }

    private void PrepareLobby()
    {
        RunSetupData data = RunSetupData.EnsureInstance();
        data.ResetRun();
        LevelFlowController.Instance?.RegisterRuntimeObject(data.gameObject);

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

        CreateStartButton();
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

        MoveActor(player, targetPosition, targetRotation);
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

        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 1.1f, NavMesh.AllAreas))
        {
            Vector3 delta = hit.position - position;
            delta.y = 0f;
            if (delta.sqrMagnitude <= 1.1f * 1.1f)
                position = hit.position;
        }

        return position;
    }

    private static Vector3 GetConnectedRoomApproachPosition(PlacedRoom endRoom, Vector3 exitPosition)
    {
        PlacedRoom connectedRoom = FindBestConnectedRoom(endRoom, exitPosition);
        if (connectedRoom == null || connectedRoom.root == null)
            return GetExitApproachPosition(endRoom, exitPosition);

        SocketMarker endSocket = FindConnectedSocketClosestTo(endRoom, exitPosition);
        Vector3 referencePosition = endSocket != null ? endSocket.CenterWorld : exitPosition;
        SocketMarker connectedSocket = FindConnectedSocketClosestTo(connectedRoom, referencePosition);

        Vector3 directionIntoConnectedRoom = Vector3.zero;
        Vector3 position = connectedRoom.root.transform.position + Vector3.up * 0.25f;

        if (connectedSocket != null)
        {
            directionIntoConnectedRoom = -connectedSocket.ForwardWorld;
            directionIntoConnectedRoom.y = 0f;
            if (directionIntoConnectedRoom.sqrMagnitude > 0.001f)
            {
                directionIntoConnectedRoom.Normalize();
                position = connectedSocket.CenterWorld + directionIntoConnectedRoom * 1.2f + Vector3.up * 0.25f;
            }
        }

        return ClampToRoomBounds(connectedRoom, position, 0.75f);
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
        return ClampToRoomBounds(endRoom, position, 0.75f);
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

    private static GameObject ResolveGeneratedMinionRoot(MinionCore minion)
    {
        if (minion == null)
            return null;

        PCGGeneratedContentMarker marker = minion.GetComponentInParent<PCGGeneratedContentMarker>();
        return marker != null ? marker.gameObject : minion.gameObject;
    }

    private static bool IsLiveMinion(MinionCore minion)
    {
        if (minion == null || !minion.gameObject.activeInHierarchy)
            return false;

        CombatantStats stats = minion.GetComponentInParent<CombatantStats>();
        return stats == null || !stats.IsDead;
    }

    private void CreateStartButton()
    {
        if (startButtonObject != null || player == null) return;

        Transform playerBody = PlayerRootResolver.BodyTransform(player);
        if (playerBody == null) playerBody = player.transform;

        Vector3 forward = playerBody.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 position = playerBody.position + forward * 3f + Vector3.up * 0.12f;

        startButtonObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        startButtonObject.name = "Run Start Button";
        startButtonObject.transform.SetPositionAndRotation(position, Quaternion.identity);
        startButtonObject.transform.localScale = new Vector3(0.9f, 0.12f, 0.9f);

        Collider trigger = startButtonObject.GetComponent<Collider>();
        if (trigger != null) trigger.isTrigger = true;

        Rigidbody rb = startButtonObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        Renderer renderer = startButtonObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = CreateGlowMaterial(new Color(1f, 0.05f, 0.02f, 1f), 2.5f);
        }

        Light light = startButtonObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.08f, 0.03f, 1f);
        light.range = 5f;
        light.intensity = 3f;

        RunStartButtonTrigger triggerScript = startButtonObject.AddComponent<RunStartButtonTrigger>();
        triggerScript.Initialize(this);
    }

    private void PlaceExitInEndRoom()
    {
        PlacedRoom endRoom = FindEndRoom();
        if (endRoom == null || endRoom.root == null)
        {
            Debug.LogWarning("[RunFlow] Cannot place next-level exit because no generated end room was found.", this);
            return;
        }

        Vector3 position = GetRoomExitCenter(endRoom.root);

        exitObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        exitObject.name = $"Next Level Exit {RunSetupData.EnsureInstance().levelIndex + 1}";
        exitObject.transform.SetPositionAndRotation(position, Quaternion.identity);
        exitObject.transform.localScale = new Vector3(1.35f, 0.45f, 1.35f);

        Collider trigger = exitObject.GetComponent<Collider>();
        if (trigger != null) trigger.isTrigger = true;

        Rigidbody rb = exitObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        Renderer renderer = exitObject.GetComponent<Renderer>();
        Material exitMaterial = CreateGlowMaterial(new Color(0.1f, 0.9f, 1f, 1f), 2.2f);
        if (renderer != null)
        {
            renderer.sharedMaterial = exitMaterial;
        }

        CreateExitBeacon(exitObject.transform, exitMaterial);

        Light light = exitObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.1f, 0.75f, 1f, 1f);
        light.range = 6f;
        light.intensity = 2.8f;

        RunLevelExitTrigger triggerScript = exitObject.AddComponent<RunLevelExitTrigger>();
        triggerScript.Initialize(this);
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

    private static Vector3 GetRoomExitCenter(GameObject roomRoot)
    {
        Transform boundsTransform = roomRoot.transform.Find("Bounds");
        if (boundsTransform != null && boundsTransform.TryGetComponent(out BoxCollider boundsCollider))
        {
            Bounds bounds = boundsCollider.bounds;
            return new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
        }

        Renderer[] renderers = roomRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return new Vector3(bounds.center.x, Mathf.Max(bounds.center.y, bounds.min.y + 0.75f), bounds.center.z);
        }

        return roomRoot.transform.position + Vector3.up * 0.75f;
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

    private static void CreateExitBeacon(Transform parent, Material material)
    {
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "Exit Beacon Beam";
        beam.transform.SetParent(parent, false);
        beam.transform.localPosition = Vector3.up * 1.05f;
        beam.transform.localScale = new Vector3(0.28f, 1.1f, 0.28f);
        DisableCollider(beam);

        Renderer beamRenderer = beam.GetComponent<Renderer>();
        if (beamRenderer != null) beamRenderer.sharedMaterial = material;

        GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "Exit Beacon Orb";
        orb.transform.SetParent(parent, false);
        orb.transform.localPosition = Vector3.up * 2.2f;
        orb.transform.localScale = Vector3.one * 0.65f;
        DisableCollider(orb);

        Renderer orbRenderer = orb.GetComponent<Renderer>();
        if (orbRenderer != null) orbRenderer.sharedMaterial = material;
    }

    private static void DisableCollider(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
    }

    private void ClearExitObject()
    {
        if (exitObject == null) return;

        exitObject.SetActive(false);
        Destroy(exitObject);
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

        GameObject canvasObject = new GameObject("Run Selection Canvas");
        selectionCanvas = canvasObject.AddComponent<Canvas>();
        selectionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        selectionCanvas.sortingOrder = 200;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        Image backdrop = CreateImage("Backdrop", selectionCanvas.transform, new Color(0f, 0f, 0f, 0.55f));
        StretchToParent(backdrop.rectTransform);

        GameObject panelObject = new GameObject("Panel");
        RectTransform panel = panelObject.AddComponent<RectTransform>();
        panel.SetParent(selectionCanvas.transform, false);
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(460f, 360f);

        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.05f, 0.06f, 0.07f, 0.94f);

        VerticalLayoutGroup layout = panelObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 24, 24);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TMP_Text title = CreateText("Title", panel, "Minions", 30f, FontStyles.Bold, TextAlignmentOptions.Center);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

        meleeRow = CreateSelectionRow(panel, "Melee");
        rangedRow = CreateSelectionRow(panel, "Ranged");
        supportRow = CreateSelectionRow(panel, "Support");

        totalText = CreateText("Total", panel, string.Empty, 22f, FontStyles.Bold, TextAlignmentOptions.Center);
        totalText.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

        startRunButton = CreateButton(panel, "StartButton", "Start", new Color(0.1f, 0.65f, 0.45f, 1f));
        startRunButton.GetComponent<LayoutElement>().preferredHeight = 52f;
        startRunButton.onClick.AddListener(() =>
            StartSelectedRun(meleeRow.Value, rangedRow.Value, supportRow.Value));

        selectionCanvas.gameObject.SetActive(false);
    }

    private void LoadRowsFromRunSetup()
    {
        RunSetupData data = RunSetupData.EnsureInstance();

        meleeRow.SetValue(data.typeA);
        rangedRow.SetValue(data.typeB);
        supportRow.SetValue(data.typeC);
    }

    private SelectionRow CreateSelectionRow(Transform parent, string label)
    {
        GameObject rowObject = new GameObject(label + " Row");
        RectTransform rowTransform = rowObject.AddComponent<RectTransform>();
        rowTransform.SetParent(parent, false);

        HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;

        rowObject.AddComponent<LayoutElement>().preferredHeight = 50f;

        TMP_Text labelText = CreateText(label + " Label", rowTransform, label, 22f, FontStyles.Normal, TextAlignmentOptions.Left);
        LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
        labelLayout.flexibleWidth = 1f;
        labelLayout.preferredHeight = 48f;

        Button minusButton = CreateButton(rowTransform, label + " Minus", "-", new Color(0.25f, 0.28f, 0.31f, 1f));
        Button plusButton = CreateButton(rowTransform, label + " Plus", "+", new Color(0.25f, 0.28f, 0.31f, 1f));
        TMP_Text valueText = CreateText(label + " Value", rowTransform, "0", 24f, FontStyles.Bold, TextAlignmentOptions.Center);
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 62f;

        minusButton.transform.SetSiblingIndex(1);
        valueText.transform.SetSiblingIndex(2);
        plusButton.transform.SetSiblingIndex(3);

        SelectionRow row = new SelectionRow(valueText, minusButton, plusButton);
        minusButton.onClick.AddListener(() =>
        {
            row.SetValue(row.Value - 1);
            UpdateSelectionUI();
        });
        plusButton.onClick.AddListener(() =>
        {
            if (GetSelectedTotal() >= MaxSelectableMinions) return;
            row.SetValue(row.Value + 1);
            UpdateSelectionUI();
        });

        return row;
    }

    private Button CreateButton(Transform parent, string name, string text, Color color)
    {
        GameObject buttonObject = new GameObject(name);
        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = buttonObject.AddComponent<Image>();
        image.color = color;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredWidth = 48f;
        layout.preferredHeight = 48f;

        TMP_Text label = CreateText("Text", rect, text, 22f, FontStyles.Bold, TextAlignmentOptions.Center);
        StretchToParent(label.rectTransform);

        return button;
    }

    private static TMP_Text CreateText(string name, Transform parent, string text, float fontSize, FontStyles style, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.SetParent(parent, false);

        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;

        return label;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(name);
        RectTransform rect = imageObject.AddComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private void UpdateSelectionUI()
    {
        int total = GetSelectedTotal();
        totalText.text = $"{total}/{MaxSelectableMinions}";
        startRunButton.interactable = total >= 0 && total <= MaxSelectableMinions;

        bool canAdd = total < MaxSelectableMinions;
        meleeRow.SetCanAdd(canAdd);
        rangedRow.SetCanAdd(canAdd);
        supportRow.SetCanAdd(canAdd);
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

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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

    private static Material CreateGlowMaterial(Color baseColor, float emissionIntensity)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);

        int baseColorId = Shader.PropertyToID("_BaseColor");
        int colorId = Shader.PropertyToID("_Color");
        int emissionId = Shader.PropertyToID("_EmissionColor");

        if (material.HasProperty(baseColorId)) material.SetColor(baseColorId, baseColor);
        if (material.HasProperty(colorId)) material.SetColor(colorId, baseColor);
        if (material.HasProperty(emissionId))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor(emissionId, baseColor * Mathf.Max(0f, emissionIntensity));
        }

        return material;
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

public sealed class RunStartButtonTrigger : MonoBehaviour
{
    private LevelStartRunFlowController controller;

    public void Initialize(LevelStartRunFlowController owner)
    {
        controller = owner;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        controller?.OpenSelectionUI();
    }

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<PlayerMinionCommander>() != null) return true;
        return other.CompareTag("Player") || other.transform.root.CompareTag("Player");
    }
}

public sealed class RunLevelExitTrigger : MonoBehaviour
{
    private LevelStartRunFlowController controller;
    private bool triggered;

    public void Initialize(LevelStartRunFlowController owner)
    {
        controller = owner;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggered || !IsPlayer(other)) return;

        triggered = true;
        controller?.AdvanceToNextLevel();
    }

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<PlayerMinionCommander>() != null) return true;
        return other.CompareTag("Player") || other.transform.root.CompareTag("Player");
    }
}
