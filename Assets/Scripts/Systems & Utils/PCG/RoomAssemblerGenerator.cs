using System;
using System.Collections.Generic;
using System.Text;
using PCG.RoomAssembler;
using PCG.RoomAssembler.Data;
using PCG.RoomAssembler.Logic;
using PCG.RoomAssembler.Metrics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

public class RoomAssemblerGenerator : MonoBehaviour
{
    public RoomAssemblerConfig config;

    [Header("Output")]
    public Transform parent;
    public bool clearBeforeGenerate = true;
    public bool autoGenerateOnStart = true;

    [Header("Content")]
    [Tooltip("Optional runtime NavMesh build step. Runs after layout generation and before content spawning.")]
    public RuntimeNavMeshBuilder navMeshBuilder;

    [Tooltip("Optional second pass that fills generated rooms with player, minions, enemies, and items.")]
    public LevelContentSpawner contentSpawner;

    public int LastRunSeed { get; private set; }
    public int LastGenerationFailedAttempts { get; private set; }
    public string LastGenerationFailureSummary { get; private set; }
    public bool LastGenerationSucceeded { get; private set; }
    public PCGGenerationMetrics LastGenerationMetrics { get; private set; }
    public IReadOnlyList<PlacedRoom> LastPlacedRooms => placed;
    public bool IsGenerating => isGenerating;

    public void SetRuntimeConfig(RoomAssemblerConfig runtimeConfig)
    {
        config = runtimeConfig;
    }

    private System.Random rng;
    private bool isGenerating;

    private readonly List<PlacedRoom> placed = new();
    private readonly List<OpenSocket> openSockets = new();
    private readonly List<OpenSocket> socketsToCap = new();
    private readonly List<RoomDefinition> deadEndCache = new();

    private RoomPicker roomPicker;
    private RoomPlacer roomPlacer;
    private Capping capping;

    private void Start()
    {
        if (!autoGenerateOnStart) return;
        if (LevelStartRunFlowController.ShouldDeferAutoGeneration(SceneManager.GetActiveScene())) return;

        Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        GenerateWithMetrics();
    }

