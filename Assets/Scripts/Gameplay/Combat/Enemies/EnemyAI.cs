using UnityEngine;
using UnityEngine.AI;

/* <Summary / Notes>
    Generic enemy AI supporting Melee and Ranged behaviour types.

    State flow:
    1. IDLE — no target; standing still.

    2. APPROACH — moving toward target at MoveSpeed.
       → ATTACK when within attack range.

    3. ATTACK — in range; executing attack on cooldown.
       → APPROACH when target moves out of range.
       → BACK AWAY when target is too close (Ranged only).

    4. BACK AWAY — target too close (Ranged only); retreating.
       → ATTACK when back in preferred range.

    5. CHASE LAST KNOWN — target lost LOS and EnablePursuit is true.
       → IDLE when position is reached or exceeds PursuitRadius.
*/
[RequireComponent(typeof(CombatantStats))]
public class EnemyAI : MonoBehaviour
{
    private enum EnemyState
    {
        Idle,           // no target; standing still
        Approach,       // moving toward target
        Attack,         // in range; executing attack
        BackAway,       // too close (Ranged only); retreating
        ChaseLastKnown  // LOS lost; pursuing last known position
    }

    [Tooltip("Shared behaviour configuration. Create via Assets > Create > SO > Combat > Enemy AI Settings.")]
    [SerializeField] private EnemyAISettings settings;

    private CombatantStats stats;
    private Rigidbody rb;

    private Transform currentTarget;
    private Vector3 lastKnownTargetPosition;
    private bool hasLastKnownPosition;
    private EnemyState currentState = EnemyState.Idle;
    private float lastAttackTime = -999f;

    private Vector3 frameVelocity; // horizontal movement, applied in FixedUpdate

    private NavMeshPath navPath;
    private int navCornerIndex;
    private float nextNavRepathTime;
    private Vector3 navLastDestination;
    private bool hasNavPath;

    private static readonly Collider[] overlapBuffer = new Collider[32];

    private void Awake()
    {
        stats = GetComponent<CombatantStats>();
        stats.Died += OnDied;

        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        if (settings != null && settings.IgnoreCollisionMask != 0)
        {
            int myLayer = gameObject.layer;
            for (int i = 0; i < 32; i++)
            {
                if ((settings.IgnoreCollisionMask.value & (1 << i)) != 0)
                    Physics.IgnoreLayerCollision(myLayer, i, true);
            }
        }
    }

    private void OnDestroy()
    {
        if (stats != null) stats.Died -= OnDied;
    }

    private void Update()
    {
        if (stats.IsDead) return;

        frameVelocity = Vector3.zero;   // reset; ExecuteState fills it in
        RefreshTarget();
        UpdateState();
        ExecuteState();
    }

    // Physics step: apply horizontal velocity while preserving Y so gravity is not cancelled.
    private void FixedUpdate()
    {
        if (rb == null || stats == null || stats.IsDead) return;
        rb.linearVelocity = new Vector3(frameVelocity.x, rb.linearVelocity.y, frameVelocity.z);
    }

    // Target selection and tracking
    private void RefreshTarget()
    {
        float forgetRadius = settings != null ? settings.ForgetRadius : 18f;

        // Drop stale/dead targets first.
        if (currentTarget != null)
        {
            CombatantStats targetStats = currentTarget.GetComponentInParent<CombatantStats>();
            bool isDead       = targetStats != null && targetStats.IsDead;
            bool outOfRange   = HorizontalDistance(currentTarget.position) > forgetRadius;

            if (isDead || outOfRange || !currentTarget.gameObject.activeInHierarchy)
            {
                currentTarget        = null;
                hasLastKnownPosition = false;
                ResetNavPath();
            }
        }

        // Track last known position while target is visible.
        if (currentTarget != null && HasLineOfSight(currentTarget))
        {
            lastKnownTargetPosition = currentTarget.position;
            hasLastKnownPosition    = true;
        }

        if (currentTarget != null) return;

        currentTarget = FindBestTarget();
    }

    private Transform FindBestTarget()
    {
        if (settings == null) return null;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, settings.DetectRadius, overlapBuffer, settings.DetectMask, QueryTriggerInteraction.Ignore);

        if (count == 0) return null;

