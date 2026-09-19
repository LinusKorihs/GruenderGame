using UnityEngine;
using UnityEngine.AI;

/* <Summary / Notes>
    Enemy 1 — Lunger (Goomba-like ground rusher).

    Full behaviour loop:
    1. IDLE — asleep; standing still until player enters LOS or lunger takes damage.

    2. PRE-LUNGE — windup; direction locked toward target. → LUNGING after timer.

    3. LUNGING — flying toward target at LungeSpeed.
       Deals sweep damage to every player/minion overlapped (once per target).
       → RECOVERING on wall hit or reaching lunge distance.

    4. RECOVERING — stunned on the ground after landing. → APPROACH after timer.

    5. APPROACH — walking toward nearest target at MoveSpeed.
       Switches to a nearer target if found (0.5 m hysteresis).
       → BITE when within BiteMaxRange.

    6. BITE — in melee range; biting on cooldown.
       → APPROACH if target moves away. → IDLE when all targets are lost.
*/
[RequireComponent(typeof(CombatantStats))]
public class LungerEnemy : MonoBehaviour
{
    private enum LungerState
    {
        Idle,       // asleep or no target
        Approach,   // walking toward target
        PreLunge,   // windup before launching
        Lunging,    // flying toward target
        Recovering, // stunned after landing
        Bite        // in melee range; biting on cooldown
    }

    [SerializeField] private LungerEnemySettings settings;

    [Tooltip("Assign the player's actual moving transform. " + "Required when the player prefab root is a static anchor above the moving body.")]
    [SerializeField] private Transform playerTransform;

    [Header("Animation")]
    [SerializeField] private LungerAnimatorBridge animationBridge;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private bool applyVisualYawOffset = true;
    [SerializeField] private float visualYawOffset = 180f;
    [SerializeField] private bool damageDrivenByAnimationEvents = true;
    [SerializeField, Min(0f)] private float deathDestroyDelay = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool enableLogs;

    private CombatantStats stats;
    private Rigidbody rb;

    private Transform currentTarget;
    private Vector3 lastKnownTargetPos;
    private bool hasLastKnownPos;

    [Header("Runtime (Read Only)")]
    [SerializeField] private LungerState currentState = LungerState.Idle;
    private float stateTimer;

    private bool isAsleep = true; // stands still until hit or player spotted
    private bool hasLungedThisEncounter; // blocks re-lunge until target is fully lost

    private Vector3 lungeDirection;
    private Vector3 lungeStartPos;
    private float lungeDistance; // dist-to-target + overshoot, locked at windup
    private bool hitWallDuringLunge;
    private float lastLungeTime = -999f;
    private readonly System.Collections.Generic.HashSet<int> lungeHitIds =
        new System.Collections.Generic.HashSet<int>();
    private bool lungeDamageActive;

    private float lastBiteTime = -999f;
    private Transform pendingBiteTarget;
    private bool biteDamagePending;

    private Vector3 frameVelocity;
    private int myLayer;

    private NavMeshPath navPath;
    private int navCornerIndex;
    private bool hasNavPath;
    private float nextNavRepathTime;
    private Vector3 navLastDestination;

    private static readonly Collider[] overlapBuffer = new Collider[32];