    public PCGGenerationMetrics GenerateWithMetrics(int? seedOverride = null, int runIndex = 0)
    {
        PCGGenerationMetrics metrics = PCGGenerationMetrics.Create(name, runIndex, config);
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        LastGenerationMetrics = metrics;

        if (!ValidateSetup())
        {
            totalStopwatch.Stop();
            metrics.success = false;
            metrics.failureCategory = GenerationFailureReason.InvalidSetup.ToString();
            metrics.totalGenerationMs = totalStopwatch.Elapsed.TotalMilliseconds;
            return metrics;
        }

        if (isGenerating)
        {
            Debug.LogWarning("[PCG] Generate ignored because generation is already running.", this);
            totalStopwatch.Stop();
            metrics.success = false;
            metrics.failureCategory = GenerationFailureReason.AlreadyGenerating.ToString();
            metrics.totalGenerationMs = totalStopwatch.Elapsed.TotalMilliseconds;
            return metrics;
        }

        if (parent == null) parent = transform;
        if (navMeshBuilder == null) navMeshBuilder = GetComponent<RuntimeNavMeshBuilder>();
        if (navMeshBuilder == null && parent != null) navMeshBuilder = parent.GetComponent<RuntimeNavMeshBuilder>();
        if (contentSpawner == null) contentSpawner = GetComponent<LevelContentSpawner>();

        // Content lives outside PCG_Level, so clear it before layout retries begin.
        // Otherwise a failed regeneration leaves enemies from the previous layout active.
        if (contentSpawner != null)
        {
            contentSpawner.ClearSpawnedObjects();
        }

        isGenerating = true;
        LastGenerationFailedAttempts = 0;
        LastGenerationFailureSummary = string.Empty;
        LastGenerationSucceeded = false;
        var runDiagnostics = new GenerationRunDiagnostics();
        GenerationFailureReason lastFailureReason = GenerationFailureReason.None;

        try
        {
            int normalRetries = Mathf.Max(1, config.maxGenerationRetries);
            int fallbackRetries = config.useEmergencyFallback
                ? Mathf.Max(1, config.emergencyFallbackRetries)
                : 0;
            int totalRetries = normalRetries + fallbackRetries;
            int initialSeed = seedOverride ?? (config.randomSeed ? Environment.TickCount : config.seed);
            metrics.initialSeed = initialSeed;

            for (int attempt = 0; attempt < totalRetries; attempt++)
            {
                if (clearBeforeGenerate) ClearChildren(parent);

                bool emergencyFallback = attempt >= normalRetries;
                metrics.emergencyFallbackUsed = emergencyFallback;
                metrics.fullAttemptsUsed = attempt + 1;
                int displayAttempt = emergencyFallback
                    ? attempt - normalRetries + 1
                    : attempt + 1;
                int displayAttemptLimit = emergencyFallback ? fallbackRetries : normalRetries;

                if (emergencyFallback && attempt == normalRetries)
                {
                    Debug.LogWarning(
                        $"[PCG] Normal generation failed after {normalRetries} attempts. " +
                        $"Starting {fallbackRetries} bounded emergency fallback attempt(s) with relaxed constraints.",
                        this);
                }

                int runSeed = initialSeed + attempt;

                LastRunSeed = runSeed;
                metrics.seed = runSeed;
                rng = new System.Random(runSeed);

                roomPicker = new RoomPicker(rng);
                roomPlacer = new RoomPlacer(
                    parent: parent,
                    rng: rng,
                    overlapPadding: config.overlapPadding,
                    widthToleranceFallback: config.widthToleranceFallback,
                    wallCapInset: config.wallCapInset,
                    log: config.log
                );
                capping = new Capping(roomPicker, roomPlacer);

                placed.Clear();
                openSockets.Clear();
                socketsToCap.Clear();
                BuildDeadEndCache(config.roomPool);

                var startGO = Instantiate(config.startRoom.prefab, Vector3.zero, Quaternion.identity, parent);
                startGO.name = $"START_{config.startRoom.id}";
                var startPlaced = new PlacedRoom(config.startRoom, startGO);
                placed.Add(startPlaced);

                SocketUtils.AddOpenSocketsFromRoom(openSockets, startPlaced);

                float roomUnitWorld = config.roomUnitWorldOverride > 0f
                    ? config.roomUnitWorldOverride
                    : ComputeRoomUnitWorldFromBounds(startGO);

                float minEndWorld = config.minEndDistanceRooms * roomUnitWorld;
                float maxEndWorld = Mathf.Max(minEndWorld, config.maxEndDistanceRooms * roomUnitWorld);
                int effectiveMinRooms = emergencyFallback
                    ? Mathf.Max(RoomAssemblerConfig.MinimumEmergencyRooms, config.emergencyMinimumRooms)
                    : config.minRooms;
                int effectiveMaxRooms = emergencyFallback
                    ? Mathf.Max(effectiveMinRooms, config.maxRooms + Mathf.Max(0, config.emergencyAdditionalMaxRooms))
                    : config.maxRooms;
                bool effectiveUseDistanceRange = config.useEndDistanceRange
                    && !(emergencyFallback && config.emergencyIgnoreEndDistance);
                metrics.effectiveMinRooms = effectiveMinRooms;
                metrics.effectiveMaxRooms = effectiveMaxRooms;
                var attemptDiagnostics = new GenerationAttemptDiagnostics(runSeed);

                double cappingBeforeAttemptMs = metrics.cappingMs;
                Stopwatch placementStopwatch = Stopwatch.StartNew();
                bool layoutSucceeded = GrowUntilEnd(
                    startWorldPos: GetStartCenterWorld(startGO),
                    useDistanceRange: effectiveUseDistanceRange,
                    minEndWorld: minEndWorld,
                    maxEndWorld: maxEndWorld,
                    minRooms: effectiveMinRooms,
                    maxRooms: effectiveMaxRooms,
                    attemptsPerOpenSocket: config.attemptsPerOpenSocket,
                    forceEndWhenEligible: emergencyFallback && config.emergencyForceEndRoom,
                    metrics: metrics,
                    diagnostics: attemptDiagnostics,
                    failureReason: out GenerationFailureReason failureReason
                );
                placementStopwatch.Stop();
                metrics.roomPlacementMs += Math.Max(
                    0d,
                    placementStopwatch.Elapsed.TotalMilliseconds - (metrics.cappingMs - cappingBeforeAttemptMs));
                metrics.candidatePlacementAttempts += attemptDiagnostics.CandidateAttempts;
                metrics.unfillableSockets += attemptDiagnostics.UnfillableSockets;
                AddCounts(metrics.placementFailureCounts, attemptDiagnostics.PlacementFailures);

                if (layoutSucceeded)
                {
                    LastGenerationFailedAttempts = attempt;
                    metrics.failedFullAttempts = attempt;
                    PopulateLayoutMetrics(metrics);

                    metrics.success = metrics.remainingOpenSocketsAfterCapping <= 0;
                    metrics.failureCategory = metrics.success
                        ? GenerationFailureReason.None.ToString()
                        : GenerationFailureReason.OpenSocketsRemainingAfterCapping.ToString();

                    LastGenerationFailureSummary = runDiagnostics.FormatSummary();
                    LastGenerationSucceeded = metrics.success;

                    Debug.Log(
                        $"[PCG] Layout succeeded. Seed={runSeed}, Rooms={placed.Count}, " +
                        $"FailedAttempts={attempt}, EmergencyFallback={emergencyFallback}, " +
                        $"MetricSuccess={metrics.success}. " +
                        LastGenerationFailureSummary,
                        this);

                    if (navMeshBuilder != null)
                    {
                        Debug.Log("[PCG] Building NavMesh.", this);
                        Stopwatch navMeshStopwatch = Stopwatch.StartNew();
                        navMeshBuilder.Build(parent);
                        navMeshStopwatch.Stop();
                        metrics.navMeshMs += navMeshStopwatch.Elapsed.TotalMilliseconds;
                        Debug.Log("[PCG] NavMesh build complete.", this);
                    }

                    if (contentSpawner != null)
                    {
                        Debug.Log("[PCG] Spawning generated level content.", this);
                        Stopwatch contentStopwatch = Stopwatch.StartNew();
                        contentSpawner.SpawnForGeneratedRooms(placed, runSeed);
                        contentStopwatch.Stop();
                        metrics.contentSpawningMs += contentStopwatch.Elapsed.TotalMilliseconds;
                        Debug.Log("[PCG] Content spawning complete.", this);
                    }
                    return metrics;
                }

                lastFailureReason = failureReason;
                PCGGenerationMetrics.Increment(metrics.generationFailureCounts, failureReason.ToString(), 1);
                runDiagnostics.Record(failureReason, attemptDiagnostics);
                Debug.LogWarning(
                    $"[PCG] {(emergencyFallback ? "Emergency fallback" : "Normal")} attempt " +
                    $"{displayAttempt}/{displayAttemptLimit} failed. " +
                    attemptDiagnostics.FormatAttempt(failureReason, placed.Count, openSockets.Count),
                    this);
            }

            LastGenerationFailedAttempts = totalRetries;
            metrics.failedFullAttempts = totalRetries;
            metrics.success = false;
            metrics.failureCategory = lastFailureReason.ToString();
            LastGenerationFailureSummary = runDiagnostics.FormatSummary();
            if (clearBeforeGenerate) ClearChildren(parent);
            Debug.LogError(
                $"[PCG] Generation failed after {totalRetries} bounded attempts. " +
                $"{LastGenerationFailureSummary} Automatic generation has stopped; it will not loop.",
                this);

            return metrics;
        }
        finally
        {
            isGenerating = false;
            totalStopwatch.Stop();
            metrics.totalGenerationMs = totalStopwatch.Elapsed.TotalMilliseconds;
            LastGenerationMetrics = metrics;
        }
    }

