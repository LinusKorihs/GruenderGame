using System;
using System.Collections.Generic;
using PCG.RoomAssembler.Data;
using UnityEngine;

public sealed class StaticLevelLayoutBuilder : MonoBehaviour
{
    private const string PCGRootName = "PCG_Root";
    private const string GeneratedLevelRootName = "Generated_Level";
    private const string RoomsRootName = "Rooms";
    private const string ContentRootName = "Content";

    private readonly List<PlacedRoom> placedRooms = new List<PlacedRoom>();

    public IReadOnlyList<PlacedRoom> LastPlacedRooms => placedRooms;
    public GameObject LastPlayer { get; private set; }
    public GameObject LastBoss { get; private set; }

    public bool Build(StaticLevelLayoutProfile profile)
    {
        LastPlayer = null;
        LastBoss = null;
        placedRooms.Clear();

        if (profile == null)
        {
            Debug.LogWarning("[Static Layout] Cannot build because no profile is assigned.", this);
            return false;
        }

        ResolveRuntimeRoots(out Transform roomsRoot, out Transform contentRoot);
        if (roomsRoot == null || contentRoot == null)
        {
            Debug.LogWarning("[Static Layout] Cannot build because runtime roots are missing.", this);
            return false;
        }

        if (profile.clearBeforeBuild)
        {
            ClearChildren(roomsRoot);
            ClearContentChildren(contentRoot);
        }

        if (profile.rooms == null || profile.rooms.Count == 0)
        {
            Debug.LogWarning($"[Static Layout] '{profile.name}' has no rooms.", this);
            return false;
        }

        for (int i = 0; i < profile.rooms.Count; i++)
        {
            StaticLevelRoomEntry entry = profile.rooms[i];
            if (!TryPlaceRoom(entry, i, roomsRoot, out PlacedRoom placedRoom))
                return false;

            placedRooms.Add(placedRoom);
        }

        if (profile.capOpenSockets)
        {
            CapOpenSockets(profile, roomsRoot);
        }

        LastPlayer = SpawnActor(
            profile.spawnPlayer,
            profile.reuseExistingPlayer,
            profile.playerPrefab,
            profile.playerRoomId,
            PCGSpawnPointKind.Player,
            profile.playerFallbackLocalPosition,
            contentRoot,
            "Player");

        LastBoss = SpawnActor(
            profile.spawnBoss,
            false,
            profile.bossPrefab,
            profile.bossRoomId,
            PCGSpawnPointKind.Enemy,
            profile.bossFallbackLocalPosition,
            contentRoot,
            "Boss");

        FaceActorTowards(LastBoss, LastPlayer);

        if (profile.buildNavMesh)
        {
            RuntimeNavMeshBuilder navMeshBuilder = GetComponentInChildren<RuntimeNavMeshBuilder>(true);
            if (navMeshBuilder != null)
            {
                navMeshBuilder.Build(roomsRoot);
            }
        }

        Debug.Log($"[Static Layout] Built '{profile.name}' with {placedRooms.Count} room(s).", this);
        return true;
    }

    private bool TryPlaceRoom(StaticLevelRoomEntry entry, int index, Transform roomsRoot, out PlacedRoom placedRoom)
    {
        placedRoom = null;

        if (entry == null || entry.roomDefinition == null || entry.roomDefinition.prefab == null)
        {
            Debug.LogWarning($"[Static Layout] Room entry {index} is missing a RoomDefinition or prefab.", this);
            return false;
        }

        GameObject roomObject = Instantiate(entry.roomDefinition.prefab, roomsRoot);
        roomObject.name = string.IsNullOrWhiteSpace(entry.roomId) ? entry.roomDefinition.id : entry.roomId;
        roomObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        roomObject.transform.localScale = entry.scale == Vector3.zero ? Vector3.one : entry.scale;

        placedRoom = new PlacedRoom(entry.roomDefinition, roomObject);

        if (index == 0)
        {
            return true;
        }

        PlacedRoom targetRoom = FindPlacedRoom(entry.attachToRoomId) ?? placedRooms[placedRooms.Count - 1];
        SocketMarker targetSocket = FindSocket(targetRoom.root, entry.targetSocketName, targetRoom.connectedSocketInstanceIds);
        SocketMarker roomSocket = FindSocket(roomObject, entry.roomSocketName, placedRoom.connectedSocketInstanceIds);

        if (targetSocket == null || roomSocket == null)
        {
            Debug.LogWarning(
                $"[Static Layout] Cannot attach '{roomObject.name}'. " +
                $"TargetSocket={(targetSocket != null ? targetSocket.name : "missing")}, " +
                $"RoomSocket={(roomSocket != null ? roomSocket.name : "missing")}.",
                this);
            DestroyRuntime(roomObject);
            return false;
        }

        AlignRoomSocketToTarget(roomObject.transform, roomSocket, targetSocket);
        placedRoom.connectedSocketInstanceIds.Add(roomSocket.GetInstanceID());
        targetRoom.connectedSocketInstanceIds.Add(targetSocket.GetInstanceID());
        placedRoom.ConnectTo(targetRoom);
        return true;
    }

