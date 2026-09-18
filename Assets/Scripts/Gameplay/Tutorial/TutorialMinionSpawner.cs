using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class TutorialMinionSpawner : MonoBehaviour
{
    [Header("Minion Prefabs")]
    [SerializeField] private GameObject meleePrefab;
    [SerializeField] private GameObject rangedPrefab;
    [SerializeField] private GameObject supportPrefab;

    [Header("Spawn")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField, Min(1)] private int maxTutorialMinions = 10;
    [SerializeField, Min(0.1f)] private float spawnSpacing = 1.25f;
    [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 2f;

    private static readonly HashSet<TutorialMinionSpawner> Instances = new HashSet<TutorialMinionSpawner>();
    private readonly List<TutorialSpawnedMinion> spawned = new List<TutorialSpawnedMinion>();
    private PlayerMinionCommander commander;

    public int MaxTutorialMinions => maxTutorialMinions;
    public int SpawnedCount
    {
        get
        {
            RemoveMissingEntries();
            return spawned.Count;
        }
    }

    public void GetActiveCounts(out int melee, out int ranged, out int support)
    {
        RemoveMissingEntries();
        melee = 0;
        ranged = 0;
        support = 0;

        for (int i = 0; i < spawned.Count; i++)
        {
            MinionCore minion = spawned[i] != null ? spawned[i].GetComponent<MinionCore>() : null;
            if (minion == null) continue;
            switch (minion.RoleType)
            {
                case MinionRoleType.Melee: melee++; break;
                case MinionRoleType.Ranged: ranged++; break;
                case MinionRoleType.Support: support++; break;
            }
        }
    }

    private void Awake()
    {
        if (spawnPoint == null)
        {
            GameObject point = new GameObject("Minion Spawn Point");
            point.transform.SetParent(transform, false);
            point.transform.localPosition = new Vector3(0f, 0f, 3f);
            spawnPoint = point.transform;
        }

    }

    private void OnEnable()
    {
        Instances.Add(this);
    }

    private void OnDisable()
    {
        Instances.Remove(this);
    }

    private void OnDestroy()
    {
        ClearAll();
    }

    public void SpawnMelee() => Spawn(meleePrefab);
    public void SpawnRanged() => Spawn(rangedPrefab);
    public void SpawnSupport() => Spawn(supportPrefab);

    public void SpawnOneOfEach()
    {
        SpawnMelee();
        SpawnRanged();
        SpawnSupport();
    }

    public void ClearAll()
    {
        ResolveCommander();
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            TutorialSpawnedMinion marker = spawned[i];
            if (marker == null)
                continue;

            MinionCore minion = marker.GetComponent<MinionCore>();
            if (commander != null && minion != null)
                commander.UnregisterRuntimeMinion(minion);

            Destroy(marker.gameObject);
        }
        spawned.Clear();
    }

    public static void ClearAllTutorialMinions()
    {
        TutorialMinionSpawner[] spawners = new TutorialMinionSpawner[Instances.Count];
        Instances.CopyTo(spawners);
        for (int i = 0; i < spawners.Length; i++)
            spawners[i]?.ClearAll();

        TutorialSpawnedMinion[] leftovers = FindObjectsByType<TutorialSpawnedMinion>(FindObjectsSortMode.None);
        for (int i = 0; i < leftovers.Length; i++)
        {
            TutorialSpawnedMinion marker = leftovers[i];
            if (marker == null)
                continue;
            MinionCore minion = marker.GetComponent<MinionCore>();
            PlayerMinionCommander activeCommander = FindFirstObjectByType<PlayerMinionCommander>();
            if (activeCommander != null && minion != null)
                activeCommander.UnregisterRuntimeMinion(minion);
            Destroy(marker.gameObject);
        }
    }

    private void Spawn(GameObject prefab)
    {
        RemoveMissingEntries();
        if (prefab == null || spawned.Count >= maxTutorialMinions)
            return;

        Vector3 desired = (spawnPoint != null ? spawnPoint.position : transform.position)
            + transform.right * GetLateralOffset(spawned.Count);
        Vector3 position = NavMesh.SamplePosition(desired, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas)
            ? hit.position
            : desired;

        GameObject instance = Instantiate(prefab, position, transform.rotation);
        instance.name = prefab.name + "_Tutorial";

        TutorialSpawnedMinion marker = instance.GetComponent<TutorialSpawnedMinion>();
        if (marker == null)
            marker = instance.AddComponent<TutorialSpawnedMinion>();
        marker.Initialize(this);
        spawned.Add(marker);

        ResolveCommander();
        MinionCore minion = instance.GetComponent<MinionCore>();
        if (commander != null && minion != null)
            commander.RegisterMinion(minion);
    }

    private float GetLateralOffset(int index)
    {
        if (index == 0) return 0f;
        int step = (index + 1) / 2;
        return step * spawnSpacing * (index % 2 == 1 ? 1f : -1f);
    }

    private void ResolveCommander()
    {
        if (commander == null)
            commander = FindFirstObjectByType<PlayerMinionCommander>();
    }

    private void RemoveMissingEntries()
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            TutorialSpawnedMinion marker = spawned[i];
            CombatantStats stats = marker != null ? marker.GetComponent<CombatantStats>() : null;
            if (marker == null || (stats != null && stats.IsDead))
                spawned.RemoveAt(i);
        }
    }

}