    private bool ValidateSetup()
    {
        if (config == null)
        {
            Debug.LogError("RoomAssemblerConfig missing.", this);
            return false;
        }

        if (config.startRoom == null || config.startRoom.prefab == null)
        {
            Debug.LogError("Start room missing.", this);
            return false;
        }

        if (config.endRoom == null || config.endRoom.prefab == null)
        {
            Debug.LogError("End room missing.", this);
            return false;
        }

        if (config.roomPool == null || config.roomPool.Count == 0)
        {
            Debug.LogError("Room pool is empty.", this);
            return false;
        }

        if (config.maxRooms < config.minRooms)
        {
            Debug.LogError("Config invalid: maxRooms < minRooms.", this);
            return false;
        }

        if (config.attemptsPerOpenSocket < 1 || config.maxGenerationRetries < 1)
        {
            Debug.LogError("Config invalid: generation attempts and retries must be at least 1.", this);
            return false;
        }

        if (config.roomOverlapMask == 0)
        {
            Debug.LogWarning("roomOverlapMask is 0. Set it to your 'Generated' layer.", this);
        }

        return true;
    }

    private bool GrowUntilEnd(
        Vector3 startWorldPos,
        bool useDistanceRange,
        float minEndWorld,
        float maxEndWorld,
        int minRooms,
        int maxRooms,
        int attemptsPerOpenSocket,
        bool forceEndWhenEligible,
        PCGGenerationMetrics metrics,
        GenerationAttemptDiagnostics diagnostics,
        out GenerationFailureReason failureReason)
    {
        int safety = Mathf.Max(1, maxRooms) * 250;
        bool endPlaced = false;
        failureReason = GenerationFailureReason.None;

        while (safety-- > 0 && placed.Count < maxRooms)
        {
            if (openSockets.Count == 0)
            {
                failureReason = GenerationFailureReason.OpenSocketsExhausted;
                return false;
            }

            int idx = rng.Next(openSockets.Count);
            OpenSocket target = openSockets[idx];
            bool placedSomething = false;

            for (int attempt = 0; attempt < attemptsPerOpenSocket; attempt++)
            {
                bool canTryEndByRoomCount = placed.Count >= Mathf.Max(1, minRooms - 1);
                RoomDefinition candidate;
                if (!endPlaced && canTryEndByRoomCount)
                {
                    candidate = roomPicker.PickRoomWithBiasToEnd(
                        config.roomPool,
                        config.endRoom,
                        forceEnd: forceEndWhenEligible);
                }
                else
                {
                    candidate = roomPicker.PickNonEndRoom(config.roomPool, endPlaced);
                }

                diagnostics.CandidateAttempts++;
                if (candidate == null || candidate.prefab == null)
                {
                    diagnostics.RecordPlacementFailure(PlacementFailureReason.MissingRoomOrPrefab);
                    continue;
                }

                bool isEnd = !endPlaced && candidate == config.endRoom;
                Func<Vector3, bool> endValidator = null;
                if (isEnd && useDistanceRange)
                {
                    endValidator = endCenter =>
                    {
                        float distance = Vector3.Distance(startWorldPos, endCenter);
                        return distance >= minEndWorld && distance <= maxEndWorld;
                    };
                }

                if (roomPlacer.TryAttachRoom(
                        target,
                        candidate,
                        out PlacedRoom newPlaced,
                        out PlacementFailureReason placementFailure,
                        extraOverlapPadding: 0f,
                        overlapMaskToUse: config.roomOverlapMask,
                        placementCenterValidator: endValidator))
                {
                    openSockets.RemoveAt(idx);
                    placed.Add(newPlaced);
                    SocketUtils.AddOpenSocketsFromRoom(openSockets, newPlaced);

                    if (config.allowLoops && config.autoCloseMatchingSockets)
                    {
                        while (SocketUtils.CloseAnySocketPairsThatMeet(
                                   openSockets,
                                   config.centerSnapTolerance,
                                   config.forwardDotTolerance,
                                   config.widthToleranceFallback))
                        {
                        }
                    }

                    if (isEnd) endPlaced = true;
                    placedSomething = true;
                    break;
                }

                diagnostics.RecordPlacementFailure(placementFailure);
            }

            if (!placedSomething)
            {
                openSockets.RemoveAt(idx);
                socketsToCap.Add(target);
                diagnostics.UnfillableSockets++;
            }

            if (endPlaced && placed.Count >= minRooms)
            {
                metrics.actualRoomsBeforeCapping = placed.Count;

                if (config.capOpenSocketsAfterEnd)
                {
                    openSockets.AddRange(socketsToCap);
                    socketsToCap.Clear();

                    if (config.allowLoops && config.autoCloseMatchingSockets)
                    {
                        while (SocketUtils.CloseAnySocketPairsThatMeet(
                                   openSockets,
                                   config.centerSnapTolerance,
                                   config.forwardDotTolerance,
                                   config.widthToleranceFallback))
                        {
                        }
                    }

                    metrics.openSocketsBeforeCapping = openSockets.Count;
                    Debug.Log($"[PCG] Capping {openSockets.Count} remaining open socket(s).", this);
                    Stopwatch cappingStopwatch = Stopwatch.StartNew();
                    capping.CapAllOpenSockets(
                        openSockets: openSockets,
                        placedRooms: placed,
                        deadEndCache: deadEndCache,
                        wallCapRoom: config.wallCapRoom,
                        attemptsPerOpenSocket: attemptsPerOpenSocket,
                        maxCapsAfterEnd: config.maxCapsAfterEnd,
                        useDeadEndsToCap: config.useDeadEndsToCap,
                        capExtraPadding: config.capExtraPadding,
                        roomOverlapMask: config.roomOverlapMask,
                        preventCapOverlappingCaps: config.preventCapOverlappingCaps,
                        capOverlapMask: config.capOverlapMask,
                        log: config.log
                    );
                    cappingStopwatch.Stop();
                    metrics.cappingMs += cappingStopwatch.Elapsed.TotalMilliseconds;

                    CappingResult cappingResult = capping.LastResult;
                    metrics.capPlacements = cappingResult.TotalCaps;
                    metrics.deadEndCaps = cappingResult.DeadEndCaps;
                    metrics.wallCaps = cappingResult.WallCaps;
                    metrics.logicalOnlyCaps = cappingResult.LogicalClosures;
                    metrics.remainingOpenSocketsAfterCapping = cappingResult.RemainingOpenSockets;
                    metrics.actualRoomsAfterCapping = placed.Count;
                    Debug.Log($"[PCG] Capping complete. RemainingOpen={openSockets.Count}.", this);
                }
                else
                {
                    int unresolvedSockets = openSockets.Count + socketsToCap.Count;
                    metrics.openSocketsBeforeCapping = unresolvedSockets;
                    metrics.remainingOpenSocketsAfterCapping = unresolvedSockets;
                    metrics.actualRoomsAfterCapping = placed.Count;
                }

                return true;
            }
        }

        failureReason = placed.Count >= maxRooms
            ? GenerationFailureReason.RoomLimitReachedBeforeCompletion
            : GenerationFailureReason.SafetyLimitReached;
        return false;
    }