        Transform bestRangedMinion = null;
        float     bestRangedSq    = float.PositiveInfinity;
        Transform bestOther       = null;
        float     bestOtherSq     = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null) continue;

            Transform candidate = col.transform;
            if (!candidate.gameObject.activeInHierarchy) continue;

            CombatantStats cStats = candidate.GetComponentInParent<CombatantStats>();
            if (cStats != null && cStats.IsDead) continue;

            bool isMinion = candidate.CompareTag(settings.MinionTag);
            bool isPlayer = candidate.CompareTag(settings.PlayerTag);
            if (!isMinion && !isPlayer) continue;

            // Skip targets behind walls when LOS detection is required.
            if (settings.RequireLOSToDetect && !HasLineOfSight(candidate)) continue;

            float sqDist = (candidate.position - transform.position).sqrMagnitude;

            // Ranged minions get a priority lane when the flag is enabled.
            if (settings.PrioritizeRangedMinions && isMinion)
            {
                MinionCore minion = candidate.GetComponentInParent<MinionCore>();
                if (minion != null && minion.RoleType == MinionRoleType.Ranged && sqDist < bestRangedSq)
                {
                    bestRangedSq     = sqDist;
                    bestRangedMinion = candidate;
                    continue;
                }
            }

            if (sqDist < bestOtherSq)
            {
                bestOtherSq = sqDist;
                bestOther   = candidate;
            }
        }

        return bestRangedMinion != null ? bestRangedMinion : bestOther;
    }

    // State machine
    private void UpdateState()
    {
        if (settings == null || currentTarget == null)
        {
            // No live target: pursue last known position while inside PursuitRadius.
            bool canPursue = settings != null && settings.EnablePursuit && hasLastKnownPosition && HorizontalDistance(lastKnownTargetPosition) <= settings.PursuitRadius;
            currentState = canPursue ? EnemyState.ChaseLastKnown : EnemyState.Idle;
            return;
        }

        // Lost LOS while target is still tracked → chase if pursuit is enabled.
        if (!HasLineOfSight(currentTarget))
        {
            bool canPursue = settings.EnablePursuit && hasLastKnownPosition;
            currentState = canPursue ? EnemyState.ChaseLastKnown : EnemyState.Idle;
            return;
        }

        float dist = HorizontalDistance(currentTarget.position);

        switch (settings.EnemyType)
        {
            case EnemyType.Melee:
                currentState = dist <= settings.MeleeAttackRange ? EnemyState.Attack : EnemyState.Approach;
                break;

            case EnemyType.Ranged:
                if (dist > settings.RangedMaxRange) currentState = EnemyState.Approach;
                else if (dist < settings.RangedMinRange) currentState = EnemyState.BackAway;
                else currentState = EnemyState.Attack;
                break;
        }
    }

    private void ExecuteState()
    {
        switch (currentState)
        {
            case EnemyState.Idle: break;
            case EnemyState.ChaseLastKnown:
            {
                if (!hasLastKnownPosition || settings == null) break;

                // Abandon pursuit if the last known position is too far away.
                if (HorizontalDistance(lastKnownTargetPosition) > settings.PursuitRadius)
                {
                    hasLastKnownPosition = false;
                    ResetNavPath();
                    currentState = EnemyState.Idle;
                    break;
                }

                MoveTowards(lastKnownTargetPosition, 0.5f);
                break;
            }

            case EnemyState.Approach:
            {
                if (settings == null || currentTarget == null) break;
                float stopDist = settings.EnemyType == EnemyType.Melee ? settings.MeleeAttackRange * 0.9f : (settings.RangedMinRange + settings.RangedMaxRange) * 0.5f;

                MoveTowards(currentTarget.position, stopDist);
                break;
            }

            case EnemyState.BackAway:
            {
                if (currentTarget == null || settings == null) break;
                Vector3 away = transform.position - currentTarget.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                {
                    // Route through MoveTowards so NavMesh obstacles are respected.
                    Vector3 retreatTarget = transform.position + away.normalized * settings.RangedMinRange;
                    retreatTarget.y = transform.position.y;
                    MoveTowards(retreatTarget, 0f);
                    SmoothFaceDirection(away.normalized);
                }
                break;
            }

            case EnemyState.Attack:
                FaceTarget();
                TryAttack();
                break;
        }
    }

    // Movement and navigation
    private void MoveTowards(Vector3 targetPos, float stopDistance)
    {
        Vector3 toTarget = targetPos - transform.position;
        toTarget.y       = 0f;
        float dist       = toTarget.magnitude;

        if (dist <= stopDistance)
        {
            ResetNavPath();
            return;
        }

        float baseSpeed = settings != null ? settings.MoveSpeed : 3.5f;
        float speed = SupportSlowTarget.ApplyMoveSpeed(this, baseSpeed);

        if (settings != null && settings.UseNavMesh)
        {
            if (TryMoveAlongNavPath(targetPos, stopDistance, speed)) return;
            // NavMesh unavailable — fall through to direct movement.
        }

        Vector3 dir   = toTarget / Mathf.Max(dist, 0.0001f);
        frameVelocity = dir * speed;
        SmoothFaceDirection(dir);
    }

    // NavMesh
    private bool TryMoveAlongNavPath(Vector3 destination, float stopDistance, float speed)
    {
        float now = Time.time;
        float repathInterval = settings != null ? settings.NavRepathInterval : 0.3f;

        bool destinationMoved = (navLastDestination - destination).sqrMagnitude > 0.35f * 0.35f;
        if (!hasNavPath || now >= nextNavRepathTime || destinationMoved)
        {
            if (!TryBuildNavPath(destination)) return false;
        }

        Vector3[] corners = navPath.corners;
        if (corners == null || corners.Length == 0) { hasNavPath = false; return false; }

        float tolerance = settings != null ? settings.NavWaypointTolerance : 0.3f;
        navCornerIndex  = Mathf.Clamp(navCornerIndex, 1, corners.Length - 1);

        while (navCornerIndex < corners.Length)
        {
            Vector3 toCorner = corners[navCornerIndex] - transform.position;
            toCorner.y = 0f;

            if (toCorner.sqrMagnitude <= tolerance * tolerance)
            {
                navCornerIndex++;
                continue;
            }

            // Last corner: respect stopDistance.
            if (navCornerIndex == corners.Length - 1 && toCorner.magnitude <= stopDistance)
            {
                ResetNavPath();
                return true;
            }

            Vector3 dir   = toCorner.normalized;
            frameVelocity = dir * speed;
            SmoothFaceDirection(dir);
            return true;
        }

        ResetNavPath();
        return true;
    }

    private bool TryBuildNavPath(Vector3 destination)
    {
        if (navPath == null) navPath = new NavMeshPath();

        float sampleRadius    = settings != null ? settings.NavTargetSampleRadius : 1.5f;
        float repathInterval  = settings != null ? settings.NavRepathInterval     : 0.3f;
        nextNavRepathTime     = Time.time + repathInterval;

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit startHit, 1.5f, NavMesh.AllAreas))
        {
            hasNavPath = false;
            return false;
        }

        if (!NavMesh.SamplePosition(destination, out NavMeshHit destHit, sampleRadius, NavMesh.AllAreas))
        {
            hasNavPath = false;
            return false;
        }

        bool ok = NavMesh.CalculatePath(startHit.position, destHit.position, NavMesh.AllAreas, navPath);
        if (!ok || navPath.status == NavMeshPathStatus.PathInvalid
               || navPath.corners == null || navPath.corners.Length < 2)
        {
            hasNavPath = false;
            return false;
        }

        hasNavPath         = true;
        navCornerIndex     = 1;
        navLastDestination = destination;
        return true;
    }

    private void ResetNavPath()
    {
        hasNavPath        = false;
        navCornerIndex    = 0;
        nextNavRepathTime = 0f;
    }

    private void FaceTarget()
    {
        if (currentTarget == null) return;

        Vector3 dir = currentTarget.position - transform.position;
        dir.y       = 0f;
        if (dir.sqrMagnitude > 0.0001f) SmoothFaceDirection(dir.normalized);
    }

    private void SmoothFaceDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;

        float rs             = settings != null ? settings.RotationSpeed : 8f;
        Quaternion targetRot = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation   = Quaternion.Slerp(transform.rotation, targetRot, rs * Time.deltaTime);
    }

    private float HorizontalDistance(Vector3 pos)
    {
        Vector3 d = pos - transform.position;
        d.y       = 0f;
        return d.magnitude;
    }

    // Combat
    private void TryAttack()
    {
        if (settings == null || currentTarget == null) return;

        // Never attack through walls.
        if (settings.RequireLOSToAttack && !HasLineOfSight(currentTarget)) return;

        float cooldown = settings.EnemyType == EnemyType.Melee ? settings.MeleeAttackCooldown : settings.RangedAttackCooldown;
        if (Time.time < lastAttackTime + cooldown) return;

        lastAttackTime = Time.time;

        switch (settings.EnemyType)
        {
            case EnemyType.Melee:
            {
                CombatantStats targetStats = currentTarget.GetComponentInParent<CombatantStats>();
                if (targetStats != null && !targetStats.IsDead)
                {
                    targetStats.ApplyDamage(settings.MeleeDamage);
                }

                break;
            }

            case EnemyType.Ranged:
            {
                if (settings.ProjectilePrefab != null)
                {
                    Vector3 spawnPos            = transform.position + Vector3.up * 0.5f;
                    MinionProjectile projectile = Instantiate(settings.ProjectilePrefab, spawnPos, Quaternion.identity)
                        .GetComponent<MinionProjectile>();

                    if (projectile != null)
                    {
                        projectile.Initialize(
                            currentTarget, settings.RangedDamage, settings.ProjectileSpeed,
                            settings.UseHomingProjectile, gameObject.tag, 6f);
                    }
                }
                else
                {
                    // Instant-hit fallback when no prefab is assigned.
                    CombatantStats targetStats = currentTarget.GetComponentInParent<CombatantStats>();
                    if (targetStats != null && !targetStats.IsDead)
                    {
                        targetStats.ApplyDamage(settings.RangedDamage);
                    }
                }

                break;
            }
        }
    }

    private void OnDied()
    {
        Destroy(gameObject);
    }

    // LOS
    private bool HasLineOfSight(Transform target)
    {
        if (target == null || settings == null) return false;

        float   height = settings.LosHeightOffset;
        Vector3 start  = transform.position + Vector3.up * height;
        Vector3 end    = target.position    + Vector3.up * height;
        Vector3 dir    = end - start;
        float   dist   = dir.magnitude;
        if (dist <= 0.0001f) return true;

        if (Physics.Raycast(start, dir / dist, out RaycastHit hit, dist, settings.LosBlockMask, QueryTriggerInteraction.Ignore))
        {
            // Hit something — LOS only if it’s the target itself.
            if (hit.transform == target || hit.transform.IsChildOf(target)) return true;
            return false;
        }

        return true;
    }
}
