using System;
using System.Collections.Generic;
using PCG.RoomAssembler.Data;
using UnityEngine;

public class LevelContentSpawner : MonoBehaviour
{
    private const string GeneratedContentRootName = "PCG_Content";

    [SerializeField] private LevelContentSpawnConfig config;
    [SerializeField, Min(1)] private int levelIndex = 1;
    [SerializeField] private Transform contentParent;
    [SerializeField] private bool forceSpawnedObjectsActive = true;
    [SerializeField] private bool reuseExistingPlayer = true;

    [Header("Debug")]
    [SerializeField, Tooltip("Enables non-critical PCG content status and diagnostic logs for this spawner.")]
    private bool enableLogs;

    [Header("Run Setup")]
    [SerializeField] private bool useRunSetupData = true;
    [SerializeField, Min(0)] private int fallbackMeleeMinions;
    [SerializeField, Min(0)] private int fallbackRangedMinions;
    [SerializeField, Min(0)] private int fallbackSupportMinions;

    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly List<SpawnedObjectState> spawnedObjectStates = new List<SpawnedObjectState>();
    private Transform generatedContentRoot;

    public IReadOnlyList<GameObject> SpawnedObjects => spawnedObjects;
    public GameObject CurrentPlayer { get; private set; }
    public LevelContentSpawnConfig Config => config;
    public int LevelIndex
    {
        get => levelIndex;
        set => levelIndex = Mathf.Max(1, value);
    }

    public void SetRuntimeConfig(LevelContentSpawnConfig runtimeConfig)
    {
        config = runtimeConfig;
    }

    private void Update()
    {
        if (!LogsEnabled) return;

        for (int i = spawnedObjectStates.Count - 1; i >= 0; i--)
        {
            SpawnedObjectState state = spawnedObjectStates[i];
            if (state.Object == null)
            {
                spawnedObjectStates.RemoveAt(i);
                continue;
            }

            bool activeSelf = state.Object.activeSelf;
            bool activeInHierarchy = state.Object.activeInHierarchy;

            if (state.WasActiveSelf != activeSelf || state.WasActiveInHierarchy != activeInHierarchy)
            {
                Log(
                    $"[PCG Content] Active state changed: {state.Object.name} " +
                    $"activeSelf {state.WasActiveSelf}->{activeSelf}, " +
                    $"activeInHierarchy {state.WasActiveInHierarchy}->{activeInHierarchy}. " +
                    "If activeSelf changed, a script or manual scene action disabled the root object. " +
                    "If only activeInHierarchy changed, one of its parents was disabled.",
                    state.Object);

                state.WasActiveSelf = activeSelf;
                state.WasActiveInHierarchy = activeInHierarchy;
                spawnedObjectStates[i] = state;
            }

            int inactiveChildCount = CountInactiveChildren(state.Object);
            if (state.InactiveChildCount != inactiveChildCount)
            {
                Log(
                    $"[PCG Content] Child active state changed: {state.Object.name} " +
                    $"inactive children {state.InactiveChildCount}->{inactiveChildCount}. " +
                    "This is usually enemy behaviour toggling visuals/hitboxes, not the whole enemy being disabled.",
                    state.Object);

                state.InactiveChildCount = inactiveChildCount;
                spawnedObjectStates[i] = state;
            }
        }
    }

    public void SpawnForGeneratedRooms(IReadOnlyList<PlacedRoom> placedRooms, int layoutSeed)
    {
        if (config == null)
        {
            Debug.LogWarning($"{name}: LevelContentSpawnConfig missing.", this);
            return;
        }

        if (placedRooms == null || placedRooms.Count == 0)
        {
            Debug.LogWarning($"{name}: no rooms available for content spawning.", this);
            return;
        }

        ClearSpawnedObjects();
        GetOrCreateGeneratedContentRoot();

        ResetSpawnPointState(placedRooms);

        System.Random playerRng = CreateChildRandom(layoutSeed, 101);
        System.Random minionRng = CreateChildRandom(layoutSeed, 202);
        System.Random enemyRng = CreateChildRandom(layoutSeed, 303);
        System.Random itemRng = CreateChildRandom(layoutSeed, 404);

        GameObject player = config.spawnPlayer ? SpawnPlayer(placedRooms, playerRng) : FindExistingPlayer();
        CurrentPlayer = player;

        if (config.spawnMinions)
        {
            SpawnMinions(placedRooms, minionRng, player);
        }

        if (config.spawnEnemies)
        {
            SpawnBudgetedContent(placedRooms, PCGSpawnPointKind.Enemy, config.enemyBudget, config.enemyPool, enemyRng, ApplyEnemyLevelScaling);
        }

        if (config.spawnItems)
        {
            if (ItemDatabase.Instance != null)
            {
                ItemDatabase.Instance.SetSeed(CreateChildSeed(layoutSeed, 505));
            }

            SpawnBudgetedContent(placedRooms, PCGSpawnPointKind.Item, config.itemBudget, config.itemPool, itemRng, null);
        }

        if (LogsEnabled)
        {
            Log($"[PCG Content] Seed={layoutSeed}, Level={levelIndex}, Spawned={spawnedObjects.Count}");
        }
    }