    private void CapOpenSockets(StaticLevelLayoutProfile profile, Transform roomsRoot)
    {
        if (profile.wallCapRoom == null || profile.wallCapRoom.prefab == null)
        {
            Debug.LogWarning($"[Static Layout] '{profile.name}' cannot cap sockets because no WallCap room is assigned.", this);
            return;
        }

        for (int i = 0; i < placedRooms.Count; i++)
        {
            PlacedRoom room = placedRooms[i];
            SocketMarker[] sockets = room.root.GetComponentsInChildren<SocketMarker>(true);
            for (int s = 0; s < sockets.Length; s++)
            {
                SocketMarker socket = sockets[s];
                if (socket == null || socket.left == null || socket.right == null)
                    continue;

                if (room.connectedSocketInstanceIds.Contains(socket.GetInstanceID()))
                    continue;

                GameObject cap = Instantiate(profile.wallCapRoom.prefab, roomsRoot);
                cap.name = $"CAP_{profile.wallCapRoom.id}_{room.root.name}_{socket.name}";
                Vector3 prefabScale = cap.transform.localScale;
                cap.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

                if (profile.matchCapScaleToRoom)
                {
                    cap.transform.localScale = Vector3.Scale(prefabScale, room.root.transform.localScale);
                }

                if (profile.wallCapScaleOverride != Vector3.zero)
                {
                    cap.transform.localScale = profile.wallCapScaleOverride;
                }

                PlaceCap(cap.transform, socket, profile.wallCapInset);
                room.connectedSocketInstanceIds.Add(socket.GetInstanceID());
            }
        }
    }

    private GameObject SpawnActor(
        bool shouldSpawn,
        bool reuseExisting,
        GameObject prefab,
        string roomId,
        PCGSpawnPointKind spawnKind,
        Vector3 fallbackLocalPosition,
        Transform contentRoot,
        string actorName)
    {
        if (!shouldSpawn)
            return null;

        if (reuseExisting)
        {
            GameObject existing = FindExistingPlayer();
            if (existing != null)
            {
                Transform target = ResolveSpawnTransform(roomId, spawnKind, fallbackLocalPosition);
                existing.transform.SetPositionAndRotation(target.position, target.rotation);
                return existing;
            }
        }

        if (prefab == null)
        {
            Debug.LogWarning($"[Static Layout] Cannot spawn {actorName} because no prefab is assigned.", this);
            return null;
        }

        Transform spawn = ResolveSpawnTransform(roomId, spawnKind, fallbackLocalPosition);
        GameObject actor = Instantiate(prefab, spawn.position, spawn.rotation, contentRoot);
        actor.name = actorName;
        actor.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        return actor;
    }

    private static void FaceActorTowards(GameObject actor, GameObject target)
    {
        if (actor == null || target == null)
            return;

        Vector3 direction = target.transform.position - actor.transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            return;

        actor.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private Transform ResolveSpawnTransform(string roomId, PCGSpawnPointKind spawnKind, Vector3 fallbackLocalPosition)
    {
        PlacedRoom room = FindPlacedRoom(roomId);
        if (room == null && placedRooms.Count > 0)
        {
            room = placedRooms[0];
        }

        if (room != null && room.root != null)
        {
            PCGSpawnPoint[] spawnPoints = room.root.GetComponentsInChildren<PCGSpawnPoint>(true);
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] != null && spawnPoints[i].kind == spawnKind)
                {
                    return spawnPoints[i].transform;
                }
            }

