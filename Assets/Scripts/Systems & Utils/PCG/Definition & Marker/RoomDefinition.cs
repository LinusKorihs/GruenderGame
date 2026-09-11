using System.Collections.Generic;
using UnityEngine;

public enum PCGSpecialRoomKind
{
    None,
    Treasure,
    Shop,
    Boss
}

public enum PCGResearchRoomType
{
    Auto,
    Start,
    Boss,
    Treasure,
    Shop,
    DeadEnd,
    Corridor,
    Corner,
    ThreeWay,
    Crossway,
    BigRoom,
    GenericRoom,
    Cap
}

[CreateAssetMenu(menuName = "SO/PCG/Base/Room Definition", fileName = "RoomDefinition")]
public class RoomDefinition : ScriptableObject
{
    public string id;
    public GameObject prefab;

    [Min(1)] public int weight = 10;

    [Header("Flags")]
    public bool isStart = false;
    public bool isEnd = false;
    public bool isDeadEnd = false; // can also be used for WallCaps
    public bool isHallway = false;
    public bool isRoom = false;

    [Header("Research Metrics")]
    [Tooltip("Optional semantic room role used by PCG metrics. The configured end room is treated as Boss when this is None.")]
    public PCGSpecialRoomKind specialRoomKind = PCGSpecialRoomKind.None;

    [Tooltip("Optional manual room-type override for CSV metrics. Auto derives the type from flags, sockets, and prefab/id names.")]
    public PCGResearchRoomType researchRoomType = PCGResearchRoomType.Auto;

    [Tooltip("When this room is Treasure, Shop, or Boss, metrics expect it to have at most one non-cap graph connection.")]
    public bool specialRoomRequiresDeadEnd = true;

    [Header("Placement")]
    public bool allowRotation = true;

    [Tooltip("Socket width tolerance (world units). Example: 0.05")]
    public float widthTolerance = 0.05f;

    public List<SocketType> allowedSocketTypes = new() { SocketType.Corridor, SocketType.Door, SocketType.BigDoor };
}
