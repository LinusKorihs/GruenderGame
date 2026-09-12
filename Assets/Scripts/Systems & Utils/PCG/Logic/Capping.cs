using System.Collections.Generic;
using UnityEngine;
using PCG.RoomAssembler.Data;

namespace PCG.RoomAssembler.Logic
{
    public class Capping
    {
        private readonly RoomPicker roomPicker;
        private readonly RoomPlacer roomPlacer;

        public CappingResult LastResult { get; private set; }

        public Capping(RoomPicker roomPicker, RoomPlacer roomPlacer)
        {
            this.roomPicker = roomPicker;
            this.roomPlacer = roomPlacer;
        }

        public int CapAllOpenSockets(
            List<OpenSocket> openSockets,
            List<PlacedRoom> placedRooms,
            List<RoomDefinition> deadEndCache,
            RoomDefinition wallCapRoom,
            int attemptsPerOpenSocket,
            int maxCapsAfterEnd,
            bool useDeadEndsToCap,
            float capExtraPadding,
            LayerMask roomOverlapMask,
            bool preventCapOverlappingCaps,
            LayerMask capOverlapMask,
            bool log = false)
        {
            int caps = 0;
            int deadEndCaps = 0;
            int wallCaps = 0;
            int logicalClosures = 0;

            for (int i = openSockets.Count - 1; i >= 0; i--)
            {
                if (caps >= maxCapsAfterEnd) break;

                var target = openSockets[i];
                bool capped = false;

                // 1) Try dead ends (these are "caps", we don't add their sockets)
                if (useDeadEndsToCap && deadEndCache != null && deadEndCache.Count > 0)
                {
                    LayerMask capCheckMask = roomOverlapMask;
                    if (preventCapOverlappingCaps)
                        capCheckMask |= capOverlapMask;

                    for (int attempt = 0; attempt < attemptsPerOpenSocket; attempt++)
                    {
                        var dead = roomPicker.PickAny(deadEndCache);
                        if (dead == null || dead.prefab == null) continue;

                        if (roomPlacer.TryAttachRoom(
                                target,
                                dead,
                                out var newPlaced,
                                out _,
                                extraOverlapPadding: capExtraPadding,
                                overlapMaskToUse: capCheckMask))
                        {
                            openSockets.RemoveAt(i);
                            newPlaced.isCap = true;
                            placedRooms.Add(newPlaced);

                            caps++;
                            deadEndCaps++;
                            capped = true;
                            break;
                        }
                    }
                }

                if (capped) continue;

                // 2) Fallback wall cap
                if (roomPlacer.TryPlaceWallCapFallback(
                        target: target,
                        wallCapRoom: wallCapRoom,
                        capExtraPadding: capExtraPadding,
                        roomOverlapMask: roomOverlapMask,
                        preventCapOverlappingCaps: preventCapOverlappingCaps,
                        capOverlapMask: capOverlapMask,
                        failureReason: out WallCapFailureReason wallFailure,
                        blockingCollider: out Collider blockingCollider))
                {
                    target.owner.connectedSocketInstanceIds.Add(target.marker.GetInstanceID());
                    openSockets.RemoveAt(i);
                    caps++;
                    wallCaps++;
                    capped = true;
                }

                if (!capped)
                {
                    string blocker = blockingCollider != null
                        ? $"{blockingCollider.name} (layer={LayerMask.LayerToName(blockingCollider.gameObject.layer)})"
                        : "none";

                    if (log)
                    {
                        Debug.LogWarning(
                            $"[PCG Capping] Wall cap failed at {target.marker.name}. " +
                            $"Reason={wallFailure}, BlockingCollider={blocker}.",
                            target.marker);
                    }

                    // Keep generation state consistent, but report that no physical cap exists.
                    target.owner.connectedSocketInstanceIds.Add(target.marker.GetInstanceID());
                    openSockets.RemoveAt(i);
                    logicalClosures++;
                }
            }

            if (log)
            {
                Debug.Log(
                    $"[PCG Capping] DeadEnds={deadEndCaps}, Walls={wallCaps}, " +
                    $"LogicalOnly={logicalClosures}, RemainingOpen={openSockets.Count}.");
            }

            LastResult = new CappingResult
            {
                TotalCaps = caps,
                DeadEndCaps = deadEndCaps,
                WallCaps = wallCaps,
                LogicalClosures = logicalClosures,
                RemainingOpenSockets = openSockets.Count
            };

            if (log && (logicalClosures > 0 || openSockets.Count > 0))
            {
                Debug.LogWarning(
                    $"[PCG Capping] {logicalClosures} socket(s) were closed only logically and " +
                    $"{openSockets.Count} socket(s) remain open. Check the wall-cap prefab Bounds, " +
                    "cap overlap masks, and maxCapsAfterEnd.");
            }

            return caps;
        }
    }

    public struct CappingResult
    {
        public int TotalCaps;
        public int DeadEndCaps;
        public int WallCaps;
        public int LogicalClosures;
        public int RemainingOpenSockets;
    }
}
