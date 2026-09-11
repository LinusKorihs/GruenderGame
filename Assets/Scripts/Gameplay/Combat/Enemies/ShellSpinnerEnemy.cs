using UnityEngine;

/* <Summary / Notes>
     Enemy 2 — Shell Spinner (Koopa / Armos-like).

     Full behaviour loop:
     1. IDLE — asleep; standing still until a player or minion enters LOS or the spinner takes damage.

     2. WINDUP — target locked; live aim line is shown while the shell closes.
         → SPINNING when the windup timer ends.

     3. SPINNING — shell closed; moving in a straight line toward the locked direction.
         Any player, minion, or wall contact ends the spin.
         → HIT on impact or max range.

     4. HIT — brief impact pause while still inside the shell.
         → EXITING SHELL after the pause.

     5. EXITING SHELL — shell opens and collisions are restored.
         → DIZZY after the exit animation.

     6. DIZZY — vulnerable recovery pause.
         → WAKING UP after the dizzy timer ends.

     7. WAKING UP — shaking off the reset state.
         → Choose between shooting projectiles (random between fast or slow) or spinning if a target is still available, otherwise IDLE.
         Can maximum do the same action twice

     Hitbox setup:
     bodyObject  — full body collider, active while vulnerable.
     shellObject — shell collider, active while spinning.
*/
[RequireComponent(typeof(CombatantStats))]
public class ShellSpinnerEnemy : MonoBehaviour
{
    private enum SpinnerState
    {
          Idle,        // asleep or waiting for target
          Windup,      // live aim line while shell closes
          Spinning,    // moving in a locked straight line
          Hit,         // brief impact pause inside shell
          ExitingShell, // shell opens and collisions restore
          Dizzy,       // vulnerable recovery pause
          DizzyProj,    // variant: dizzy but fires projectiles at player
          WakingUp,    // reset animation before next action
          RangedWindup, // tucks in, holds position, tracks target
          RangedAttack  // fires projectiles while tracking target
    }

    private enum SpinnerAttackType
    {
        Spin,
        Ranged
    }

    private enum ProjectileVariant
    {
        Fast,
        Heavy
    }

    [SerializeField] private ShellSpinnerEnemySettings settings;

    [Tooltip("Assign the player's actual moving transform. " + "Required when the player prefab root is a static anchor above the moving body.")]
    [SerializeField] private Transform playerTransform;

    [Header("Hitboxes")]
    [Tooltip("Child GameObject containing the full-body collider (head + legs). Active when the spinner is vulnerable.")]
    [SerializeField] private GameObject bodyObject;
    [Tooltip("Child GameObject containing the shell-only collider. Active while the spinner is inside the shell (invincible). " + "This is the collider that physically contacts players/walls during the spin.")]
    [SerializeField] private GameObject shellObject;

    [Header("Targeting Line")]
    [Tooltip("LineRenderer used to draw a live aim line toward the target during the windup. " + "Assign a child LineRenderer (2 positions, world space). Leave empty to skip.")]
    [SerializeField] private LineRenderer targetingLine;

    [Header("Ranged Attack")]
    [Tooltip("Optional child transform projectiles spawn from. If empty, the settings ProjectileSpawnOffset is used.")]
    [SerializeField] private Transform projectileSpawnPoint;

    [Header("Animation")]
    [SerializeField] private ShellSpinnerAnimatorBridge animationBridge;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private GameObject animatedVisualPrefab;
    [SerializeField] private Transform animatedVisualParent;
    [SerializeField] private Vector3 animatedVisualLocalPosition;
    [SerializeField] private Vector3 animatedVisualLocalEulerAngles;
    [SerializeField] private Vector3 animatedVisualLocalScale = Vector3.one;
    [SerializeField] private bool hidePlaceholderMeshWhenVisualSpawned = true;
    [SerializeField] private bool applyVisualYawOffset;
    [SerializeField] private float visualYawOffset;
    [SerializeField] private bool projectilesDrivenByAnimationEvents = true;
    [SerializeField, Min(0.05f)] private float projectileAnimationEventFallbackDelay = 0.75f;
    [SerializeField, Min(0f)] private float deathDestroyDelay = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool enableLogs;