            GameObject fallback = new GameObject($"{room.root.name}_{spawnKind}_FallbackSpawn");
            fallback.transform.SetParent(room.root.transform, false);
            fallback.transform.localPosition = fallbackLocalPosition;
            fallback.transform.localRotation = Quaternion.identity;
            return fallback.transform;
        }

        GameObject worldFallback = new GameObject($"{spawnKind}_FallbackSpawn");
        worldFallback.transform.SetParent(transform, false);
        worldFallback.transform.localPosition = fallbackLocalPosition;
        worldFallback.transform.localRotation = Quaternion.identity;
        return worldFallback.transform;
    }

    private static void AlignRoomSocketToTarget(Transform roomTransform, SocketMarker roomSocket, SocketMarker targetSocket)
    {
        Vector3 roomForward = Flatten(roomSocket.ForwardWorld);
        Vector3 targetForward = Flatten(-targetSocket.ForwardWorld);

        if (roomForward.sqrMagnitude > 0.0001f && targetForward.sqrMagnitude > 0.0001f)
        {
            float yaw = Vector3.SignedAngle(roomForward.normalized, targetForward.normalized, Vector3.up);
            roomTransform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * roomTransform.rotation;
        }

        Vector3 offset = targetSocket.CenterWorld - roomSocket.CenterWorld;
        roomTransform.position += offset;
    }

    private static void PlaceCap(Transform capTransform, SocketMarker targetSocket, float inset)
    {
        Vector3 forward = Flatten(targetSocket.ForwardWorld);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        float yaw = Mathf.Abs(forward.x) > Mathf.Abs(forward.z) ? 90f : 0f;
        capTransform.SetPositionAndRotation(
            targetSocket.CenterWorld - forward * Mathf.Max(0f, inset),
            Quaternion.Euler(0f, yaw, 0f));

        if (TryGetBoundsWorldGeometry(capTransform.gameObject, out Vector3 currentBoundsCenter, out float boundsHalfHeight))
        {
            Vector3 desiredBoundsCenter = targetSocket.CenterWorld - forward * Mathf.Max(0f, inset);
            desiredBoundsCenter.y += boundsHalfHeight;
            capTransform.position += desiredBoundsCenter - currentBoundsCenter;
        }
    }

    private SocketMarker FindSocket(GameObject root, string preferredName, HashSet<int> connected)
    {
        SocketMarker[] sockets = root.GetComponentsInChildren<SocketMarker>(true);
        SocketMarker fallback = null;

        for (int i = 0; i < sockets.Length; i++)
        {
            SocketMarker socket = sockets[i];
            if (socket == null || socket.left == null || socket.right == null || connected.Contains(socket.GetInstanceID()))
                continue;

            if (fallback == null)
            {
                fallback = socket;
            }

            if (string.IsNullOrWhiteSpace(preferredName))
                continue;

            if (string.Equals(socket.name, preferredName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(socket.socketId, preferredName, StringComparison.OrdinalIgnoreCase))
            {
                return socket;
            }
        }

        return fallback;
    }

    private PlacedRoom FindPlacedRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            return null;

        for (int i = 0; i < placedRooms.Count; i++)
        {
            PlacedRoom room = placedRooms[i];
            if (room?.root == null)
                continue;

            if (string.Equals(room.root.name, roomId, StringComparison.OrdinalIgnoreCase))
                return room;
        }

        return null;
    }

    private void ResolveRuntimeRoots(out Transform roomsRoot, out Transform contentRoot)
    {
        Transform pcgRoot = GetOrCreateChild(transform, PCGRootName);
        Transform generatedRoot = GetOrCreateChild(pcgRoot, GeneratedLevelRootName);
        roomsRoot = GetOrCreateChild(generatedRoot, RoomsRootName);
        contentRoot = GetOrCreateChild(generatedRoot, ContentRootName);
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject childObject = new GameObject(childName);
        child = childObject.transform;
        child.SetParent(parent, false);
        return child;
    }

    private static GameObject FindExistingPlayer()
    {
        try
        {
            return GameObject.FindGameObjectWithTag("Player");
        }
        catch
        {
            return null;
        }
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static bool TryGetBoundsWorldGeometry(GameObject candidate, out Vector3 center, out float halfHeight)
    {
        center = candidate.transform.position;
        halfHeight = 0f;

        Transform boundsTransform = candidate.transform.Find("Bounds");
        if (boundsTransform == null) return false;

        BoxCollider bounds = boundsTransform.GetComponent<BoxCollider>();
        if (bounds == null || !bounds.enabled) return false;

        center = bounds.transform.TransformPoint(bounds.center);

        Vector3 localHalf = bounds.size * 0.5f;
        Vector3 worldX = bounds.transform.TransformVector(localHalf.x, 0f, 0f);
        Vector3 worldY = bounds.transform.TransformVector(0f, localHalf.y, 0f);
        Vector3 worldZ = bounds.transform.TransformVector(0f, 0f, localHalf.z);
        halfHeight = Mathf.Abs(worldX.y) + Mathf.Abs(worldY.y) + Mathf.Abs(worldZ.y);
        return true;
    }

    private static void ClearChildren(Transform target)
    {
        for (int i = target.childCount - 1; i >= 0; i--)
        {
            DestroyRuntime(target.GetChild(i).gameObject);
        }
    }

    private static void ClearContentChildren(Transform target)
    {
        for (int i = target.childCount - 1; i >= 0; i--)
        {
            Transform child = target.GetChild(i);
            if (IsGeneratedContentCategoryRoot(child))
            {
                child.gameObject.SetActive(true);
                ClearChildren(child);
                continue;
            }

            DestroyRuntime(child.gameObject);
        }
    }

    private static bool IsGeneratedContentCategoryRoot(Transform candidate)
    {
        if (candidate == null)
            return false;

        return string.Equals(candidate.name, "Player", StringComparison.Ordinal)
            || string.Equals(candidate.name, "Minions", StringComparison.Ordinal)
            || string.Equals(candidate.name, "Enemies", StringComparison.Ordinal)
            || string.Equals(candidate.name, "Items", StringComparison.Ordinal);
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
}