    public void SpawnMinionPartyNearPlayer(GameObject player, int melee, int ranged, int support, int seed, bool clearExistingGeneratedContent = true)
    {
        if (config == null)
        {
            Debug.LogWarning($"{name}: LevelContentSpawnConfig missing.", this);
            return;
        }

        if (player == null)
        {
            player = FindExistingPlayer();
        }
        else
        {
            player = PlayerRootResolver.FromGameObject(player);
        }

        if (player == null)
        {
            Debug.LogWarning($"{name}: no player found for minion party spawn.", this);
            return;
        }

        if (clearExistingGeneratedContent)
        {
            ClearSpawnedObjects();
        }

        CurrentPlayer = player;
        Transform playerBody = PlayerRootResolver.BodyTransform(player);
        PlayerMinionCommander commander = player.GetComponentInChildren<PlayerMinionCommander>();
        if (commander == null) commander = FindFirstObjectByType<PlayerMinionCommander>();
        if (commander != null) commander.ClearRegisteredMinions();

        System.Random rng = CreateChildRandom(seed, 202);
        int fallbackIndex = 0;
        int fallbackTotal = Mathf.Max(1, Mathf.Max(0, melee) + Mathf.Max(0, ranged) + Mathf.Max(0, support));
        SpawnMinionGroup("Melee", melee, null, rng, commander, playerBody, fallbackTotal, ref fallbackIndex);
        SpawnMinionGroup("Ranged", ranged, null, rng, commander, playerBody, fallbackTotal, ref fallbackIndex);
        SpawnMinionGroup("Support", support, null, rng, commander, playerBody, fallbackTotal, ref fallbackIndex);
    }

    public void ClearSpawnedObjects()
    {
        HashSet<GameObject> objectsToDestroy = new HashSet<GameObject>();
        GameObject protectedPlayer = reuseExistingPlayer ? FindExistingPlayer() : null;

        for (int i = 0; i < spawnedObjects.Count; i++)
        {
            if (spawnedObjects[i] != null && !IsProtectedPlayerObject(spawnedObjects[i], protectedPlayer))
                objectsToDestroy.Add(spawnedObjects[i]);
        }

        PCGGeneratedContentMarker[] markers = FindObjectsByType<PCGGeneratedContentMarker>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < markers.Length; i++)
        {
            PCGGeneratedContentMarker marker = markers[i];
            if (marker != null && marker.Owner == this && !IsProtectedPlayerObject(marker.gameObject, protectedPlayer))
                objectsToDestroy.Add(marker.gameObject);
        }

        Transform host = contentParent != null ? contentParent : transform;
        generatedContentRoot = host.Find(GeneratedContentRootName);
        if (generatedContentRoot != null)
        {
            for (int i = 0; i < generatedContentRoot.childCount; i++)
            {
                GameObject childObject = generatedContentRoot.GetChild(i).gameObject;
                if (!IsProtectedPlayerObject(childObject, protectedPlayer))
                    objectsToDestroy.Add(childObject);
            }
        }

        // Older generated content was parented directly below the configured host.
        // Match configured prefab names so the room hierarchy is left untouched.
        for (int i = 0; i < host.childCount; i++)
        {
            Transform child = host.GetChild(i);
            if (child == generatedContentRoot)
                continue;

            if (IsProtectedPlayerObject(child.gameObject, protectedPlayer))
                continue;

            if (MatchesConfiguredPrefabName(child.name))
                objectsToDestroy.Add(child.gameObject);
        }

        foreach (GameObject spawnedObject in objectsToDestroy)
        {
            if (spawnedObject == null)
                continue;

            if (Application.isPlaying)
            {
                // Destroy is delayed until end of frame. Disable immediately so stale
                // enemies cannot move or attack while a new layout is being generated.
                spawnedObject.SetActive(false);
                Destroy(spawnedObject);
            }
            else
            {
                DestroyImmediate(spawnedObject);
            }
        }