    private static void ClearChildren(Transform target)
    {
        for (int i = target.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(target.GetChild(i).gameObject);
        }
    }

    private void PopulateLayoutMetrics(PCGGenerationMetrics metrics)
    {
        if (metrics == null) return;

        metrics.actualRoomsAfterCapping = placed.Count;
        if (metrics.actualRoomsBeforeCapping <= 0)
            metrics.actualRoomsBeforeCapping = placed.Count;

        metrics.roomTypeFrequency.Clear();

        PlacedRoom startRoom = null;
        List<PlacedRoom> bossCandidates = new List<PlacedRoom>();
        int linearRooms = 0;
        int branchingRooms = 0;
        int nonCapRooms = 0;

        for (int i = 0; i < placed.Count; i++)
        {
            PlacedRoom room = placed[i];
            if (room == null) continue;

            string roomType = DetermineResearchRoomType(room);
            metrics.IncrementRoomType(roomType);

            if (!room.isCap)
            {
                nonCapRooms++;
                int socketCount = GetValidSocketCount(room);
                if (socketCount == 2) linearRooms++;
                else if (socketCount >= 3) branchingRooms++;
            }

            if (IsStartRoom(room))
                startRoom ??= room;

            if (GetSpecialKind(room.def) == PCGSpecialRoomKind.Boss)
                bossCandidates.Add(room);
        }

        metrics.branchingFactor = linearRooms > 0
            ? (float)branchingRooms / linearRooms
            : branchingRooms;

        Dictionary<PlacedRoom, int> distances = BuildGraphDistances(startRoom);
        PlacedRoom bossRoom = PickFarthestReachableRoom(bossCandidates, distances);

        if (bossRoom != null && distances.TryGetValue(bossRoom, out int bossDistance))
        {
            metrics.startToBossGraphDistance = bossDistance;
            metrics.criticalPathRooms = bossDistance + 1;
            metrics.sidePathRooms = Mathf.Max(0, nonCapRooms - metrics.criticalPathRooms);
        }
        else
        {
            metrics.startToBossGraphDistance = -1;
            metrics.criticalPathRooms = 0;
            metrics.sidePathRooms = nonCapRooms;
        }

        ValidateSpecialRoomPlacement(metrics, distances, bossRoom);
    }

