using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class StaticLevelRoomEntry
{
    public string roomId = "room";
    public RoomDefinition roomDefinition;
    public Vector3 scale = Vector3.one;

    [Header("Attachment")]
    public string attachToRoomId;
    public string targetSocketName;
    public string roomSocketName;
}

[CreateAssetMenu(menuName = "SO/Level Flow/Static Level Layout Profile", fileName = "StaticLevelLayoutProfile")]
public sealed class StaticLevelLayoutProfile : ScriptableObject
{
    [Header("Rooms")]
    public List<StaticLevelRoomEntry> rooms = new List<StaticLevelRoomEntry>();
    public RoomDefinition wallCapRoom;
    public bool capOpenSockets = true;
    public bool matchCapScaleToRoom = true;
    [Min(0f)] public float wallCapInset = 0.02f;

    [Header("Runtime")]
    public bool clearBeforeBuild = true;
    public bool buildNavMesh = true;

    [Header("Player")]
    public bool spawnPlayer = true;
    public bool reuseExistingPlayer = true;
    public GameObject playerPrefab;
    public string playerRoomId;
    public Vector3 playerFallbackLocalPosition = Vector3.up;

    [Header("Boss")]
    public bool spawnBoss;
    public GameObject bossPrefab;
    public string bossRoomId;
    public Vector3 bossFallbackLocalPosition = Vector3.up;
}