    private CombatantStats stats;
    private Rigidbody rb;

    private Transform currentTarget;

    [Header("Runtime (Read Only)")]
    [SerializeField] private SpinnerState currentState = SpinnerState.Idle;
    private float stateTimer;

    private bool isAsleep = true;

    private Vector3 spinDirection; // locked when the shell closes; never updated mid-spin
    private Vector3 spinStartPosition; // recorded when Spinning begins; used for MaxSpinRange
    private bool spinHitSomething; // set by OnCollisionEnter, consumed in ExecuteState

    private readonly System.Collections.Generic.HashSet<int> spinHitIds = new System.Collections.Generic.HashSet<int>();

    private float spinStartTime = -999f; // used for the collision grace period

    private Collider shellCollider;
    private Collider fallbackCollider;
    private readonly System.Collections.Generic.List<Collider> ignoredColliders = new System.Collections.Generic.List<Collider>();

    private Vector3 frameVelocity;

    private static readonly Collider[] overlapBuffer = new Collider[32];

    private bool hasCompletedFirstAttack;
    private SpinnerAttackType lastAttackType = SpinnerAttackType.Spin;
    private int sameAttackRepeatCount;

    private ProjectileVariant currentProjectileVariant;
    private int projectilesRemaining;
    private float nextProjectileTime;
    private bool rangedRecoveryStarted;

    private void Awake()
    {
        UnityEngine.AI.NavMeshAgent navMeshAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navMeshAgent != null) navMeshAgent.enabled = false;

        stats = GetComponent<CombatantStats>();
        stats.Died += OnDied;
        stats.DamageTaken += OnDamageTaken;

        EnsureAnimatedVisual();

        if (animationBridge == null)
            animationBridge = GetComponentInChildren<ShellSpinnerAnimatorBridge>(true);

        if (visualRoot == null && animationBridge != null)
            visualRoot = animationBridge.transform;

        ApplyVisualOrientationOffset();

        if (animationBridge == null)
            Log("No ShellSpinnerAnimatorBridge found in children. Animations will not be driven by ShellSpinnerEnemy.");
        else
            animationBridge.ResetToIdle();

        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        fallbackCollider = GetComponent<Collider>();
        if (fallbackCollider == null)
        {
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = 0.5f;
            capsule.height = 1.5f;
            capsule.center = Vector3.up * 0.5f;
            fallbackCollider = capsule;
        }

        SetHitboxState(inShell: false);
        shellCollider = shellObject != null ? shellObject.GetComponent<Collider>() : fallbackCollider;