    private string DetermineResearchRoomType(PlacedRoom room)
    {
        if (room == null) return PCGResearchRoomType.GenericRoom.ToString();
        if (room.isCap) return PCGResearchRoomType.Cap.ToString();

        RoomDefinition def = room.def;
        if (def != null && def.researchRoomType != PCGResearchRoomType.Auto)
            return def.researchRoomType.ToString();

        PCGSpecialRoomKind specialKind = GetSpecialKind(def);
        if (specialKind != PCGSpecialRoomKind.None)
            return specialKind.ToString();

        if (IsStartRoom(room)) return PCGResearchRoomType.Start.ToString();
        if (def != null && def.isDeadEnd) return PCGResearchRoomType.DeadEnd.ToString();
        if (LooksLikeBigRoom(room)) return PCGResearchRoomType.BigRoom.ToString();

        int socketCount = GetValidSocketCount(room);
        if (socketCount <= 1) return PCGResearchRoomType.DeadEnd.ToString();
        if (socketCount == 2) return IsCornerRoom(room)
            ? PCGResearchRoomType.Corner.ToString()
            : PCGResearchRoomType.Corridor.ToString();
        if (socketCount == 3) return PCGResearchRoomType.ThreeWay.ToString();
        if (socketCount >= 4) return PCGResearchRoomType.Crossway.ToString();

        if (def != null && def.isHallway) return PCGResearchRoomType.Corridor.ToString();
        return PCGResearchRoomType.GenericRoom.ToString();
    }

