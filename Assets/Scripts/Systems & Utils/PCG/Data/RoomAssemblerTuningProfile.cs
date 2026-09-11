using UnityEngine;

[CreateAssetMenu(menuName = "SO/PCG/Profiles/Tuning/Room Assembler Tuning Profile", fileName = "RoomAssemblerTuningProfile")]
public class RoomAssemblerTuningProfile : ScriptableObject
{
    [Header("Room Creation")]
    [Tooltip("Minimum number of placed rooms required before generation may finish.")]
    [Min(1)] public int minRooms = 20;

    [Tooltip("Hard limit for placed rooms during growth.")]
    [Min(1)] public int maxRooms = 50;

    [Header("Level Progression")]
    public bool useEndDistanceRange = true;
    [Min(0)] public int minEndDistanceRooms = 5;
    [Min(0)] public int maxEndDistanceRooms = 10;
    [Min(0f)] public float roomUnitWorldOverride = 0f;

    [Header("Attempts")]
    [Min(1)] public int attemptsPerOpenSocket = 50;
    [Min(1)] public int maxGenerationRetries = 5;

    [Header("Emergency Fallback")]
    public bool useEmergencyFallback = true;
    [Min(1)] public int emergencyFallbackRetries = 5;
    [Min(RoomAssemblerConfig.MinimumEmergencyRooms)] public int emergencyMinimumRooms = RoomAssemblerConfig.MinimumEmergencyRooms;
    [Min(0)] public int emergencyAdditionalMaxRooms = 15;
    public bool emergencyIgnoreEndDistance = true;
    public bool emergencyForceEndRoom = true;

    [Header("Loops")]
    public bool allowLoops = true;
    public bool autoCloseMatchingSockets = true;

    [Header("Socket Matching")]
    [Min(0f)] public float centerSnapTolerance = 0.02f;
    [Range(-1f, 1f)] public float forwardDotTolerance = 0.95f;
    [Min(0f)] public float widthToleranceFallback = 0.05f;

    [Header("Overlap")]
    [Min(0f)] public float overlapPadding = 0.02f;

    [Header("Capping")]
    public bool capOpenSocketsAfterEnd = true;
    public bool useDeadEndsToCap = true;
    [Min(0)] public int maxCapsAfterEnd = 999;
    [Min(0f)] public float capExtraPadding = 0.03f;
    public bool preventCapOverlappingCaps = true;

    [Header("Wall Cap Placement")]
    public float wallCapInset = 0f;

    [Header("Seed")]
    public bool randomSeed = true;
    public int seed = 12345;

    [Header("Debug")]
    public bool log;

    public void ApplyTo(RoomAssemblerConfig target, Object logContext = null)
    {
        if (target == null)
        {
            Debug.LogWarning("[Level Profile] Room assembler tuning skipped because the target config is missing.", logContext != null ? logContext : this);
            return;
        }

        WarnIfInvalid(logContext != null ? logContext : this);

        target.minRooms = Mathf.Max(1, minRooms);
        target.maxRooms = Mathf.Max(target.minRooms, maxRooms);

        target.useEndDistanceRange = useEndDistanceRange;
        target.minEndDistanceRooms = Mathf.Max(0, minEndDistanceRooms);
        target.maxEndDistanceRooms = Mathf.Max(target.minEndDistanceRooms, maxEndDistanceRooms);
        target.roomUnitWorldOverride = Mathf.Max(0f, roomUnitWorldOverride);

        target.attemptsPerOpenSocket = Mathf.Max(1, attemptsPerOpenSocket);
        target.maxGenerationRetries = Mathf.Max(1, maxGenerationRetries);

        target.useEmergencyFallback = useEmergencyFallback;
        target.emergencyFallbackRetries = Mathf.Max(1, emergencyFallbackRetries);
        target.emergencyMinimumRooms = Mathf.Max(RoomAssemblerConfig.MinimumEmergencyRooms, emergencyMinimumRooms);
        target.emergencyAdditionalMaxRooms = Mathf.Max(0, emergencyAdditionalMaxRooms);
        target.emergencyIgnoreEndDistance = emergencyIgnoreEndDistance;
        target.emergencyForceEndRoom = emergencyForceEndRoom;

        target.allowLoops = allowLoops;
        target.autoCloseMatchingSockets = autoCloseMatchingSockets;

        target.centerSnapTolerance = Mathf.Max(0f, centerSnapTolerance);
        target.forwardDotTolerance = Mathf.Clamp(forwardDotTolerance, -1f, 1f);
        target.widthToleranceFallback = Mathf.Max(0f, widthToleranceFallback);

        target.overlapPadding = Mathf.Max(0f, overlapPadding);

        target.capOpenSocketsAfterEnd = capOpenSocketsAfterEnd;
        target.useDeadEndsToCap = useDeadEndsToCap;
        target.maxCapsAfterEnd = Mathf.Max(0, maxCapsAfterEnd);
        target.capExtraPadding = Mathf.Max(0f, capExtraPadding);
        target.preventCapOverlappingCaps = preventCapOverlappingCaps;

        target.wallCapInset = wallCapInset;

        target.randomSeed = randomSeed;
        target.seed = seed;
        target.log = log;
    }

    private void OnValidate()
    {
        minRooms = Mathf.Max(1, minRooms);
        maxRooms = Mathf.Max(1, maxRooms);
        minEndDistanceRooms = Mathf.Max(0, minEndDistanceRooms);
        maxEndDistanceRooms = Mathf.Max(0, maxEndDistanceRooms);
        attemptsPerOpenSocket = Mathf.Max(1, attemptsPerOpenSocket);
        maxGenerationRetries = Mathf.Max(1, maxGenerationRetries);
        emergencyFallbackRetries = Mathf.Max(1, emergencyFallbackRetries);
        emergencyMinimumRooms = Mathf.Max(RoomAssemblerConfig.MinimumEmergencyRooms, emergencyMinimumRooms);
        emergencyAdditionalMaxRooms = Mathf.Max(0, emergencyAdditionalMaxRooms);
        centerSnapTolerance = Mathf.Max(0f, centerSnapTolerance);
        forwardDotTolerance = Mathf.Clamp(forwardDotTolerance, -1f, 1f);
        widthToleranceFallback = Mathf.Max(0f, widthToleranceFallback);
        overlapPadding = Mathf.Max(0f, overlapPadding);
        maxCapsAfterEnd = Mathf.Max(0, maxCapsAfterEnd);
        capExtraPadding = Mathf.Max(0f, capExtraPadding);
        roomUnitWorldOverride = Mathf.Max(0f, roomUnitWorldOverride);
    }

    private void WarnIfInvalid(Object context)
    {
        if (maxRooms < minRooms)
        {
            Debug.LogWarning(
                $"[Level Profile] {name}: maxRooms ({maxRooms}) is below minRooms ({minRooms}). Runtime config will clamp maxRooms to minRooms.",
                context);
        }

        if (maxEndDistanceRooms < minEndDistanceRooms)
        {
            Debug.LogWarning(
                $"[Level Profile] {name}: maxEndDistanceRooms ({maxEndDistanceRooms}) is below minEndDistanceRooms ({minEndDistanceRooms}). Runtime config will clamp the max value.",
                context);
        }
    }
}