        if (targetingLine != null)
        {
            targetingLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            targetingLine.receiveShadows = false;
            targetingLine.positionCount = 2;
            targetingLine.enabled = false;
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

    private void EnsureAnimatedVisual()
    {
        if (animationBridge == null)
            animationBridge = GetComponentInChildren<ShellSpinnerAnimatorBridge>(true);

        if (animationBridge != null || animatedVisualPrefab == null) return;

        Transform parent = animatedVisualParent != null ? animatedVisualParent : transform;
        GameObject spawnedVisual = Instantiate(animatedVisualPrefab, parent);
        spawnedVisual.name = animatedVisualPrefab.name;
        spawnedVisual.transform.localPosition = animatedVisualLocalPosition;
        spawnedVisual.transform.localRotation = Quaternion.Euler(animatedVisualLocalEulerAngles);
        spawnedVisual.transform.localScale = animatedVisualLocalScale;

        animationBridge = spawnedVisual.GetComponentInChildren<ShellSpinnerAnimatorBridge>(true);
        if (visualRoot == null)
            visualRoot = spawnedVisual.transform;

        if (hidePlaceholderMeshWhenVisualSpawned)
        {
            MeshRenderer placeholderRenderer = GetComponent<MeshRenderer>();
            if (placeholderRenderer != null)
                placeholderRenderer.enabled = false;
        }

        EnemyCursorHighlight highlight = GetComponent<EnemyCursorHighlight>();
        if (highlight != null)
            highlight.RefreshRenderers();
    }

    private void Update()
    {
        if (stats.IsDead) return;

        frameVelocity = Vector3.zero;
        RefreshTarget();
        UpdateState();
        ExecuteState();
    }

    // Updates the targeting line after transform and physics motion settles.
    private void LateUpdate()
    {
        if (currentState != SpinnerState.Windup || targetingLine == null || !targetingLine.enabled) return;
        if (currentTarget == null) return;
        targetingLine.SetPosition(0, SnapToGround(transform.position));
        targetingLine.SetPosition(1, SnapToGround(currentTarget.position));
    }

    // Projects a world-space point down onto the ground surface.
    private Vector3 SnapToGround(Vector3 worldPos)
    {
        const float offset = 0.05f; // hover just above the surface to avoid z-fighting
        const float rayHeight = 6f;
        if (settings != null && settings.GroundMask != 0)
        {
            Vector3 origin = worldPos + Vector3.up * rayHeight;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayHeight + 2f, settings.GroundMask, QueryTriggerInteraction.Ignore)) return hit.point + Vector3.up * offset;
        }
        return new Vector3(worldPos.x, transform.position.y + offset, worldPos.z);
    }

    private void FixedUpdate()
    {
        if (rb == null || stats == null || stats.IsDead) return;
        rb.linearVelocity = new Vector3(frameVelocity.x, rb.linearVelocity.y, frameVelocity.z);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (currentState != SpinnerState.Spinning) return;

        Transform player = EnemyTargetUtility.FindTaggedActor(collision.transform, settings.PlayerTag);
        Transform minion = EnemyTargetUtility.FindTaggedActor(collision.transform, settings.MinionTag);
        bool isPlayer = player != null;
        bool isMinion = minion != null;

        // Ignore floor / ceiling: only horizontal contacts (wall normals) matter.
        bool hasHorizontalContact = false;
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (Mathf.Abs(collision.GetContact(i).normal.y) <= 0.5f)
            {
                hasHorizontalContact = true;
                break;
            }
        }
        if (!hasHorizontalContact) return;

        // Grace period only applies to wall collisions, NOT to players/minions.
        if (!isPlayer && !isMinion)
        {
            float grace = settings != null ? settings.SpinCollisionGrace : 0.12f;
            if (Time.time < spinStartTime + grace) return;
        }

        if (isPlayer || isMinion)
        {
            // SpinUntilWall: physically pass through this target so the shell isn't stopped
            if (settings != null && settings.SpinUntilWall && shellCollider != null
                && !ignoredColliders.Contains(collision.collider))
            {
                Physics.IgnoreCollision(shellCollider, collision.collider, true);
                ignoredColliders.Add(collision.collider);
            }

            // Deal damage to the hit target (once per spin per target).
            Transform statsRoot = (isPlayer && playerTransform != null) ? playerTransform : collision.transform;
            CombatantStats ts = GetStats(statsRoot);
            if (ts != null)
            {
                int id = ts.GetInstanceID();
                if (!spinHitIds.Contains(id) && !ts.IsDead)
                {
                    spinHitIds.Add(id);
                    ts.ApplyDamage(settings != null ? settings.SpinDamage : 18f);
                    // For the player, apply knockback to playerTransform 
                    Transform knockbackTarget = (isPlayer && playerTransform != null) ? playerTransform : ts.transform;
                    ApplyKnockback(knockbackTarget);
                    Log($"Spin hit {ts.name} for {settings.SpinDamage}");
                }
            }
            // SpinUntilWall lets the shell pass through targets.
            if (settings == null || !settings.SpinUntilWall) spinHitSomething = true;
        }
        else
        {
            spinHitSomething = true;
        }
    }

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

        if (currentTarget != null)
        {
            CombatantStats ts = GetStats(currentTarget);
            bool dead = ts != null && ts.IsDead;
            bool far = HorizontalDistance(currentTarget.position) > forgetRadius;

            if (dead || far || !currentTarget.gameObject.activeInHierarchy) currentTarget = null;
        }

        if (currentTarget != null) return;

        if (isAsleep)
        {
            Transform player = FindPlayerInSight();
            if (player != null)
            {
                isAsleep = false;
                currentTarget = player;
                Log($"Player spotted — waking: {player.name}");
            }
            return;
        }

        Transform found = FindBestTarget();
        if (found != null)
        {
            currentTarget = found;
            Log($"Target acquired: {found.name}");
        }
        else
        {
            isAsleep = true;
            Log("No targets in range — returning to sleep");
        }
    }

    // Finds the player if it is visible from the spinner.
    private Transform FindPlayerInSight()
    {
        if (settings == null) return null;

        int count = Physics.OverlapSphereNonAlloc(transform.position, settings.DetectRadius, overlapBuffer, settings.DetectMask, QueryTriggerInteraction.Ignore);

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

    // Finds the closest valid target in range.
    private Transform FindBestTarget()
    {
        if (settings == null) return null;

        int count = Physics.OverlapSphereNonAlloc(transform.position, settings.DetectRadius, overlapBuffer, settings.DetectMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestSq = float.PositiveInfinity;

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
            if (!actor.gameObject.activeInHierarchy) continue;
            CombatantStats cs = GetStats(col.transform);
            if (cs != null && cs.IsDead) continue;
            if (settings.RequireLOSToDetect && !HasLineOfSight(col.transform)) continue;

            Transform t = (isPlayer && playerTransform != null) ? playerTransform : col.transform;
            float sq = (transform.position - t.position).sqrMagnitude;
            if (sq < bestSq) { best = t; bestSq = sq; }
        }

        return best;
    }

    private void UpdateState()
    {
        if (currentState == SpinnerState.Windup || currentState == SpinnerState.Spinning || currentState == SpinnerState.Hit || currentState == SpinnerState.ExitingShell || currentState == SpinnerState.Dizzy || currentState == SpinnerState.DizzyProj||currentState == SpinnerState.WakingUp || currentState == SpinnerState.RangedWindup || currentState == SpinnerState.RangedAttack) return;

        if (isAsleep || currentTarget == null)
        {
            SetState(SpinnerState.Idle);
            return;
        }

        SetState(ChooseNextAttackState());
    }

    private void ExecuteState()
    {
        switch (currentState)
        {
            case SpinnerState.Idle:
            {
                SetAnimationSpeed(0f);
                if (currentTarget != null) FaceTarget();
                break;
            }

            case SpinnerState.Windup:
            {
                stateTimer -= Time.deltaTime;

                if (stateTimer <= 0f)
                {
                    Vector3 dir = currentTarget != null ? currentTarget.position - transform.position : transform.forward;
                    dir.y = 0f;
                    spinDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
                    spinHitSomething = false;
                    spinHitIds.Clear();
                    SetState(SpinnerState.Spinning);
                }
                break;
            }

            case SpinnerState.Spinning:
            {
                SetAnimationSpeed(1f);
                if (spinHitSomething)
                {
                    SetState(SpinnerState.Hit);
                    break;
                }
                if (settings != null && settings.MaxSpinRange > 0f)
                {
                    float travelled = Vector3.Distance(transform.position, spinStartPosition);
                    if (travelled >= settings.MaxSpinRange)
                    {
                        SetState(SpinnerState.Hit);
                        break;
                    }
                }
                frameVelocity = spinDirection * (settings != null ? settings.SpinSpeed : 10f);
                FaceTowards(spinDirection);
                break;
            }

            case SpinnerState.Hit:
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f) SetState(SpinnerState.ExitingShell);
                break;
            }

            case SpinnerState.ExitingShell:
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f) SetState(SpinnerState.Dizzy);
                break;
            }

            case SpinnerState.Dizzy:
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f) SetState(SpinnerState.WakingUp);
                break;
            }

            case SpinnerState.DizzyProj:
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f) SetState(SpinnerState.WakingUp);
                break;
            }

            case SpinnerState.WakingUp:
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f) SetState(currentTarget != null ? ChooseNextAttackState() : SpinnerState.Idle);
                break;
            }

            case SpinnerState.RangedWindup:
            {
                stateTimer -= Time.deltaTime;
                frameVelocity = Vector3.zero;
                FaceTarget();
                if (stateTimer <= 0f) SetState(SpinnerState.RangedAttack);
                break;
            }

            case SpinnerState.RangedAttack:
            {
                frameVelocity = Vector3.zero;
                FaceTarget();

                if (projectilesRemaining > 0 && Time.time >= nextProjectileTime)
                {
                    if (projectilesDrivenByAnimationEvents && animationBridge != null)
                        Log("Projectile animation event did not arrive before fallback timer. Firing from gameplay timer.");

                    TryFireRangedProjectile("timer");
                }

                if (projectilesRemaining <= 0)
                {
                    if (!rangedRecoveryStarted)
                    {
                        rangedRecoveryStarted = true;
                        stateTimer = settings != null ? settings.RangedRecoveryDuration : 0.35f;
                    }

                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0f) SetState(SpinnerState.DizzyProj);
                }
                break;
            }
        }
    }

    // Sets the active collider pair based on whether the spinner is inside the shell.
    private void SetHitboxState(bool inShell)
    {
        if (stats != null) stats.IsInvincible = inShell;
        if (bodyObject != null) bodyObject.SetActive(!inShell);
        if (shellObject != null) shellObject.SetActive(inShell);
        if (bodyObject == null && shellObject == null && fallbackCollider != null)
            fallbackCollider.enabled = true;
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
        Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
        float speed = settings != null ? settings.RotationSpeed : 8f;
        transform.rotation = Quaternion.Slerp(transform.rotation, target, speed * Time.deltaTime);
    }

    private float HorizontalDistance(Vector3 pos)
    {
        Vector3 delta = pos - transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    private bool HasLineOfSight(Transform target)
    {
        if (target == null || settings == null) return false;
        Vector3 start = transform.position + Vector3.up * settings.LosHeightOffset;
        Vector3 end = target.position + Vector3.up * settings.LosHeightOffset;
        Vector3 dir = end - start;
        float dist = dir.magnitude;
        if (dist <= 0.0001f) return true;
        if (Physics.Raycast(start, dir / dist, out RaycastHit hit, dist, settings.LosBlockMask, QueryTriggerInteraction.Ignore))
            return EnemyTargetUtility.BelongsToActor(hit.transform, target);
        return true;
    }

    private void SetState(SpinnerState newState)
    {
        if (newState == currentState) return;
        Log($"State: {currentState} → {newState}");
        currentState = newState;

        if (targetingLine != null) targetingLine.enabled = false;

        switch (newState)
        {
            case SpinnerState.Idle:
                SetHitboxState(inShell: false);
                SetAnimationSpeed(0f);
                StopSpinningAnimation();
                StopShootingAnimation();
                break;

            case SpinnerState.Windup:
                RecordAttackChoice(SpinnerAttackType.Spin);
                SetHitboxState(inShell: false);
                SetAnimationSpeed(0f);
                PlaySpinAttackAnimation(true);
                stateTimer = settings != null ? settings.WindupDuration : 0.6f;
                if (targetingLine != null)
                {
                    targetingLine.positionCount = 2;
                    targetingLine.enabled = true;
                }
                break;

            case SpinnerState.Spinning:
                SetHitboxState(inShell: true);
                SetContinueSpinningAnimation(true);
                spinStartTime = Time.time;
                spinStartPosition = transform.position;
                ignoredColliders.Clear();
                break;

            case SpinnerState.Hit:
                SetHitboxState(inShell: true);
                SetAnimationSpeed(0f);
                StopSpinningAnimation();
                frameVelocity = Vector3.zero;
                if (rb != null) rb.linearVelocity = Vector3.zero;
                stateTimer = settings != null ? settings.HitPauseDuration : 0.15f;
                break;

            case SpinnerState.ExitingShell:
                SetHitboxState(inShell: false);
                StopSpinningAnimation();
                RestoreIgnoredColliders();
                stateTimer = settings != null ? settings.ExitShellDuration : 0.6f;
                break;

            case SpinnerState.Dizzy:
                stateTimer = settings != null ? settings.DizzyDuration : 2.5f;
                break;

            case SpinnerState.DizzyProj:
                stateTimer = settings != null ? settings.DizzyProjDuration : 2.5f;
                break;

            case SpinnerState.WakingUp:
                stateTimer = settings != null ? settings.WakeUpDuration : 0.4f;
                break;

            case SpinnerState.RangedWindup:
                RecordAttackChoice(SpinnerAttackType.Ranged);
                SetHitboxState(inShell: false);
                SetAnimationSpeed(0f);
                frameVelocity = Vector3.zero;
                if (rb != null) rb.linearVelocity = Vector3.zero;
                currentProjectileVariant = ChooseProjectileVariant();
                projectilesRemaining = GetProjectileCount(currentProjectileVariant);
                PlayProjectileAttackAnimation(false, projectilesRemaining > 1);
                stateTimer = settings != null ? settings.RangedWindupDuration : 0.35f;
                break;

            case SpinnerState.RangedAttack:
                SetHitboxState(inShell: false);
                frameVelocity = Vector3.zero;
                if (rb != null) rb.linearVelocity = Vector3.zero;
                if (projectilesRemaining <= 0)
                    projectilesRemaining = GetProjectileCount(currentProjectileVariant);
                nextProjectileTime = Time.time + (projectilesDrivenByAnimationEvents && animationBridge != null ? projectileAnimationEventFallbackDelay : 0f);
                rangedRecoveryStarted = false;
                break;
        }
    }

    private SpinnerState ChooseNextAttackState()
    {
        SpinnerAttackType chosen = ChooseNextAttackType();
        return chosen == SpinnerAttackType.Ranged ? SpinnerState.RangedWindup : SpinnerState.Windup;
    }

    private SpinnerAttackType ChooseNextAttackType()
    {
        if (!hasCompletedFirstAttack || !CanUseRangedAttack()) return SpinnerAttackType.Spin;

        int maxRepeats = settings != null ? Mathf.Max(1, settings.MaxSameAttackRepeats) : 2;
        if (sameAttackRepeatCount >= maxRepeats)
        {
            return lastAttackType == SpinnerAttackType.Spin ? SpinnerAttackType.Ranged : SpinnerAttackType.Spin;
        }

        float rangedChance = settings != null ? settings.RangedAttackChance : 0.5f;
        return Random.value < rangedChance ? SpinnerAttackType.Ranged : SpinnerAttackType.Spin;
    }

    private void RecordAttackChoice(SpinnerAttackType attackType)
    {
        if (hasCompletedFirstAttack && attackType == lastAttackType)
        {
            sameAttackRepeatCount++;
        }
        else
        {
            lastAttackType = attackType;
            sameAttackRepeatCount = 1;
        }

        hasCompletedFirstAttack = true;
    }

    private bool CanUseRangedAttack()
    {
        if (settings == null) return false;
        return settings.FastProjectilePrefab != null || settings.HeavyProjectilePrefab != null;
    }

    private ProjectileVariant ChooseProjectileVariant()
    {
        bool hasFast = settings != null && settings.FastProjectilePrefab != null;
        bool hasHeavy = settings != null && settings.HeavyProjectilePrefab != null;

        if (hasFast && hasHeavy) return Random.value < 0.5f ? ProjectileVariant.Fast : ProjectileVariant.Heavy;
        return hasHeavy ? ProjectileVariant.Heavy : ProjectileVariant.Fast;
    }

    private int GetProjectileCount(ProjectileVariant variant)
    {
        if (settings == null) return 1;
        int count = variant == ProjectileVariant.Fast ? settings.FastProjectileCount : settings.HeavyProjectileCount;
        return Mathf.Max(1, count);
    }

    private float GetProjectileInterval(ProjectileVariant variant)
    {
        if (settings == null) return 0.25f;
        float interval = variant == ProjectileVariant.Fast ? settings.FastProjectileInterval : settings.HeavyProjectileInterval;
        return Mathf.Max(0.01f, interval);
    }

    private void FireProjectile()
    {
        if (settings == null) return;

        GameObject prefab = GetProjectilePrefab(currentProjectileVariant);
        if (prefab == null || currentTarget == null) return;

        Vector3 spawnPos = projectileSpawnPoint != null
            ? projectileSpawnPoint.position
            : transform.TransformPoint(settings.ProjectileSpawnOffset);

        Vector3 aimDir = currentTarget.position - spawnPos;
        aimDir.y = 0f;
        Quaternion spawnRot = aimDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(aimDir.normalized, Vector3.up)
            : transform.rotation;

        MinionProjectile projectile = Instantiate(prefab, spawnPos, spawnRot).GetComponent<MinionProjectile>();
        if (projectile != null)
        {
            float damage = currentProjectileVariant == ProjectileVariant.Fast ? settings.FastProjectileDamage : settings.HeavyProjectileDamage;
            float speed = currentProjectileVariant == ProjectileVariant.Fast ? settings.FastProjectileSpeed : settings.HeavyProjectileSpeed;
            string ownerTag = gameObject.CompareTag("Untagged") ? string.Empty : gameObject.tag;

            projectile.Initialize(
                currentTarget,
                damage,
                speed,
                settings.UseHomingProjectiles,
                ownerTag,
                settings.ProjectileLifetime,
                string.Empty);
        }

        if (currentProjectileVariant == ProjectileVariant.Heavy) ApplyRangedRecoil();
        Log($"Fired {currentProjectileVariant} projectile");
    }

    private bool TryFireRangedProjectile(string source)
    {
        if (currentState != SpinnerState.RangedAttack && currentState != SpinnerState.RangedWindup)
        {
            Log($"Ignored projectile {source}: current state is {currentState}.");
            return false;
        }

        if (projectilesRemaining <= 0)
        {
            StopShootingAnimation();
            Log($"Ignored projectile {source}: no projectiles remaining.");
            return false;
        }

        FireProjectile();
        projectilesRemaining--;

        bool continueShooting = projectilesRemaining > 0;
        if (animationBridge != null)
            animationBridge.SetContinueShooting(continueShooting);

        float fallbackDelay = projectilesDrivenByAnimationEvents && animationBridge != null
            ? Mathf.Max(projectileAnimationEventFallbackDelay, GetProjectileInterval(currentProjectileVariant))
            : GetProjectileInterval(currentProjectileVariant);
        nextProjectileTime = Time.time + fallbackDelay;
        return true;
    }

    private GameObject GetProjectilePrefab(ProjectileVariant variant)
    {
        if (settings == null) return null;

        GameObject preferred = variant == ProjectileVariant.Fast ? settings.FastProjectilePrefab : settings.HeavyProjectilePrefab;
        if (preferred != null) return preferred;

        return variant == ProjectileVariant.Fast ? settings.HeavyProjectilePrefab : settings.FastProjectilePrefab;
    }

    private void ApplyRangedRecoil()
    {
        if (rb == null || rb.isKinematic || settings == null || settings.HeavyShotRecoilForce <= 0f) return;

        Vector3 recoilDir = -transform.forward;
        recoilDir.y = 0f;
        if (recoilDir.sqrMagnitude <= 0.0001f) return;

        rb.AddForce(recoilDir.normalized * settings.HeavyShotRecoilForce, ForceMode.Impulse);
    }

    private void Log(string msg)
    {
        if (enableLogs) Debug.Log($"[ShellSpinner] {msg}", this);
    }

    public void OnProjectileAttackShootFrame()
    {
        if (currentState == SpinnerState.RangedWindup)
            SetState(SpinnerState.RangedAttack);

        TryFireRangedProjectile("animation event");
    }

    public void OnProjectileAttackHitFrame()
    {
        Log("Projectile attack hit frame reached.");
    }

    public void OnProjectileAttackEndFrame()
    {
        StopShootingAnimation();
    }

    public void OnSpinAttackStartFrame()
    {
        if (currentState == SpinnerState.Spinning)
            SetContinueSpinningAnimation(true);
    }

    public void OnSpinAttackHitFrame()
    {
        Log("Spin attack hit frame reached. Spin damage is still applied by collision contact.");
    }

    public void OnSpinAttackEndFrame()
    {
        if (currentState == SpinnerState.Windup || currentState == SpinnerState.Spinning)
        {
            SetContinueSpinningAnimation(true);
            return;
        }

        StopSpinningAnimation();
    }

    public void OnDeathAnimationFinished()
    {
        if (stats != null && stats.IsDead)
            Destroy(gameObject);
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

    private void PlayProjectileAttackAnimation(bool needsTurn, bool continueShooting)
    {
        if (animationBridge != null)
            animationBridge.PlayProjectileAttack(needsTurn, continueShooting);
    }

    private void PlaySpinAttackAnimation(bool continueSpinning)
    {
        if (animationBridge != null)
            animationBridge.PlaySpinAttack(continueSpinning);
    }

    private void SetContinueSpinningAnimation(bool continueSpinning)
    {
        if (animationBridge != null)
            animationBridge.SetContinueSpinning(continueSpinning);
    }

    private void StopSpinningAnimation()
    {
        if (animationBridge != null)
            animationBridge.StopSpinning();
    }

    private void StopShootingAnimation()
    {
        if (animationBridge != null)
            animationBridge.SetContinueShooting(false);
    }

    // Restores all Physics.IgnoreCollision pairs from a SpinUntilWall spin.
    private void RestoreIgnoredColliders()
    {
        if (shellCollider != null)
            foreach (var col in ignoredColliders)
                if (col != null) Physics.IgnoreCollision(shellCollider, col, false);
        ignoredColliders.Clear();
    }

    // Applies a sideways impulse that pushes the target off the spin path.
    private void ApplyKnockback(Transform target)
    {
        float force = settings != null ? settings.KnockbackForce : 8f;
        if (force <= 0f) return;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        Vector3 lateral = toTarget - spinDirection * Vector3.Dot(toTarget, spinDirection);
        Vector3 dir = lateral.sqrMagnitude > 0.0001f ? lateral.normalized : Vector3.Cross(spinDirection, Vector3.up).normalized;

        Rigidbody targetRb = target.GetComponent<Rigidbody>();
        if (targetRb != null && !targetRb.isKinematic)
        {
            targetRb.AddForce(dir * force, ForceMode.Impulse);
            return;
        }

        KnockbackReceiver receiver = target.GetComponent<KnockbackReceiver>();
        if (receiver == null) receiver = target.gameObject.AddComponent<KnockbackReceiver>();
        receiver.AddImpulse(dir * force);
    }

    private void OnDied()
    {
        RestoreIgnoredColliders();

        if (animationBridge != null)
            animationBridge.SetDead(true);

        Destroy(gameObject, deathDestroyDelay);
    }
}
