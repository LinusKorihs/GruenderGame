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
    [SerializeField, Min(0.05f)] private float spawnCooldown = 0.2f;
    [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 2f;
    [SerializeField, Min(0.1f)] private float groundProbeHeight = 4f;
    [SerializeField, Min(0.1f)] private float groundProbeDistance = 12f;

    private static readonly HashSet<TutorialMinionSpawner> Instances = new HashSet<TutorialMinionSpawner>();
    private readonly List<TutorialSpawnedMinion> spawned = new List<TutorialSpawnedMinion>();
    private PlayerMinionCommander commander;
    private TextMesh[] worldLabels;
    private Camera activeCamera;
    private float nextSpawnTime;

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
        worldLabels = GetComponentsInChildren<TextMesh>(true);
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

    private void LateUpdate()
    {
        UpdateWorldLabelVisibility();
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
        if (prefab == null || spawned.Count >= maxTutorialMinions || Time.time < nextSpawnTime)
            return;

        nextSpawnTime = Time.time + spawnCooldown;

        Vector3 desired = spawnPoint != null ? spawnPoint.position : transform.position;
        Vector3 position = ResolveSpawnPosition(desired);

        GameObject instance = Instantiate(prefab, position, transform.rotation);
        instance.name = prefab.name + "_Tutorial";

        TutorialSpawnedMinion marker = instance.GetComponent<TutorialSpawnedMinion>();
        if (marker == null)
            marker = instance.AddComponent<TutorialSpawnedMinion>();
        marker.Initialize(this);
        spawned.Add(marker);

        ResolveCommander();
        MinionCore minion = instance.GetComponent<MinionCore>();
        if (minion != null)
        {
            minion.SetRuntimeNavMeshNavigation(false);
            if (commander != null)
                minion.SetFollowTarget(commander.PlayerTransform, followImmediately: false);
        }
        if (commander != null && minion != null)
            commander.RegisterMinion(minion);
    }

    private Vector3 ResolveSpawnPosition(Vector3 desired)
    {
        Vector3 rayOrigin = desired + Vector3.up * groundProbeHeight;
        RaycastHit[] groundHits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            groundProbeHeight + groundProbeDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.PositiveInfinity;
        Vector3 groundedPosition = desired;
        bool foundGround = false;

        for (int i = 0; i < groundHits.Length; i++)
        {
            RaycastHit groundHit = groundHits[i];
            if (groundHit.transform == null || groundHit.transform.IsChildOf(transform)) continue;
            if (groundHit.collider.GetComponentInParent<MinionCore>() != null) continue;
            if (EnemyTargetUtility.FindTaggedActor(groundHit.transform, "Player") != null) continue;
            if (Vector3.Dot(groundHit.normal, Vector3.up) < 0.5f) continue;
            if (groundHit.distance >= closestDistance) continue;

            closestDistance = groundHit.distance;
            groundedPosition = groundHit.point;
            foundGround = true;
        }

        if (foundGround)
            return groundedPosition;

        return NavMesh.SamplePosition(desired, out NavMeshHit navHit, navMeshSampleRadius, NavMesh.AllAreas)
            ? navHit.position
            : desired;
    }

    private void UpdateWorldLabelVisibility()
    {
        if (activeCamera == null)
            activeCamera = Camera.main;
        if (activeCamera == null || worldLabels == null) return;

        Vector3 origin = activeCamera.transform.position;
        for (int i = 0; i < worldLabels.Length; i++)
        {
            TextMesh label = worldLabels[i];
            if (label == null) continue;

            Vector3 direction = label.transform.position - origin;
            float distance = direction.magnitude;
            bool occluded = false;

            if (distance > 0.01f)
            {
                RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance, ~0, QueryTriggerInteraction.Ignore);
                for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
                {
                    Transform hit = hits[hitIndex].transform;
                    if (hit == null || hit == label.transform || hit.IsChildOf(label.transform)) continue;
                    if (EnemyTargetUtility.FindTaggedActor(hit, "Player") != null) continue;
                    occluded = true;
                    break;
                }
            }

            Renderer labelRenderer = label.GetComponent<Renderer>();
            if (labelRenderer != null)
                labelRenderer.forceRenderingOff = occluded;
        }
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