    private bool IsStartRoom(PlacedRoom room)
    {
        if (room == null) return false;
        if (room.def == config.startRoom) return true;
        return room.def != null && room.def.isStart;
    }

    private PCGSpecialRoomKind GetSpecialKind(RoomDefinition definition)
    {
        if (definition == null) return PCGSpecialRoomKind.None;
        if (definition.specialRoomKind != PCGSpecialRoomKind.None) return definition.specialRoomKind;
        if (definition == config.endRoom || definition.isEnd) return PCGSpecialRoomKind.Boss;
        return PCGSpecialRoomKind.None;
    }

    private static Dictionary<PlacedRoom, int> BuildGraphDistances(PlacedRoom startRoom)
    {
        Dictionary<PlacedRoom, int> distances = new Dictionary<PlacedRoom, int>();
        if (startRoom == null) return distances;

        Queue<PlacedRoom> queue = new Queue<PlacedRoom>();
        distances[startRoom] = 0;
        queue.Enqueue(startRoom);

        while (queue.Count > 0)
        {
            PlacedRoom current = queue.Dequeue();
            int nextDistance = distances[current] + 1;

            for (int i = 0; i < current.connectedRooms.Count; i++)
            {
                PlacedRoom next = current.connectedRooms[i];
                if (next == null || next.isCap || distances.ContainsKey(next)) continue;

                distances[next] = nextDistance;
                queue.Enqueue(next);
            }
        }

        return distances;
    }