        spawnedObjects.Clear();
        spawnedObjectStates.Clear();

        if (LogsEnabled && objectsToDestroy.Count > 0)
            Log($"[PCG Content] Cleared {objectsToDestroy.Count} generated object(s).");
    }

    private GameObject SpawnPlayer(IReadOnlyList<PlacedRoom> rooms, System.Random rng)
    {
        PCGSpawnPoint point = PickSpawnPoint(CollectSpawnPoints(rooms, PCGSpawnPointKind.Player), rng, includeRequiredOnly: false);
        if (point == null)
        {
            LogWarning($"{name}: no player spawnpoint found. Player content not spawned.");
            return null;
        }

        if (reuseExistingPlayer)
        {
            GameObject existingPlayer = FindExistingPlayer();
            if (existingPlayer != null)
            {
                MoveExistingPlayer(existingPlayer, point.transform.position, point.transform.rotation);
                point.occupied = true;
                return existingPlayer;
            }
        }

        if (config.playerPrefab == null) return null;

        return SpawnPrefab(config.playerPrefab, point, "Player");
    }

    private void SpawnMinions(IReadOnlyList<PlacedRoom> rooms, System.Random rng, GameObject player)
    {
        int melee = fallbackMeleeMinions;
        int ranged = fallbackRangedMinions;
        int support = fallbackSupportMinions;

        if (useRunSetupData && RunSetupData.Instance != null)
        {
            melee = RunSetupData.Instance.typeA;
            ranged = RunSetupData.Instance.typeB;
            support = RunSetupData.Instance.typeC;
        }

        int total = Mathf.Max(0, melee) + Mathf.Max(0, ranged) + Mathf.Max(0, support);
        if (total == 0) return;

        List<PCGSpawnPoint> points = CollectSpawnPoints(rooms, PCGSpawnPointKind.Minion);
        PlayerMinionCommander commander = player != null ? player.GetComponentInChildren<PlayerMinionCommander>() : FindFirstObjectByType<PlayerMinionCommander>();
        if (commander != null) commander.ClearRegisteredMinions();
        Transform playerBody = PlayerRootResolver.BodyTransform(player);

        int fallbackIndex = 0;
        int fallbackTotal = Mathf.Max(1, total);
        SpawnMinionGroup("Melee", melee, points, rng, commander, playerBody, fallbackTotal, ref fallbackIndex);
        SpawnMinionGroup("Ranged", ranged, points, rng, commander, playerBody, fallbackTotal, ref fallbackIndex);
        SpawnMinionGroup("Support", support, points, rng, commander, playerBody, fallbackTotal, ref fallbackIndex);
    }

    private void SpawnMinionGroup(
        string roleId,
        int count,
        List<PCGSpawnPoint> points,
        System.Random rng,
        PlayerMinionCommander commander,
        Transform playerBody,
        int fallbackTotal,
        ref int fallbackIndex)
    {
        for (int i = 0; i < count; i++)
        {
            PCGSpawnPoint point = PickSpawnPoint(points, rng, includeRequiredOnly: false);
            WeightedSpawnEntry entry = PickWeightedEntry(config.minionPool, rng, point, roleId);

            if (entry == null || entry.prefab == null) return;

            int fallbackSlot = fallbackIndex++;
            GameObject minionObject = point != null
                ? SpawnPrefab(entry.prefab, point, roleId)
                : SpawnPrefabNearPlayer(entry.prefab, playerBody, roleId, fallbackSlot, fallbackTotal);

            if (minionObject == null) continue;

            MinionCore minion = minionObject.GetComponentInChildren<MinionCore>();
            if (minion != null && playerBody != null)
            {
                minion.SetFollowTarget(playerBody);
            }

            if (commander != null && minion != null)
            {
                commander.RegisterMinion(minion);
            }
        }
    }

    private void SpawnBudgetedContent(
        IReadOnlyList<PlacedRoom> rooms,
        PCGSpawnPointKind kind,
        SpawnBudget budget,
        List<WeightedSpawnEntry> pool,
        System.Random rng,
        Action<GameObject> afterSpawn)
    {
        if (budget == null)
        {
            LogWarning($"[PCG Content] {kind}: budget missing.");
            return;
        }

        if (pool == null || pool.Count == 0)
        {
            LogWarning($"[PCG Content] {kind}: pool is empty.");
            return;
        }

        List<RoomSpawnContext> roomContexts = BuildRoomContexts(rooms, kind);
        if (roomContexts.Count == 0)
        {
            LogWarning($"[PCG Content] {kind}: no valid spawnpoints found for level {levelIndex}. Add PCGSpawnPoint components with kind {kind} to generated room prefabs.");
            return;
        }

        int capacity = 0;
        for (int i = 0; i < roomContexts.Count; i++)
        {
            capacity += Mathf.Min(Mathf.Max(0, budget.maxPerRoom), roomContexts[i].AvailableCount);
        }

        if (capacity <= 0)
        {
            LogWarning($"[PCG Content] {kind}: capacity is 0. Check {kind} budget maxPerRoom. Current maxPerRoom={budget.maxPerRoom}, valid rooms={roomContexts.Count}.");
            return;
        }

        int roomMinimumTotal = 0;
        for (int i = 0; i < roomContexts.Count; i++)
        {
            roomMinimumTotal += Mathf.Min(Mathf.Max(0, budget.minPerRoom), Mathf.Max(0, budget.maxPerRoom), roomContexts[i].AvailableCount);
        }

        int minTotal = Mathf.Min(capacity, Mathf.Max(roomMinimumTotal, budget.GetScaledMin(levelIndex, config.ScalesSpawnAmounts)));
        int maxTotal = Mathf.Min(capacity, budget.GetScaledMax(levelIndex, config.ScalesSpawnAmounts));
        maxTotal = Mathf.Max(minTotal, maxTotal);
        if (maxTotal <= 0)
        {
            LogWarning($"[PCG Content] {kind}: scaled level max is 0. Check min/max per level.");
            return;
        }

        int totalToSpawn = rng.Next(minTotal, maxTotal + 1);
        int spawned = 0;

        SpawnRequiredPoints(roomContexts, pool, rng, budget, afterSpawn, ref spawned, totalToSpawn);
        SpawnRoomMinimums(roomContexts, pool, rng, budget, afterSpawn, ref spawned, totalToSpawn);

        int safety = roomContexts.Count * Mathf.Max(1, budget.maxPerRoom) * 4;
        while (spawned < totalToSpawn && safety-- > 0)
        {
            RoomSpawnContext room = PickRoomWithCapacity(roomContexts, budget, rng);
            if (room == null) break;

            PCGSpawnPoint point = PickSpawnPoint(room.Points, rng, includeRequiredOnly: false);
            WeightedSpawnEntry entry = PickWeightedEntry(pool, rng, point, null);
            if (point == null || entry == null || entry.prefab == null) break;

            GameObject spawnedObject = SpawnPrefab(entry.prefab, point, entry.id);
            afterSpawn?.Invoke(spawnedObject);
            room.SpawnedCount++;
            spawned++;
        }

        if (spawned < totalToSpawn)
        {
            LogWarning($"[PCG Content] {kind}: spawned {spawned}/{totalToSpawn}. Check pool entry level ranges, maxPerLevel, prefab assignments, and spawnpoint allowedContentIds.");
        }
    }

    private void SpawnRequiredPoints(
        List<RoomSpawnContext> roomContexts,
        List<WeightedSpawnEntry> pool,
        System.Random rng,
        SpawnBudget budget,
        Action<GameObject> afterSpawn,
        ref int spawned,
        int totalToSpawn)
    {
        for (int r = 0; r < roomContexts.Count; r++)
        {
            RoomSpawnContext room = roomContexts[r];
            for (int p = 0; p < room.Points.Count; p++)
            {
                if (spawned >= totalToSpawn) return;
                if (room.SpawnedCount >= budget.maxPerRoom) break;

                PCGSpawnPoint point = room.Points[p];
                if (point == null || point.occupied || !point.required) continue;

                WeightedSpawnEntry entry = PickWeightedEntry(pool, rng, point, null);
                if (entry == null || entry.prefab == null) continue;

                GameObject spawnedObject = SpawnPrefab(entry.prefab, point, entry.id);
                afterSpawn?.Invoke(spawnedObject);
                room.SpawnedCount++;
                spawned++;
            }
        }
    }

    private void SpawnRoomMinimums(
        List<RoomSpawnContext> roomContexts,
        List<WeightedSpawnEntry> pool,
        System.Random rng,
        SpawnBudget budget,
        Action<GameObject> afterSpawn,
        ref int spawned,
        int totalToSpawn)
    {
        for (int r = 0; r < roomContexts.Count; r++)
        {
            RoomSpawnContext room = roomContexts[r];
            int roomMinimum = Mathf.Min(Mathf.Max(0, budget.minPerRoom), Mathf.Max(0, budget.maxPerRoom), room.AvailableCount + room.SpawnedCount);

            while (spawned < totalToSpawn && room.SpawnedCount < roomMinimum && room.SpawnedCount < budget.maxPerRoom)
            {
                PCGSpawnPoint point = PickSpawnPoint(room.Points, rng, includeRequiredOnly: false);
                WeightedSpawnEntry entry = PickWeightedEntry(pool, rng, point, null);
                if (point == null || entry == null || entry.prefab == null) break;

                GameObject spawnedObject = SpawnPrefab(entry.prefab, point, entry.id);
                afterSpawn?.Invoke(spawnedObject);
                room.SpawnedCount++;
                spawned++;
            }
        }
    }

    private void ApplyEnemyLevelScaling(GameObject enemy)
    {
        if (enemy == null || !config.ScalesEnemyStats) return;

        CombatantStats stats = enemy.GetComponentInChildren<CombatantStats>();
        if (stats == null) return;

        float multiplier = Mathf.Max(0.01f, config.enemyStatMultiplierByLevel.Evaluate(levelIndex));
        if (Mathf.Approximately(multiplier, 1f)) return;

        for (int i = 0; i < config.scaledEnemyStats.Count; i++)
        {
            stats.AddPersistentModifier(
                $"pcg-level-scaling-{levelIndex}",
                new CombatStatModifierData
                {
                    Type = config.scaledEnemyStats[i],
                    Operation = StatModifierOperation.Multiply,
                    Value = multiplier
                });
        }

        stats.SetHealth(stats.GetStat(CombatStatType.MaxHealth));
    }

    private GameObject SpawnPrefab(GameObject prefab, PCGSpawnPoint point, string contentId)
    {
        GameObject go = Instantiate(prefab, point.transform.position, point.transform.rotation, GetOrCreateGeneratedContentRoot());
        go.name = string.IsNullOrWhiteSpace(contentId) ? prefab.name : $"{prefab.name}_{contentId}";
        RegisterSpawnedObject(go, prefab, point);
        return go;
    }

    private GameObject SpawnPrefabNearPlayer(GameObject prefab, Transform playerBody, string contentId, int indexInGroup, int groupCount)
    {
        if (prefab == null || playerBody == null) return null;

        Vector3 basePosition = playerBody.position;
        Vector3 forward = playerBody.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float angle = groupCount <= 1 ? 0f : (indexInGroup / (float)groupCount) * Mathf.PI * 2f;
        float radius = 1.6f + Mathf.Floor(indexInGroup / 8f) * 0.7f;
        Vector3 radial = right * Mathf.Cos(angle) - forward * Mathf.Sin(angle);
        Vector3 position = basePosition - forward * 1.75f + radial * radius;

        const float navSampleRadius = 0.85f;
        if (UnityEngine.AI.NavMesh.SamplePosition(position, out UnityEngine.AI.NavMeshHit hit, navSampleRadius, UnityEngine.AI.NavMesh.AllAreas))
        {
            Vector3 navDelta = hit.position - position;
            navDelta.y = 0f;
            if (navDelta.sqrMagnitude <= navSampleRadius * navSampleRadius)
            {
                position = hit.position;
            }
        }

        return SpawnPrefabAt(prefab, position, playerBody.rotation, contentId);
    }

    private GameObject SpawnPrefabAt(GameObject prefab, Vector3 position, Quaternion rotation, string contentId)
    {
        GameObject go = Instantiate(prefab, position, rotation, GetOrCreateGeneratedContentRoot());
        go.name = string.IsNullOrWhiteSpace(contentId) ? prefab.name : $"{prefab.name}_{contentId}";
        RegisterSpawnedObject(go, prefab, null);
        return go;
    }

    private void RegisterSpawnedObject(GameObject go, GameObject prefab, PCGSpawnPoint point)
    {
        PCGGeneratedContentMarker marker = go.GetComponent<PCGGeneratedContentMarker>();
        if (marker == null)
            marker = go.AddComponent<PCGGeneratedContentMarker>();
        marker.Initialize(this);

        bool prefabSpawnedInactive = !go.activeSelf;
        if (forceSpawnedObjectsActive && prefabSpawnedInactive)
        {
            go.SetActive(true);
        }

        if (point != null)
        {
            point.occupied = true;
        }

        spawnedObjects.Add(go);

        int inactiveChildCount = CountInactiveChildren(go);
        spawnedObjectStates.Add(new SpawnedObjectState
        {
            Object = go,
            WasActiveSelf = go.activeSelf,
            WasActiveInHierarchy = go.activeInHierarchy,
            InactiveChildCount = inactiveChildCount
        });

        if (LogsEnabled)
        {
            if (prefabSpawnedInactive)
            {
                LogWarning(
                    $"[PCG Content] {go.name} was instantiated inactive because prefab '{prefab.name}' root is inactive. " +
                    $"forceSpawnedObjectsActive={forceSpawnedObjectsActive}.",
                    go);
            }

            string pointName = point != null ? point.name : "player fallback";
            Log(
                $"[PCG Content] Spawned {go.name} at {pointName} " +
                $"activeSelf={go.activeSelf}, activeInHierarchy={go.activeInHierarchy}, inactiveChildren={inactiveChildCount}.",
                go);
        }
    }

    private static GameObject FindExistingPlayer()
    {
        PlayerMinionCommander commander = FindFirstObjectByType<PlayerMinionCommander>();
        if (commander != null)
        {
            return PlayerRootResolver.FromCommander(commander);
        }

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
            // The Player tag may not exist in isolated test scenes.
        }

        return null;
    }

    private static bool IsProtectedPlayerObject(GameObject candidate, GameObject protectedPlayer)
    {
        if (candidate == null || protectedPlayer == null) return false;

        Transform candidateTransform = candidate.transform;
        Transform protectedTransform = protectedPlayer.transform;

        return candidateTransform == protectedTransform
            || candidateTransform.IsChildOf(protectedTransform)
            || protectedTransform.IsChildOf(candidateTransform);
    }

    private static void MoveExistingPlayer(GameObject player, Vector3 position, Quaternion rotation)
    {
        if (player == null) return;

        CharacterController[] controllers = player.GetComponentsInChildren<CharacterController>();
        bool[] controllerStates = new bool[controllers.Length];
        for (int i = 0; i < controllers.Length; i++)
        {
            controllerStates[i] = controllers[i] != null && controllers[i].enabled;
            if (controllers[i] != null) controllers[i].enabled = false;
        }

        Transform body = PlayerRootResolver.BodyTransform(player);
        player.transform.SetPositionAndRotation(position, rotation);
        if (body != null && body != player.transform)
        {
            body.localPosition = Vector3.zero;
            body.localRotation = Quaternion.identity;
        }

        Rigidbody[] rigidbodies = player.GetComponentsInChildren<Rigidbody>();
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rb = rigidbodies[i];
            if (rb == null) continue;
            if (rb.isKinematic) continue;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Physics.SyncTransforms();

        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null) controllers[i].enabled = controllerStates[i];
        }
    }

    private Transform GetOrCreateGeneratedContentRoot()
    {
        if (generatedContentRoot != null)
            return generatedContentRoot;

        Transform host = contentParent != null ? contentParent : transform;
        generatedContentRoot = host.Find(GeneratedContentRootName);
        if (generatedContentRoot != null)
            return generatedContentRoot;

        GameObject rootObject = new GameObject(GeneratedContentRootName);
        generatedContentRoot = rootObject.transform;
        generatedContentRoot.SetParent(host, false);
        return generatedContentRoot;
    }

    private bool MatchesConfiguredPrefabName(string instanceName)
    {
        if (config == null || string.IsNullOrEmpty(instanceName))
            return false;

        if (MatchesPrefabName(instanceName, config.playerPrefab))
            return true;

        return PoolContainsPrefabName(config.enemyPool, instanceName)
            || PoolContainsPrefabName(config.minionPool, instanceName)
            || PoolContainsPrefabName(config.itemPool, instanceName);
    }

    private static bool PoolContainsPrefabName(List<WeightedSpawnEntry> pool, string instanceName)
    {
        if (pool == null)
            return false;

        for (int i = 0; i < pool.Count; i++)
        {
            WeightedSpawnEntry entry = pool[i];
            if (entry != null && MatchesPrefabName(instanceName, entry.prefab))
                return true;
        }

        return false;
    }

    private static bool MatchesPrefabName(string instanceName, GameObject prefab)
    {
        return prefab != null
            && instanceName.StartsWith(prefab.name + "_", StringComparison.Ordinal);
    }

    private static int CountInactiveChildren(GameObject root)
    {
        if (root == null) return 0;

        int count = 0;
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == null || children[i].gameObject == root) continue;
            if (!children[i].gameObject.activeSelf) count++;
        }

        return count;
    }

    private bool LogsEnabled => enableLogs && config != null && config.log;

    private void Log(string message, UnityEngine.Object context = null)
    {
        if (LogsEnabled)
            Debug.Log(message, context != null ? context : this);
    }

    private void LogWarning(string message, UnityEngine.Object context = null)
    {
        if (LogsEnabled)
            Debug.LogWarning(message, context != null ? context : this);
    }

    private List<RoomSpawnContext> BuildRoomContexts(IReadOnlyList<PlacedRoom> rooms, PCGSpawnPointKind kind)
    {
        List<RoomSpawnContext> result = new List<RoomSpawnContext>();

        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i]?.root == null) continue;

            PCGSpawnPoint[] points = rooms[i].root.GetComponentsInChildren<PCGSpawnPoint>(true);
            List<PCGSpawnPoint> valid = new List<PCGSpawnPoint>();

            for (int p = 0; p < points.Length; p++)
            {
                PCGSpawnPoint point = points[p];
                if (point == null || point.kind != kind || point.occupied || !point.IsValidForLevel(levelIndex)) continue;
                if (!IsSpawnPointInsideRoomBounds(rooms[i], point))
                {
                    LogWarning(
                        $"[PCG Content] Ignoring {kind} spawnpoint '{point.name}' because it is " +
                        $"outside room '{rooms[i].root.name}' Bounds.",
                        point);
                    continue;
                }

                valid.Add(point);
            }

            if (valid.Count > 0)
            {
                result.Add(new RoomSpawnContext { Room = rooms[i], Points = valid });
            }
        }

        return result;
    }

    private List<PCGSpawnPoint> CollectSpawnPoints(IReadOnlyList<PlacedRoom> rooms, PCGSpawnPointKind kind)
    {
        List<PCGSpawnPoint> result = new List<PCGSpawnPoint>();

        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i]?.root == null) continue;

            PCGSpawnPoint[] points = rooms[i].root.GetComponentsInChildren<PCGSpawnPoint>(true);
            for (int p = 0; p < points.Length; p++)
            {
                PCGSpawnPoint point = points[p];
                if (point == null || point.kind != kind || point.occupied || !point.IsValidForLevel(levelIndex)) continue;
                if (!IsSpawnPointInsideRoomBounds(rooms[i], point))
                {
                    LogWarning(
                        $"[PCG Content] Ignoring {kind} spawnpoint '{point.name}' because it is " +
                        $"outside room '{rooms[i].root.name}' Bounds.",
                        point);
                    continue;
                }

                result.Add(point);
            }
        }

        return result;
    }

    private static bool IsSpawnPointInsideRoomBounds(PlacedRoom room, PCGSpawnPoint point)
    {
        if (room?.root == null || point == null) return false;

        Transform boundsTransform = room.root.transform.Find("Bounds");
        if (boundsTransform == null) return true;

        BoxCollider bounds = boundsTransform.GetComponent<BoxCollider>();
        if (bounds == null || !bounds.enabled) return true;

        Vector3 localPoint = bounds.transform.InverseTransformPoint(point.transform.position);
        Vector3 halfSize = bounds.size * 0.5f;
        Vector3 delta = localPoint - bounds.center;
        const float tolerance = 0.05f;

        return Mathf.Abs(delta.x) <= halfSize.x + tolerance &&
               Mathf.Abs(delta.z) <= halfSize.z + tolerance;
    }

    private static void ResetSpawnPointState(IReadOnlyList<PlacedRoom> rooms)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i]?.root == null) continue;

            PCGSpawnPoint[] points = rooms[i].root.GetComponentsInChildren<PCGSpawnPoint>(true);
            for (int p = 0; p < points.Length; p++)
            {
                if (points[p] != null) points[p].occupied = false;
            }
        }
    }

    private static PCGSpawnPoint PickSpawnPoint(List<PCGSpawnPoint> points, System.Random rng, bool includeRequiredOnly)
    {
        if (points == null || points.Count == 0) return null;

        int total = 0;
        for (int i = 0; i < points.Count; i++)
        {
            PCGSpawnPoint point = points[i];
            if (point == null || point.occupied) continue;
            if (includeRequiredOnly && !point.required) continue;
            total += Mathf.Max(1, point.weight);
        }

        if (total <= 0) return null;

        int roll = rng.Next(0, total);
        int sum = 0;

        for (int i = 0; i < points.Count; i++)
        {
            PCGSpawnPoint point = points[i];
            if (point == null || point.occupied) continue;
            if (includeRequiredOnly && !point.required) continue;

            sum += Mathf.Max(1, point.weight);
            if (roll < sum) return point;
        }

        return null;
    }

    private WeightedSpawnEntry PickWeightedEntry(List<WeightedSpawnEntry> pool, System.Random rng, PCGSpawnPoint point, string preferredId)
    {
        if (pool == null || pool.Count == 0) return null;

        int total = 0;
        int preferredTotal = 0;

        for (int i = 0; i < pool.Count; i++)
        {
            WeightedSpawnEntry entry = pool[i];
            if (!IsEntryValid(entry, point)) continue;

            int weight = Mathf.Max(1, entry.weight);
            total += weight;

            if (!string.IsNullOrWhiteSpace(preferredId) && string.Equals(entry.id, preferredId, StringComparison.OrdinalIgnoreCase))
            {
                preferredTotal += weight;
            }
        }

        if (total <= 0) return null;

        if (preferredTotal > 0)
        {
            return PickWeightedEntryInternal(pool, rng, point, preferredId, preferredTotal);
        }

        return PickWeightedEntryInternal(pool, rng, point, null, total);
    }

    private WeightedSpawnEntry PickWeightedEntryInternal(List<WeightedSpawnEntry> pool, System.Random rng, PCGSpawnPoint point, string requiredId, int total)
    {
        int roll = rng.Next(0, total);
        int sum = 0;

        for (int i = 0; i < pool.Count; i++)
        {
            WeightedSpawnEntry entry = pool[i];
            if (!IsEntryValid(entry, point)) continue;
            if (!string.IsNullOrWhiteSpace(requiredId) && !string.Equals(entry.id, requiredId, StringComparison.OrdinalIgnoreCase)) continue;

            sum += Mathf.Max(1, entry.weight);
            if (roll < sum) return entry;
        }

        return null;
    }

    private bool IsEntryValid(WeightedSpawnEntry entry, PCGSpawnPoint point)
    {
        if (entry == null || entry.prefab == null) return false;
        int minLevel = Mathf.Max(1, entry.minLevel);
        int maxLevel = entry.maxLevel <= 0 ? int.MaxValue : Mathf.Max(minLevel, entry.maxLevel);
        int maxPerLevel = entry.maxPerLevel <= 0 ? int.MaxValue : entry.maxPerLevel;

        if (levelIndex < minLevel || levelIndex > maxLevel) return false;
        if (point != null && !point.AllowsContent(entry.id)) return false;
        if (maxPerLevel <= CountSpawnedByPrefab(entry.prefab)) return false;
        return true;
    }

    private int CountSpawnedByPrefab(GameObject prefab)
    {
        if (prefab == null) return 0;

        int count = 0;
        for (int i = 0; i < spawnedObjects.Count; i++)
        {
            if (spawnedObjects[i] == null) continue;
            if (spawnedObjects[i].name.StartsWith(prefab.name, StringComparison.Ordinal)) count++;
        }

        return count;
    }

    private static RoomSpawnContext PickRoomWithCapacity(List<RoomSpawnContext> rooms, SpawnBudget budget, System.Random rng)
    {
        int total = 0;
        for (int i = 0; i < rooms.Count; i++)
        {
            RoomSpawnContext room = rooms[i];
            int remainingRoomBudget = Mathf.Max(0, budget.maxPerRoom - room.SpawnedCount);
            int availablePoints = room.AvailableCount;
            total += Mathf.Min(remainingRoomBudget, availablePoints);
        }

        if (total <= 0) return null;

        int roll = rng.Next(0, total);
        int sum = 0;

        for (int i = 0; i < rooms.Count; i++)
        {
            RoomSpawnContext room = rooms[i];
            int weight = Mathf.Min(Mathf.Max(0, budget.maxPerRoom - room.SpawnedCount), room.AvailableCount);
            sum += weight;
            if (roll < sum) return room;
        }

        return null;
    }

    private static System.Random CreateChildRandom(int seed, int salt)
    {
        return new System.Random(CreateChildSeed(seed, salt));
    }

    private static int CreateChildSeed(int seed, int salt)
    {
        unchecked
        {
            int childSeed = seed;
            childSeed = (childSeed * 397) ^ salt;
            childSeed = (childSeed * 397) ^ 0x5f3759df;
            return childSeed;
        }
    }

    private sealed class RoomSpawnContext
    {
        public PlacedRoom Room;
        public List<PCGSpawnPoint> Points = new List<PCGSpawnPoint>();
        public int SpawnedCount;

        public int AvailableCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Points.Count; i++)
                {
                    if (Points[i] != null && !Points[i].occupied) count++;
                }

                return count;
            }
        }
    }

    private struct SpawnedObjectState
    {
        public GameObject Object;
        public bool WasActiveSelf;
        public bool WasActiveInHierarchy;
        public int InactiveChildCount;
    }
}