    private void Awake()
    {
        NavMeshAgent navMeshAgent = GetComponent<NavMeshAgent>();
        if (navMeshAgent != null) navMeshAgent.enabled = false;

        stats = GetComponent<CombatantStats>();
        stats.Died += OnDied;
        stats.DamageTaken += OnDamageTaken;

        if (animationBridge == null)
            animationBridge = GetComponentInChildren<LungerAnimatorBridge>(true);

        if (visualRoot == null && animationBridge != null)
            visualRoot = animationBridge.transform;

        ApplyVisualOrientationOffset();

        if (animationBridge == null)
            Log("No LungerAnimatorBridge found in children. Animations will not be driven by LungerEnemy.");
        else
            animationBridge.ResetToIdle();

        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        myLayer = gameObject.layer;

        if (settings != null && settings.IgnoreCollisionMask != 0)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((settings.IgnoreCollisionMask.value & (1 << i)) != 0)
                    Physics.IgnoreLayerCollision(myLayer, i, true);
            }
        }
    }

    private void OnDestroy()
    {
        if (stats != null)
        {
            stats.Died -= OnDied;
            stats.DamageTaken -= OnDamageTaken;
        }
    }

    private void Update()
    {
        if (stats.IsDead) return;

        frameVelocity = Vector3.zero;
        RefreshTarget();
        UpdateState();
        ExecuteState();
    }

    private void FixedUpdate()
    {
        if (rb == null || stats == null || stats.IsDead) return;
        rb.linearVelocity = new Vector3(frameVelocity.x, rb.linearVelocity.y, frameVelocity.z);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (currentState != LungerState.Lunging) return;

        // Targets are handled by sweep damage — skip them here.
        bool hitPlayer = EnemyTargetUtility.FindTaggedActor(collision.transform, settings.PlayerTag) != null;
        bool hitMinion = EnemyTargetUtility.FindTaggedActor(collision.transform, settings.MinionTag) != null;
        if (hitPlayer || hitMinion) return;

        // Only horizontal contact normals (walls) stop the lunge; floor and ceiling are ignored.
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (Mathf.Abs(collision.GetContact(i).normal.y) <= 0.5f)
            {
                hitWallDuringLunge = true;
                return;
            }
        }
    }

    // Searches the transform itself, then up, then down — handles any hierarchy layout.
    private static CombatantStats GetStats(Transform t)
    {
        return EnemyTargetUtility.GetStats(t);
    }

    private void OnDamageTaken(float _)
    {
        isAsleep = false;
    }

    private void RefreshTarget()
    {
        float forgetRadius = settings != null ? settings.ForgetRadius : 18f;

        // Drop stale / dead targets.
        if (currentTarget != null)
        {
            CombatantStats ts = GetStats(currentTarget);
            bool dead = ts != null && ts.IsDead;
            bool far  = HorizontalDistance(currentTarget.position) > forgetRadius;

            if (dead || far || !currentTarget.gameObject.activeInHierarchy)
                LoseTarget(targetDied: dead);
        }

        // Update last known position while we have LOS.
        if (currentTarget != null && HasLineOfSight(currentTarget))
        {
            lastKnownTargetPos = currentTarget.position;
            hasLastKnownPos = true;
        }

        if (currentTarget != null) return;

        // Asleep: only player in direct sight wakes the lunger.
        if (isAsleep)
        {
            Transform player = FindPlayerInSight();
            if (player != null)
            {
                isAsleep = false;
                currentTarget = player;
                Log($"Player spotted — waking, lunging at: {player.name}");
                BeginPreLunge();
            }
            return;
        }

        // Awake but no target: find nearest threat or return to sleep.
        Transform found = FindBestTarget();
        if (found != null)
        {
            currentTarget = found;
            Log($"Target acquired: {found.name}");
            if (!hasLungedThisEncounter)
                BeginPreLunge();
        }
        else
        {
            isAsleep = true;
            Log("No targets in range — returning to sleep");
        }
    }

    // Drops the target; finds a replacement if it died, otherwise resets and sleeps.
    private void LoseTarget(bool targetDied = false)
    {
        string lost = currentTarget != null ? currentTarget.name : "?";
        currentTarget = null;
        hasLastKnownPos = false;
        ResetNavPath();

        if (targetDied)
        {
            Transform next = FindBestTarget();
            if (next != null)
            {
                currentTarget = next;
                Log($"Target {lost} died \u2192 next target: {next.name}");
                return;
            }
            // No replacement found this frame — stay awake so RefreshTarget retries next frame.
            Log($"Target {lost} died \u2014 no replacement found, staying alert");
            return;
        }

        hasLungedThisEncounter = false;
        lastLungeTime = -999f;
        isAsleep = true;
        Log($"Target lost ({lost}) \u2014 returning to sleep");
    }

    // Returns the player root transform if visible; null otherwise.
    private Transform FindPlayerInSight()
    {
        if (settings == null) return null;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, settings.DetectRadius, overlapBuffer, settings.DetectMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null) continue;

            Transform player = EnemyTargetUtility.FindTaggedActor(col.transform, settings.PlayerTag);
            if (player == null || !player.gameObject.activeInHierarchy) continue;

            CombatantStats cs = GetStats(col.transform);
            if (cs != null && cs.IsDead) continue;

            if (!HasLineOfSight(col.transform)) continue;

            return playerTransform != null ? playerTransform : player;
        }
        return null;
    }

    private Transform FindBestTarget()
    {
        if (settings == null) return null;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, settings.DetectRadius, overlapBuffer, settings.DetectMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestSq = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null) continue;

            Transform t = col.transform;
            Transform player = EnemyTargetUtility.FindTaggedActor(t, settings.PlayerTag);
            Transform minion = EnemyTargetUtility.FindTaggedActor(t, settings.MinionTag);
            Transform actor = player != null ? player : minion;
            if (actor == null || !actor.gameObject.activeInHierarchy) continue;

            CombatantStats cs = GetStats(t);
            if (cs != null && cs.IsDead) continue;

            bool isPlayer = player != null;
            bool isMinion = minion != null;
            if (!isPlayer && !isMinion) continue;

            if (settings.RequireLOSToDetect && !HasLineOfSight(t)) continue;

            // Use playerTransform override for player; cs.transform for individual minion tracking.
            Transform trackTarget;
            if (isPlayer && playerTransform != null)
                trackTarget = playerTransform;
            else if (cs != null)
                trackTarget = cs.transform;
            else
                trackTarget = t;

            float sq = (trackTarget.position - transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = trackTarget; }
        }

        return best;
    }

    private void UpdateState()
    {
        // Sleeping: do nothing.
        if (isAsleep) { SetState(LungerState.Idle); return; }

        // These states self-terminate via their own timers or events.
        if (currentState == LungerState.PreLunge  ||
            currentState == LungerState.Lunging    ||
            currentState == LungerState.Recovering) return;

        if (currentTarget == null) { SetState(LungerState.Idle); return; }

        float dist = Vector3.Distance(transform.position, currentTarget.position);

        if (dist <= settings.BiteMaxRange)
        {
            SetState(LungerState.Bite);
            return;
        }

        SetState(LungerState.Approach);
    }

    private void ExecuteState()
    {
        switch (currentState)
        {
            case LungerState.Idle:
            {
                SetAnimationSpeed(0f);
                break;
            }

            case LungerState.Approach:
            {
                if (currentTarget == null) break;

                SetAnimationSpeed(1f);

                // Switch to a nearer target if one exists (0.5 m hysteresis avoids flip-flop).
                Transform nearest = FindBestTarget();
                if (nearest != null && nearest != currentTarget)
                {
                    float dNearest = Vector3.Distance(transform.position, nearest.position);
                    float dCurrent = Vector3.Distance(transform.position, currentTarget.position);
                    if (dNearest < dCurrent - 0.5f)
                    {
                        Log($"Closer target: switching {currentTarget.name} \u2192 {nearest.name}");
                        currentTarget = nearest;
                        ResetNavPath();
                    }
                }

                MoveTowards(currentTarget.position, settings.BiteDesiredRange);
                break;
            }

            case LungerState.PreLunge:
            {
                SetAnimationSpeed(0f);
                stateTimer -= Time.deltaTime;
                FaceTowards(lungeDirection);
                if (stateTimer <= 0f && Vector3.Angle(transform.forward, lungeDirection) <= 10f)
                {
                    hitWallDuringLunge = false;
                    SetState(LungerState.Lunging);
                }
                break;
            }

            case LungerState.Lunging:
            {
                SetAnimationSpeed(0f);
                if (!damageDrivenByAnimationEvents || lungeDamageActive)
                {
                    // Once the animation hit frame opens the window, sweep while the body moves.
                    DealLungeSweepDamage();
                }

                float traveled   = HorizontalDistance(lungeStartPos);
                bool  reachedEnd = traveled >= lungeDistance;

                if (hitWallDuringLunge || reachedEnd)
                {
                    BeginRecovery();
                    break;
                }

                frameVelocity = lungeDirection * settings.LungeSpeed;
                FaceTowards(lungeDirection);
                break;
            }

            case LungerState.Recovering:
            {
                SetAnimationSpeed(0f);
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    // Re-evaluate target after landing — a nearer threat may have appeared.
                    Transform best = FindBestTarget();
                    if (best != null && best != currentTarget)
                    {
                        Log($"Post-lunge retarget: {currentTarget?.name} \u2192 {best.name}");
                        currentTarget = best;
                        ResetNavPath();
                    }
                    SetState(currentTarget != null ? LungerState.Approach : LungerState.Idle);
                }
                break;
            }

            case LungerState.Bite:
            {
                SetAnimationSpeed(0f);
                if (currentTarget == null) { SetState(LungerState.Idle); break; }

                // Target walked out of range — close in.
                if (Vector3.Distance(transform.position, currentTarget.position) > settings.BiteMaxRange)
                {
                    SetState(LungerState.Approach);
                    break;
                }

                FaceTarget();

                // LOS blocked — approach until visible again.
                if (settings.RequireLOSToAttack && !HasLineOfSight(currentTarget))
                {
                    SetState(LungerState.Approach);
                    break;
                }

                if (Time.time >= lastBiteTime + settings.BiteCooldown)
                {
                    lastBiteTime = Time.time;
                    pendingBiteTarget = currentTarget;
                    biteDamagePending = true;
                    PlayMainAttackAnimation();
                    SoundManager.TryPlayId("Enemy.Lunger.Attack", transform);

                    if (damageDrivenByAnimationEvents)
                        break;

                    biteDamagePending = false;
                    pendingBiteTarget = null;

                    CombatantStats ts = GetStats(currentTarget);
                    if (ts != null && !ts.IsDead)
                    {
                        ts.ApplyDamage(settings.BiteDamage);
                        Log($"Bite hit {currentTarget.name} for {settings.BiteDamage} damage");
                    }

                    // After each bite, switch to a nearer target if one exists (0.5 m hysteresis).
                    Transform nearest = FindBestTarget();
                    if (nearest != null && nearest != currentTarget)
                    {
                        float dNearest = Vector3.Distance(transform.position, nearest.position);
                        float dCurrent = Vector3.Distance(transform.position, currentTarget.position);
                        if (dNearest < dCurrent - 0.5f)
                        {
                            Log($"Post-bite: closer target — switching {currentTarget.name} → {nearest.name}");
                            currentTarget = nearest;
                            ResetNavPath();
                        }
                    }
                }
                break;
            }
        }
    }

    private void BeginPreLunge()
    {
        if (currentState == LungerState.PreLunge  ||
            currentState == LungerState.Lunging    ||
            currentState == LungerState.Recovering) return;

        if (settings != null && Time.time < lastLungeTime + settings.LungeCooldown) return;

        // Lock direction toward current target or last known position.
        Vector3 dir;
        if (currentTarget != null)
            dir = currentTarget.position - transform.position;
        else if (hasLastKnownPos)
            dir = lastKnownTargetPos - transform.position;
        else
            return;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        lungeDirection = dir.normalized;
        // Lunge distance includes overshoot so the player must sidestep.
        float overshoot = settings != null ? settings.LungeOvershootDistance : 2f;
        lungeDistance = dir.magnitude + overshoot;
        lastLungeTime = Time.time;
        stateTimer = settings != null ? settings.LungeWindupDuration : 0.4f;
        lungeDamageActive = false;
        lungeHitIds.Clear();
        PlayLungeAttackAnimation();
        SoundManager.TryPlayId("Enemy.Lunger.Attack", transform);
        SetState(LungerState.PreLunge);
    }

    private void BeginRecovery()
    {
        frameVelocity = Vector3.zero;
        lungeDamageActive = false;
        hasLungedThisEncounter = true;
        stateTimer = settings != null ? settings.LungeRecoveryDuration : 1.2f;
        // Restore lunge-only pass-through layers (skip any that are permanently ignored).
        if (settings != null)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((settings.LungeIgnoreCollisionMask.value & (1 << i)) != 0 &&
                    (settings.IgnoreCollisionMask.value      & (1 << i)) == 0)
                {
                    Physics.IgnoreLayerCollision(myLayer, i, false);
                }
            }
        }
        SetState(LungerState.Recovering);
    }

    // Damages every player/minion overlapping the body hitbox (once per target per lunge).
    private void DealLungeSweepDamage()
    {
        if (settings == null) return;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, settings.LungeHitRadius, overlapBuffer, settings.DetectMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null) continue;

            Transform player = EnemyTargetUtility.FindTaggedActor(col.transform, settings.PlayerTag);
            Transform minion = EnemyTargetUtility.FindTaggedActor(col.transform, settings.MinionTag);
            bool isPlayer = player != null;
            bool isMinion = minion != null;
            if (!isPlayer && !isMinion) continue;

            Transform actor = isPlayer ? player : minion;
            int id = actor.GetInstanceID();
            if (lungeHitIds.Contains(id)) continue;

            CombatantStats ts = GetStats(col.transform);
            if (ts != null && !ts.IsDead)
            {
                lungeHitIds.Add(id);
                ts.ApplyDamage(settings.LungeDamage);
                Log($"Lunge sweep hit {actor.name} for {settings.LungeDamage} damage");
                if (settings.LungeStopOnHit)
                    hitWallDuringLunge = true;
            }
        }
    }

    private void MoveTowards(Vector3 targetPos, float stopDistance)
    {
        Vector3 toTarget = targetPos - transform.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        if (dist <= stopDistance) { ResetNavPath(); return; }

        float baseSpeed = settings != null ? settings.MoveSpeed : 3.5f;
        float speed = SupportSlowTarget.ApplyMoveSpeed(this, baseSpeed);

        if (settings != null && settings.UseNavMesh)
        {
            if (TryMoveAlongNavPath(targetPos, stopDistance, speed)) return;
        }

        Vector3 dir = toTarget / Mathf.Max(dist, 0.0001f);
        frameVelocity = dir * speed;
        SmoothFaceDirection(dir);
    }

    private bool TryMoveAlongNavPath(Vector3 destination, float stopDistance, float speed)
    {
        float now = Time.time;
        // Repath only when the target has moved significantly.
        bool destinationMoved = (navLastDestination - destination).sqrMagnitude > 2f * 2f;

        if (!hasNavPath || now >= nextNavRepathTime || destinationMoved)
        {
            if (!TryBuildNavPath(destination)) return false;
        }

        Vector3[] corners = navPath.corners;
        if (corners == null || corners.Length == 0) { hasNavPath = false; return false; }

        float tolerance = settings != null ? settings.NavWaypointTolerance : 0.3f;
        navCornerIndex = Mathf.Clamp(navCornerIndex, 1, corners.Length - 1);

        while (navCornerIndex < corners.Length)
        {
            Vector3 toCorner = corners[navCornerIndex] - transform.position;
            toCorner.y = 0f;

            if (toCorner.sqrMagnitude <= tolerance * tolerance) { navCornerIndex++; continue; }

            if (navCornerIndex == corners.Length - 1 && toCorner.magnitude <= stopDistance)
            {
                ResetNavPath();
                return true;
            }

            Vector3 moveDir = toCorner.normalized;

            // Blend facing toward the next corner early to smooth out sharp turns.
            if (navCornerIndex + 1 < corners.Length)
            {
                float blend = 1f - Mathf.Clamp01(toCorner.magnitude / 1.5f);
                Vector3 toNext = corners[navCornerIndex + 1] - transform.position;
                toNext.y = 0f;
                if (toNext.sqrMagnitude > 0.01f)
                    moveDir = Vector3.Slerp(moveDir, toNext.normalized, blend).normalized;
            }

            frameVelocity = toCorner.normalized * speed;
            SmoothFaceDirection(moveDir);
            return true;
        }

        ResetNavPath();
        return true;
    }

    private bool TryBuildNavPath(Vector3 destination)
    {
        if (navPath == null) navPath = new NavMeshPath();

        float repathInterval = settings != null ? settings.NavRepathInterval : 0.3f;
        nextNavRepathTime    = Time.time + repathInterval;

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit startHit, 1.5f, NavMesh.AllAreas))
        { hasNavPath = false; return false; }

        float sampleRadius = settings != null ? settings.NavTargetSampleRadius : 1.5f;
        if (!NavMesh.SamplePosition(destination, out NavMeshHit destHit, sampleRadius, NavMesh.AllAreas))
        { hasNavPath = false; return false; }

        bool ok = NavMesh.CalculatePath(startHit.position, destHit.position, NavMesh.AllAreas, navPath);
        if (!ok || navPath.status == NavMeshPathStatus.PathInvalid ||
            navPath.corners == null || navPath.corners.Length < 2)
        { hasNavPath = false; return false; }

        hasNavPath = true;
        navCornerIndex = 1;
        navLastDestination = destination;
        return true;
    }

    private void ResetNavPath() { hasNavPath = false; navCornerIndex = 0; nextNavRepathTime = 0f; }

    public void OnMainAttackHitFrame()
    {
        ApplyPendingBiteDamage();
    }

    public void OnLungeAttackHitFrame()
    {
        lungeDamageActive = true;
        DealLungeSweepDamage();
    }

    public void OnDeathAnimationFinished()
    {
        // Imported animation events may occur before the clip finishes.
    }

    private void ApplyPendingBiteDamage()
    {
        if (!biteDamagePending)
        {
            Log("Main attack hit frame reached, but no bite damage was pending.");
            return;
        }

        biteDamagePending = false;

        Transform target = pendingBiteTarget;
        pendingBiteTarget = null;

        if (target == null)
        {
            Log("Bite missed: target no longer exists.");
            return;
        }

        float maxRange = settings != null ? settings.BiteMaxRange : 1.5f;
        if (Vector3.Distance(transform.position, target.position) > maxRange)
        {
            Log($"Bite missed {target.name}: target moved out of range.");
            return;
        }

        if (settings != null && settings.RequireLOSToAttack && !HasLineOfSight(target))
        {
            Log($"Bite missed {target.name}: line of sight blocked.");
            return;
        }

        CombatantStats ts = GetStats(target);
        if (ts != null && !ts.IsDead)
        {
            float damage = settings != null ? settings.BiteDamage : 10f;
            ts.ApplyDamage(damage);
            Log($"Bite hit {target.name} for {damage} damage");
        }

        RetargetAfterBite();
    }

    private void RetargetAfterBite()
    {
        if (currentTarget == null) return;

        Transform nearest = FindBestTarget();
        if (nearest == null || nearest == currentTarget) return;

        float dNearest = Vector3.Distance(transform.position, nearest.position);
        float dCurrent = Vector3.Distance(transform.position, currentTarget.position);
        if (dNearest < dCurrent - 0.5f)
        {
            Log($"Post-bite: closer target â€” switching {currentTarget.name} â†’ {nearest.name}");
            currentTarget = nearest;
            ResetNavPath();
        }
    }

    private void ApplyVisualOrientationOffset()
    {
        if (!applyVisualYawOffset || visualRoot == null) return;

        visualRoot.localRotation = Quaternion.Euler(0f, visualYawOffset, 0f);
    }

    private void SetAnimationSpeed(float speed)
    {
        if (animationBridge != null)
            animationBridge.SetSpeed(speed);
    }

    private void PlayMainAttackAnimation()
    {
        if (animationBridge != null)
            animationBridge.PlayMainAttack();
    }

    private void PlayLungeAttackAnimation()
    {
        if (animationBridge != null)
            animationBridge.PlayLungeAttack();
    }

    private void FaceTarget()
    {
        if (currentTarget == null) return;
        Vector3 dir = currentTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) SmoothFaceDirection(dir.normalized);
    }

    private void FaceTowards(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f) SmoothFaceDirection(direction.normalized);
    }

    private void SmoothFaceDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        float rs = settings != null ? settings.RotationSpeed : 8f;
        Quaternion targetRot = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rs * Time.deltaTime);
    }

    private float HorizontalDistance(Vector3 pos)
    {
        Vector3 d = pos - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    private bool HasLineOfSight(Transform target)
    {
        if (target == null || settings == null) return false;

        float h = settings.LosHeightOffset;
        Vector3 start = transform.position + Vector3.up * h;
        Vector3 end = target.position + Vector3.up * h;
        Vector3 dir = end - start;
        float dist = dir.magnitude;

        if (dist <= 0.0001f) return true;

        if (Physics.Raycast(start, dir / dist, out RaycastHit hit, dist, settings.LosBlockMask, QueryTriggerInteraction.Ignore))
            return EnemyTargetUtility.BelongsToActor(hit.transform, target);

        return true;
    }

    private void SetState(LungerState newState)
    {
        if (newState == currentState) return;
        string targetInfo = newState == LungerState.Approach && currentTarget != null ? $" [{currentTarget.name}]" : "";
        Log($"State: {currentState} → {newState}{targetInfo}");
        if (newState == LungerState.Lunging)
        {
            lungeStartPos = transform.position;
            // Enable lunge-only pass-through (e.g. player layer) so the lunger flies through targets.
            if (settings != null)
            {
                for (int i = 0; i < 32; i++)
                {
                    if ((settings.LungeIgnoreCollisionMask.value & (1 << i)) != 0)
                        Physics.IgnoreLayerCollision(myLayer, i, true);
                }
            }
        }
        currentState = newState;
    }

    private void Log(string msg)
    {
        if (enableLogs) Debug.Log($"[Lunger:{name}] {msg}");
    }

    private void OnDied()
    {
        frameVelocity = Vector3.zero;

        if (rb != null)
            rb.linearVelocity = Vector3.zero;

        if (animationBridge != null)
            animationBridge.SetDead(true);

        Animator animator = animationBridge != null ? animationBridge.GetComponent<Animator>() : null;
        StartCoroutine(CombatantStats.WaitForDeathState(animator, deathDestroyDelay,
            () => { if (stats != null && stats.IsDead) Destroy(gameObject); }, "E1_Death"));
    }
}