    private static PlacedRoom PickFarthestReachableRoom(
        List<PlacedRoom> candidates,
        Dictionary<PlacedRoom, int> distances)
    {
        PlacedRoom best = null;
        int bestDistance = -1;

        if (candidates == null || distances == null)
            return null;

        for (int i = 0; i < candidates.Count; i++)
        {
            PlacedRoom candidate = candidates[i];
            if (candidate == null || !distances.TryGetValue(candidate, out int distance)) continue;
            if (distance <= bestDistance) continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    private void ValidateSpecialRoomPlacement(
        PCGGenerationMetrics metrics,
        Dictionary<PlacedRoom, int> distances,
        PlacedRoom bossRoom)
    {
        bool allValid = true;
        int invalidCount = 0;
        bool bossFound = bossRoom != null;
        StringBuilder details = new StringBuilder();
        metrics.specialRooms.Clear();

        for (int i = 0; i < placed.Count; i++)
        {
            PlacedRoom room = placed[i];
            if (room == null || room.isCap) continue;

            PCGSpecialRoomKind kind = GetSpecialKind(room.def);
            if (kind == PCGSpecialRoomKind.None) continue;

            int degree = CountNonCapConnections(room);
            bool requiresDeadEnd = room.def == null || room.def.specialRoomRequiresDeadEnd;
            bool deadEndValid = !requiresDeadEnd || degree <= 1;
            bool distanceValid = true;

            if (kind == PCGSpecialRoomKind.Boss)
            {
                bossFound = true;
                distanceValid = distances != null
                    && distances.TryGetValue(room, out int distance)
                    && distance >= config.minEndDistanceRooms;
            }

            bool roomValid = deadEndValid && distanceValid;
            int graphDistance = distances != null && distances.TryGetValue(room, out int knownDistance)
                ? knownDistance
                : -1;

            metrics.specialRooms.Add(new PCGSpecialRoomMetric
            {
                kind = kind.ToString(),
                roomId = room.def != null ? room.def.id : "Unknown",
                graphDegree = degree,
                graphDistance = graphDistance,
                requiresDeadEnd = requiresDeadEnd,
                deadEndValid = deadEndValid,
                distanceValid = distanceValid,
                valid = roomValid
            });

            if (!roomValid)
            {
                allValid = false;
                invalidCount++;
            }

            if (details.Length > 0) details.Append(';');
            details.Append(kind);
            details.Append('(');
            details.Append(room.def != null ? room.def.id : "Unknown");
            details.Append(",degree=");
            details.Append(degree);

            if (kind == PCGSpecialRoomKind.Boss)
            {
                details.Append(",path=");
                details.Append(graphDistance);
            }

            details.Append(",valid=");
            details.Append(roomValid ? "true" : "false");
            details.Append(')');
        }

        if (!bossFound)
        {
            allValid = false;
            invalidCount++;
            metrics.specialRooms.Add(new PCGSpecialRoomMetric
            {
                kind = PCGSpecialRoomKind.Boss.ToString(),
                roomId = "Missing",
                graphDegree = 0,
                graphDistance = -1,
                requiresDeadEnd = true,
                deadEndValid = false,
                distanceValid = false,
                valid = false
            });

            if (details.Length > 0) details.Append(';');
            details.Append("BossMissing(valid=false)");
        }

        metrics.specialRoomPlacementValid = allValid;
        metrics.invalidSpecialRoomCount = invalidCount;
        metrics.specialRoomValidation = details.Length > 0 ? details.ToString() : "none";
    }

    private static int CountNonCapConnections(PlacedRoom room)
    {
        if (room == null || room.connectedRooms == null) return 0;

        int count = 0;
        for (int i = 0; i < room.connectedRooms.Count; i++)
        {
            PlacedRoom connected = room.connectedRooms[i];
            if (connected != null && !connected.isCap)
                count++;
        }

        return count;
    }

    private static int GetValidSocketCount(PlacedRoom room)
    {
        if (room == null || room.root == null) return 0;

        SocketMarker[] markers = room.root.GetComponentsInChildren<SocketMarker>(true);
        int count = 0;
        for (int i = 0; i < markers.Length; i++)
        {
            SocketMarker marker = markers[i];
            if (marker == null || marker.left == null || marker.right == null) continue;
            count++;
        }

        return count;
    }

    private static bool IsCornerRoom(PlacedRoom room)
    {
        if (room == null || room.root == null) return false;

        SocketMarker[] markers = room.root.GetComponentsInChildren<SocketMarker>(true);
        List<Vector3> directions = new List<Vector3>(2);
        for (int i = 0; i < markers.Length; i++)
        {
            SocketMarker marker = markers[i];
            if (marker == null || marker.left == null || marker.right == null) continue;

            Vector3 forward = marker.ForwardWorld;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) continue;
            directions.Add(forward.normalized);
            if (directions.Count == 2) break;
        }

        if (directions.Count != 2) return false;
        return Vector3.Dot(directions[0], directions[1]) > -0.75f;
    }

    private static bool LooksLikeBigRoom(PlacedRoom room)
    {
        if (room == null) return false;

        RoomDefinition def = room.def;
        if (def != null)
        {
            if (ContainsIgnoreCase(def.id, "big")) return true;
            if (def.prefab != null && ContainsIgnoreCase(def.prefab.name, "big")) return true;
        }

        if (room.root != null && ContainsIgnoreCase(room.root.name, "big"))
            return true;

        SocketMarker[] markers = room.root != null
            ? room.root.GetComponentsInChildren<SocketMarker>(true)
            : Array.Empty<SocketMarker>();

        for (int i = 0; i < markers.Length; i++)
        {
            if (markers[i] != null && markers[i].type == SocketType.BigDoor)
                return true;
        }

        return false;
    }

    private static bool ContainsIgnoreCase(string value, string search)
    {
        return !string.IsNullOrEmpty(value)
            && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddCounts<T>(
        Dictionary<string, int> target,
        IReadOnlyDictionary<T, int> source)
    {
        if (target == null || source == null) return;

        foreach (KeyValuePair<T, int> pair in source)
        {
            PCGGenerationMetrics.Increment(target, pair.Key.ToString(), pair.Value);
        }
    }

    private void BuildDeadEndCache(List<RoomDefinition> pool)
    {
        deadEndCache.Clear();
        for (int i = 0; i < pool.Count; i++)
        {
            RoomDefinition room = pool[i];
            if (room == null || room.prefab == null) continue;
            if (room.isDeadEnd) deadEndCache.Add(room);
        }
    }

    private static float ComputeRoomUnitWorldFromBounds(GameObject roomRoot)
    {
        Transform boundsTransform = roomRoot.transform.Find("Bounds");
        if (boundsTransform != null)
        {
            BoxCollider bounds = boundsTransform.GetComponent<BoxCollider>();
            if (bounds != null)
            {
                Vector3 size = Vector3.Scale(bounds.size, bounds.transform.lossyScale);
                float unit = Mathf.Max(size.x, size.z);
                return Mathf.Max(0.01f, unit);
            }
        }

        return 4.5f;
    }

    private static Vector3 GetStartCenterWorld(GameObject startGO)
    {
        return OverlapChecker.TryGetBoundsCenter(startGO, out Vector3 center)
            ? center
            : startGO.transform.position;
    }

    private enum GenerationFailureReason
    {
        None,
        InvalidSetup,
        AlreadyGenerating,
        OpenSocketsExhausted,
        OpenSocketsRemainingAfterCapping,
        RoomLimitReachedBeforeCompletion,
        SafetyLimitReached
    }

    private sealed class GenerationAttemptDiagnostics
    {
        private readonly Dictionary<PlacementFailureReason, int> placementFailures = new();

        public readonly int Seed;
        public int CandidateAttempts;
        public int UnfillableSockets;
        public IReadOnlyDictionary<PlacementFailureReason, int> PlacementFailures => placementFailures;

        public GenerationAttemptDiagnostics(int seed)
        {
            Seed = seed;
        }

        public void RecordPlacementFailure(PlacementFailureReason reason)
        {
            if (reason == PlacementFailureReason.None) return;
            Increment(placementFailures, reason, 1);
        }

        public string FormatAttempt(GenerationFailureReason failureReason, int roomCount, int openSocketCount)
        {
            return
                $"Seed={Seed}, Reason={failureReason}, Rooms={roomCount}, OpenSockets={openSocketCount}, " +
                $"UnfillableSockets={UnfillableSockets}, CandidateAttempts={CandidateAttempts}, " +
                $"PlacementFailures=[{FormatCounts(placementFailures)}]";
        }
    }

    private sealed class GenerationRunDiagnostics
    {
        private readonly Dictionary<GenerationFailureReason, int> generationFailures = new();
        private readonly Dictionary<PlacementFailureReason, int> placementFailures = new();
        private int candidateAttempts;
        private int unfillableSockets;

        public void Record(GenerationFailureReason reason, GenerationAttemptDiagnostics attempt)
        {
            Increment(generationFailures, reason, 1);
            candidateAttempts += attempt.CandidateAttempts;
            unfillableSockets += attempt.UnfillableSockets;

            foreach (KeyValuePair<PlacementFailureReason, int> pair in attempt.PlacementFailures)
            {
                Increment(placementFailures, pair.Key, pair.Value);
            }
        }

        public string FormatSummary()
        {
            return
                $"FailureReasons=[{FormatCounts(generationFailures)}], " +
                $"PlacementFailures=[{FormatCounts(placementFailures)}], " +
                $"CandidateAttempts={candidateAttempts}, UnfillableSockets={unfillableSockets}.";
        }
    }

    private static void Increment<T>(Dictionary<T, int> counts, T key, int amount)
    {
        if (counts.TryGetValue(key, out int current))
        {
            counts[key] = current + amount;
        }
        else
        {
            counts.Add(key, amount);
        }
    }

    private static string FormatCounts<T>(Dictionary<T, int> counts)
    {
        if (counts.Count == 0) return "none";

        var builder = new StringBuilder();
        foreach (T value in Enum.GetValues(typeof(T)))
        {
            if (!counts.TryGetValue(value, out int count)) continue;
            if (builder.Length > 0) builder.Append(", ");
            builder.Append(value);
            builder.Append('=');
            builder.Append(count);
        }

        return builder.ToString();
    }
}
