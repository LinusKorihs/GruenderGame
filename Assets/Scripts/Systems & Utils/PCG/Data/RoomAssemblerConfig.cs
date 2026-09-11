using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SO/PCG/Base/Room Assembler Config", fileName = "RoomAssemblerConfig")]
public class RoomAssemblerConfig : ScriptableObject
{
    public const int MinimumEmergencyRooms = 5;

    [Header("Rooms")]
    [Tooltip("First room placed at world origin. Its Bounds can be used to auto-calculate room unit size.")]
    public RoomDefinition startRoom;

    [Tooltip("Target room that ends generation once placement constraints are satisfied.")]
    public RoomDefinition endRoom;

    [Tooltip("Main weighted pool for generation attempts. Usually contains normal rooms and optionally the end room.")]
    public List<RoomDefinition> roomPool = new();

    [Header("Wall Cap (Fallback)")]
    [Tooltip("Simple cap prefab used when no dead-end room can close an open socket.")]
    public RoomDefinition wallCapRoom;

    [Header("Room Count")]
    [Tooltip("Minimum number of placed rooms required before generation may finish.")]
    [HideInInspector]
    [Min(1)] public int minRooms = 20;

    [Tooltip("Hard limit for placed rooms during growth.")]
    [HideInInspector]
    [Min(1)] public int maxRooms = 50;

    [Header("End Distance (in Rooms)")]
    [Tooltip("Distance measured in multiples of one room-size (derived from Start Bounds unless overridden).")]
    [HideInInspector]
    public bool useEndDistanceRange = true;

    [Tooltip("Minimum allowed start-to-end distance in room units when distance range is enabled.")]
    [HideInInspector]
    [Min(0)] public int minEndDistanceRooms = 5;

    [Tooltip("Maximum allowed start-to-end distance in room units when distance range is enabled.")]
    [HideInInspector]
    [Min(0)] public int maxEndDistanceRooms = 10;

    [Tooltip("0 = auto from Start Bounds (XZ max). Otherwise overrides size of 1 'room' in world units.")]
    [HideInInspector]
    [Min(0f)] public float roomUnitWorldOverride = 0f;

    [Header("Attempts")]
    [Tooltip("How many candidate rooms are tried for a selected open socket before giving up on that socket.")]
    [HideInInspector]
    [Min(1)] public int attemptsPerOpenSocket = 50;

    [Tooltip("How many full generation retries if constraints fail.")]
    [HideInInspector]
    [Min(1)] public int maxGenerationRetries = 5;

    [Header("Emergency Fallback")]
    [Tooltip("After normal retries fail, run a bounded set of easier attempts so the player is less likely to receive no level.")]
    [HideInInspector]
    public bool useEmergencyFallback = true;

    [Tooltip("Maximum number of easier full-layout attempts. Generation still stops after this limit.")]
    [HideInInspector]
    [Min(1)] public int emergencyFallbackRetries = 5;

    [Tooltip("Minimum room count allowed only during emergency fallback attempts.")]
    [HideInInspector]
    [Min(MinimumEmergencyRooms)] public int emergencyMinimumRooms = MinimumEmergencyRooms;

    [Tooltip("Extra room capacity allowed only during emergency fallback attempts.")]
    [HideInInspector]
    [Min(0)] public int emergencyAdditionalMaxRooms = 15;

    [Tooltip("Ignore the configured start-to-end distance range during emergency fallback attempts.")]
    [HideInInspector]
    public bool emergencyIgnoreEndDistance = true;

    [Tooltip("Always try the end room once the emergency minimum room count can be reached.")]
    [HideInInspector]
    public bool emergencyForceEndRoom = true;

    [Header("Loops")]
    [Tooltip("Allow generated layout loops instead of only tree-like expansion.")]
    [HideInInspector]
    public bool allowLoops = true;

    [Tooltip("When enabled, nearby compatible open sockets are auto-closed to form loop connections.")]
    [HideInInspector]
    public bool autoCloseMatchingSockets = true;

    [Header("Socket Matching")]
    [Tooltip("Maximum center distance (world units) for considering two sockets as the same opening.")]
    [HideInInspector]
    public float centerSnapTolerance = 0.02f;

    [Tooltip("Minimum dot product for opposite-facing sockets. Values near 1 require near-perfect alignment.")]
    [HideInInspector]
    public float forwardDotTolerance = 0.95f;

    [Tooltip("Fallback width tolerance used for socket matching if room-specific tolerance is unavailable.")]
    [HideInInspector]
    public float widthToleranceFallback = 0.05f;

    [Header("Overlap / Layers")]
    [Tooltip("Layer mask used to detect overlaps against already generated rooms.")]
    public LayerMask roomOverlapMask;

    [Tooltip("Layer mask that identifies cap objects. Used when preventing cap-vs-cap overlap.")]
    public LayerMask capOverlapMask;

    [Tooltip("Base shrink padding applied to overlap checks to avoid borderline collider contacts.")]
    [HideInInspector]
    public float overlapPadding = 0.02f;

    [Header("Capping")]
    [Tooltip("After placing the end room, try to close remaining open sockets with cap content.")]
    [HideInInspector]
    public bool capOpenSocketsAfterEnd = true;

    [Tooltip("Prefer dead-end room definitions as caps before falling back to wall caps.")]
    [HideInInspector]
    public bool useDeadEndsToCap = true;
    
    [Tooltip("Maximum number of cap placements attempted after the end is reached.")]
    [HideInInspector]
    [Min(0)] public int maxCapsAfterEnd = 999;

    [Tooltip("Additional overlap padding used only while placing caps.")]
    [HideInInspector]
    public float capExtraPadding = 0.03f;

    [Tooltip("If enabled, cap placement also checks collisions against other caps (via capOverlapMask).")]
    [HideInInspector]
    public bool preventCapOverlappingCaps = true;

    [Header("Wall Cap Placement")]
    [Tooltip("Moves fallback wall caps backward along socket forward axis. Useful to recess the cap into the opening.")]
    [HideInInspector]
    public float wallCapInset = 0.0f;

    [Header("Seed")]
    [Tooltip("Use a different seed on each run. If disabled, generation is deterministic from the Seed value.")]
    [HideInInspector]
    public bool randomSeed = true;

    [Tooltip("Deterministic seed used when Random Seed is disabled.")]
    [HideInInspector]
    public int seed = 12345;

    [Header("Debug")]
    [Tooltip("Print detailed generation and placement logs to the Console.")]
    [HideInInspector]
    public bool log = false;

    private void OnValidate()
    {
        emergencyMinimumRooms = Mathf.Max(MinimumEmergencyRooms, emergencyMinimumRooms);
        emergencyFallbackRetries = Mathf.Max(1, emergencyFallbackRetries);
        emergencyAdditionalMaxRooms = Mathf.Max(0, emergencyAdditionalMaxRooms);
    }
}
