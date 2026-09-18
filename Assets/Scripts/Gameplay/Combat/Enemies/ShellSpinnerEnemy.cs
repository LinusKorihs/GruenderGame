using UnityEngine;
using UnityEngine.UI;

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
    private Mesh targetingStripMesh;
    private MeshRenderer targetingStripRenderer;
    private MeshFilter targetingStripFilter;

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

    [Header("Sound (optional)")]
    [SerializeField] private SoundCue spinStartSound = new SoundCue("Enemy.Spinner.SpinStart");
    [SerializeField] private SoundCue spinHitSound = new SoundCue("Enemy.Spinner.SpinHit");

    [Header("Debug")]
    [SerializeField] private bool enableLogs;
    [SerializeField, Min(0.1f)] private float debugSnapshotInterval = 1f;
    private float nextDebugSnapshotTime;

    private CombatantStats stats;
    private Rigidbody rb;

    private Transform currentTarget;

    [Header("Runtime (Read Only)")]
    [SerializeField] private SpinnerState currentState = SpinnerState.Idle;
    private float stateTimer;

    private bool isAsleep = true;

    private Vector3 spinDirection; // locked when the shell closes; never updated mid-spin
    private Vector3 lastKnownTargetPosition;
    private bool hasLastKnownTargetPosition;
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

        if (targetingLine == null)
            targetingLine = GetComponentInChildren<LineRenderer>(true);

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
            targetingLine.positionCount = Mathf.Max(2, settings != null ? settings.TargetingLineSegments : 10);
            targetingLine.enabled = false;
            InitializeTargetingStrip();
        }
    }

    private void OnDestroy()
    {
        if (stats != null)
        {
            stats.Died -= OnDied;
            stats.DamageTaken -= OnDamageTaken;
        }

        if (targetingStripMesh != null)
            Destroy(targetingStripMesh);
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
        LogDebugSnapshot();
    }

    // Updates the targeting line after transform and physics motion settles.
    private void LateUpdate()
    {
        if (currentState != SpinnerState.Windup || targetingStripRenderer == null || !targetingStripRenderer.enabled) return;
        if (currentTarget != null)
        {
            lastKnownTargetPosition = currentTarget.position;
            hasLastKnownTargetPosition = true;
        }
        if (!hasLastKnownTargetPosition) return;
        int segmentCount = Mathf.Max(2, settings != null ? settings.TargetingLineSegments : 10);
        Vector3 start = transform.position;
        Vector3 end = lastKnownTargetPosition;
        float referenceGroundY = FindReferenceGroundY(start);
        Vector3[] points = new Vector3[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            float t = i / (float)(segmentCount - 1);
            points[i] = SnapToGround(Vector3.Lerp(start, end, t), referenceGroundY);
        }
        UpdateTargetingStrip(points);
    }

    private void InitializeTargetingStrip()
    {
        GameObject stripObject = new GameObject("Ground Targeting Strip");
        stripObject.transform.SetParent(transform, false);
        targetingStripFilter = stripObject.AddComponent<MeshFilter>();
        targetingStripRenderer = stripObject.AddComponent<MeshRenderer>();
        targetingStripRenderer.sharedMaterial = targetingLine.sharedMaterial;
        targetingStripRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        targetingStripRenderer.receiveShadows = false;
        targetingStripRenderer.sortingLayerID = targetingLine.sortingLayerID;
        targetingStripRenderer.sortingOrder = targetingLine.sortingOrder;

        targetingStripMesh = new Mesh { name = "Shell Spinner Ground Targeting Strip" };
        targetingStripMesh.MarkDynamic();
        targetingStripFilter.sharedMesh = targetingStripMesh;
        targetingStripRenderer.enabled = false;
    }

    private void UpdateTargetingStrip(Vector3[] worldPoints)
    {
        if (targetingStripMesh == null || worldPoints == null || worldPoints.Length < 2)
            return;

        int count = worldPoints.Length;
        Vector3[] vertices = new Vector3[count * 2];
        Vector2[] uvs = new Vector2[count * 2];
        int[] triangles = new int[(count - 1) * 6];
        float width = targetingLine != null ? Mathf.Max(0.02f, targetingLine.widthMultiplier) : 0.3f;
        float halfWidth = width * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector3 previous = worldPoints[Mathf.Max(0, i - 1)];
            Vector3 next = worldPoints[Mathf.Min(count - 1, i + 1)];
            Vector3 forward = next - previous;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward.normalized);

            vertices[i * 2] = transform.InverseTransformPoint(worldPoints[i] - side * halfWidth);
            vertices[i * 2 + 1] = transform.InverseTransformPoint(worldPoints[i] + side * halfWidth);
            float v = i / (float)(count - 1);
            uvs[i * 2] = new Vector2(0f, v);
            uvs[i * 2 + 1] = new Vector2(1f, v);

            if (i >= count - 1) continue;
            int triangle = i * 6;
            int vertex = i * 2;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 2;
            triangles[triangle + 2] = vertex + 1;
            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
        }

        targetingStripMesh.Clear();
        targetingStripMesh.vertices = vertices;
        targetingStripMesh.uv = uvs;
        targetingStripMesh.triangles = triangles;
        targetingStripMesh.RecalculateBounds();
    }

    private void SetTargetingIndicatorVisible(bool visible)
    {
        if (targetingLine != null)
            targetingLine.enabled = false;
        if (targetingStripRenderer != null)
            targetingStripRenderer.enabled = visible;
    }

    // Uses the walkable height below the Spinner as the reference. This avoids selecting
    // prop tops or lower geometry when floor and props share the Generated layer.
    private float FindReferenceGroundY(Vector3 worldPos)
    {
        float rayHeight = settings != null ? settings.TargetingLineRayHeight : 8f;
        if (settings == null || settings.GroundMask == 0)
            return transform.position.y;

        Vector3 origin = worldPos + Vector3.up * rayHeight;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, rayHeight + 4f, settings.GroundMask, QueryTriggerInteraction.Ignore);
        bool found = false;
        float bestY = transform.position.y;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].transform.IsChildOf(transform) || hits[i].normal.y < 0.8f || hits[i].point.y > transform.position.y - 0.5f)
                continue;

            float distance = Mathf.Abs(transform.position.y - hits[i].point.y);
            if (distance < bestDistance)
            {
                found = true;
                bestDistance = distance;
                bestY = hits[i].point.y;
            }
        }
        return found ? bestY : transform.position.y;
    }

    // Projects a world-space point onto the surface closest to the arena floor height.
    private Vector3 SnapToGround(Vector3 worldPos, float referenceGroundY)
    {
        float offset = settings != null ? settings.TargetingLineGroundOffset : 0.04f;
        float rayHeight = settings != null ? settings.TargetingLineRayHeight : 8f;
        if (settings != null && settings.GroundMask != 0)
        {
            Vector3 origin = worldPos + Vector3.up * rayHeight;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, rayHeight + 2f, settings.GroundMask, QueryTriggerInteraction.Ignore);
            bool foundGround = false;
            RaycastHit groundHit = default;
            float bestHeightDelta = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].transform.IsChildOf(transform) || hits[i].normal.y < 0.8f)
                    continue;
                float heightDelta = Mathf.Abs(hits[i].point.y - referenceGroundY);
                if (!foundGround || heightDelta < bestHeightDelta)
                {
                    foundGround = true;
                    groundHit = hits[i];
                    bestHeightDelta = heightDelta;
                }
            }
            if (foundGround)
                return groundHit.point + Vector3.up * offset;
        }
        return new Vector3(worldPos.x, referenceGroundY + offset, worldPos.z);
    }

    private void FixedUpdate()
    {
        if (rb == null || stats == null || stats.IsDead) return;

        if (currentState == SpinnerState.Spinning && !spinHitSomething)
        {
            ProcessSpinActorSweep(frameVelocity);

            if (WouldSpinHitBlocker(frameVelocity, out RaycastHit blockerHit))
            {
                frameVelocity = Vector3.zero;
                RecoverFromPredictedBlocker(blockerHit);
                spinHitSomething = true;
            }
        }

        rb.linearVelocity = new Vector3(frameVelocity.x, rb.linearVelocity.y, frameVelocity.z);
    }

    private void ProcessSpinActorSweep(Vector3 velocity)
    {
        if (shellCollider == null || settings == null)
            return;

        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        float speed = horizontalVelocity.magnitude;
        if (speed <= 0.001f)
            return;

        Bounds bounds = shellCollider.bounds;
        float radius = Mathf.Max(0.1f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f);
        float distance = speed * Time.fixedDeltaTime + settings.SpinCollisionSkin;
        Vector3 direction = horizontalVelocity / speed;

        Collider[] overlaps = Physics.OverlapSphere(bounds.center, radius, settings.DetectMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            if (TryApplySpinHit(overlaps[i], "overlap") && !settings.SpinUntilWall)
            {
                spinHitSomething = true;
                return;
            }
        }

        RaycastHit[] hits = Physics.SphereCastAll(bounds.center, radius, direction, distance, settings.DetectMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            if (TryApplySpinHit(hits[i].collider, "sweep") && !settings.SpinUntilWall)
            {
                spinHitSomething = true;
                return;
            }
        }
    }

    private bool TryApplySpinHit(Collider hitCollider, string source)
    {
        if (hitCollider == null || hitCollider.transform.IsChildOf(transform) || settings == null)
            return false;

        Transform player = EnemyTargetUtility.FindTaggedActor(hitCollider.transform, settings.PlayerTag);
        Transform minion = EnemyTargetUtility.FindTaggedActor(hitCollider.transform, settings.MinionTag);
        bool isPlayer = player != null;
        bool isMinion = minion != null;
        if (!isPlayer && !isMinion)
            return false;

        Transform statsRoot = isPlayer && playerTransform != null ? playerTransform : hitCollider.transform;
        CombatantStats targetStats = GetStats(statsRoot);
        if (targetStats == null || targetStats.IsDead)
        {
            Log($"Spin damage ignored ({source}): target={(targetStats != null ? targetStats.name : hitCollider.name)}, deadOrMissing=true");
            return false;
        }

        int id = targetStats.GetInstanceID();
        if (!spinHitIds.Add(id))
        {
            Log($"Spin damage ignored ({source}): target={targetStats.name}, alreadyHit=true");
            return false;
        }

        if (settings.SpinUntilWall && shellCollider != null && hitCollider != shellCollider && !ignoredColliders.Contains(hitCollider))
        {
            Physics.IgnoreCollision(shellCollider, hitCollider, true);
            ignoredColliders.Add(hitCollider);
        }

        float requestedDamage = GetSpinDamage(targetStats, isMinion);
        float dealtDamage = targetStats.ApplyDamage(requestedDamage);
        if (dealtDamage > 0f)
            spinHitSound.Play(transform);
        Transform knockbackTarget = isPlayer && playerTransform != null ? playerTransform : targetStats.transform;
        ApplyKnockback(knockbackTarget);
        Log($"Spin damage applied ({source}): target={targetStats.name}, requested={requestedDamage:F1}, dealt={dealtDamage:F1}, collider={hitCollider.name}");
        return true;
    }

    private bool WouldSpinHitBlocker(Vector3 velocity, out RaycastHit blockerHit)
    {
        blockerHit = default;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        float speed = horizontalVelocity.magnitude;
        if (speed <= 0.001f || settings == null || settings.SpinBlockMask == 0)
            return false;

        float distance = speed * Time.fixedDeltaTime + settings.SpinCollisionSkin;
        RaycastHit[] hits = rb.SweepTestAll(horizontalVelocity / speed, distance, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
                continue;

            Transform player = EnemyTargetUtility.FindTaggedActor(hitCollider.transform, settings.PlayerTag);
            Transform minion = EnemyTargetUtility.FindTaggedActor(hitCollider.transform, settings.MinionTag);
            if (player != null || minion != null)
                continue;

            if ((settings.SpinBlockMask.value & (1 << hitCollider.gameObject.layer)) != 0)
            {
                blockerHit = hits[i];
                Log($"Predictive spin block: collider={hitCollider.name}, layer={LayerMask.LayerToName(hitCollider.gameObject.layer)}, distance={hits[i].distance:F2}");
                return true;
            }
        }

        return false;
    }

    private void RecoverFromPredictedBlocker(RaycastHit blockerHit)
    {
        float recoveryDistance = settings != null ? settings.SpinWallRecoveryDistance : 0.5f;
        if (recoveryDistance <= 0f)
            return;

        Vector3 away = blockerHit.normal;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
            away = -spinDirection;
        away.Normalize();

        float safeDistance = recoveryDistance;
        if (rb != null && settings != null)
        {
            RaycastHit[] recoveryHits = rb.SweepTestAll(away, recoveryDistance, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < recoveryHits.Length; i++)
            {
                Collider recoveryCollider = recoveryHits[i].collider;
                if (recoveryCollider == null || recoveryCollider == blockerHit.collider || recoveryCollider.transform.IsChildOf(transform))
                    continue;
                if ((settings.SpinBlockMask.value & (1 << recoveryCollider.gameObject.layer)) == 0)
                    continue;

                safeDistance = Mathf.Min(safeDistance, Mathf.Max(0f, recoveryHits[i].distance - settings.SpinCollisionSkin));
            }
        }

        Vector3 before = rb != null ? rb.position : transform.position;
        Vector3 after = before + away * safeDistance;
        if (rb != null)
            rb.position = after;
        else
            transform.position = after;

        Log($"Wall recovery: blocker={blockerHit.collider.name}, from={before}, to={after}, away={away}, requested={recoveryDistance:F2}, applied={safeDistance:F2}");
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (currentState != SpinnerState.Spinning) return;

        Transform player = EnemyTargetUtility.FindTaggedActor(collision.transform, settings.PlayerTag);
        Transform minion = EnemyTargetUtility.FindTaggedActor(collision.transform, settings.MinionTag);
        bool isPlayer = player != null;
        bool isMinion = minion != null;

        if (isPlayer || isMinion)
        {
            bool hit = TryApplySpinHit(collision.collider, "collision");
            if (hit && (settings == null || !settings.SpinUntilWall))
                spinHitSomething = true;
            return;
        }

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

        Log($"Spin collision: blocker={collision.collider.name}, contacts={collision.contactCount}, position={transform.position}");
        ResolveBlockingOverlap(collision.collider);
        spinHitSomething = true;
    }

    private void ResolveBlockingOverlap(Collider blocker)
    {
        if (shellCollider == null || blocker == null)
            return;

        if (!Physics.ComputePenetration(
                shellCollider, shellCollider.transform.position, shellCollider.transform.rotation,
                blocker, blocker.transform.position, blocker.transform.rotation,
                out Vector3 separationDirection, out float separationDistance))
            return;

        Vector3 correction = separationDirection * (separationDistance + (settings != null ? settings.SpinCollisionSkin : 0.05f));
        Log($"Resolved blocker penetration: blocker={blocker.name}, distance={separationDistance:F3}, correction={correction}");
        if (rb != null)
            rb.position += correction;
        else
            transform.position += correction;
    }

    private static CombatantStats GetStats(Transform t)
    {
        return EnemyTargetUtility.GetStats(t);
    }

    private float GetSpinDamage(CombatantStats targetStats, bool isMinion)
    {
        if (targetStats != null && isMinion && settings != null && settings.OneShotMinionsOnSpin)
        {
            return targetStats.GetStat(CombatStatType.MaxHealth) * targetStats.GetStat(CombatStatType.Defense);
        }

        return settings != null ? settings.SpinDamage : 18f;
    }

    private void OnDamageTaken(float _)
    {
        isAsleep = false;
    }

    private void RefreshTarget()
    {
        bool activeAttackCycle = currentState != SpinnerState.Idle;
        float baseForgetRadius = settings != null ? settings.ForgetRadius : 18f;
        float encounterForgetRadius = settings != null && settings.EncounterForgetRadius > 0f
            ? settings.EncounterForgetRadius
            : baseForgetRadius;
        float forgetRadius = !isAsleep ? encounterForgetRadius : baseForgetRadius;

        if (currentTarget != null)
        {
            CombatantStats ts = GetStats(currentTarget);
            bool dead = ts != null && ts.IsDead;
            bool retainForCycle = settings != null && settings.RetainTargetDuringAttackCycle && activeAttackCycle;
            bool far = !retainForCycle && HorizontalDistance(currentTarget.position) > forgetRadius;

            if (dead || far || !currentTarget.gameObject.activeInHierarchy)
            {
                Log($"Target lost: name={currentTarget.name}, dead={dead}, far={far}, distance={HorizontalDistance(currentTarget.position):F2}, forgetRadius={forgetRadius:F2}, state={currentState}");
                currentTarget = null;
            }
            else
            {
                lastKnownTargetPosition = currentTarget.position;
                hasLastKnownTargetPosition = true;
            }
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
                FaceTarget();

                if (stateTimer <= 0f && IsFacingTarget())
                {
                    if (currentTarget != null)
                    {
                        lastKnownTargetPosition = currentTarget.position;
                        hasLastKnownTargetPosition = true;
                    }
                    Vector3 dir = hasLastKnownTargetPosition
                        ? lastKnownTargetPosition - transform.position
                        : transform.forward;
                    dir.y = 0f;
                    spinDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
                    spinHitSomething = false;
                    spinHitIds.Clear();
                    Log($"Spin locked: direction={spinDirection}, target={(currentTarget != null ? currentTarget.name : "none")}, lastKnown={lastKnownTargetPosition}");
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
                if (stateTimer <= 0f && IsFacingTarget()) SetState(SpinnerState.RangedAttack);
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

    private bool IsFacingTarget(float maximumAngle = 6f)
    {
        if (currentTarget == null) return false;
        Vector3 direction = currentTarget.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return true;

        return Vector3.Angle(transform.forward, direction.normalized) <= maximumAngle;
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

        SetTargetingIndicatorVisible(false);

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
                if (currentTarget != null)
                {
                    lastKnownTargetPosition = currentTarget.position;
                    hasLastKnownTargetPosition = true;
                }
                if (targetingLine != null)
                {
                    SetTargetingIndicatorVisible(true);
                }
                break;

            case SpinnerState.Spinning:
                spinStartSound.Play(transform);
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

        GameObject projectileObject = Instantiate(prefab, spawnPos, spawnRot);
        SoundManager.TryPlayId("Enemy.Spinner.Shoot", transform);
        float scaleMultiplier = settings != null ? Mathf.Max(0.01f, settings.ProjectileScaleMultiplier) : 1f;
        projectileObject.transform.localScale *= scaleMultiplier;

        SphereCollider projectileCollider = projectileObject.GetComponent<SphereCollider>();
        if (projectileCollider != null && settings != null)
            projectileCollider.radius *= Mathf.Clamp(settings.ProjectileHitboxRadiusMultiplier, 0.05f, 1f);

        Log($"Projectile spawned: variant={currentProjectileVariant}, target={(currentTarget != null ? currentTarget.name : "none")}, " +
            $"position={spawnPos}, scale={projectileObject.transform.lossyScale}, colliderRadius={(projectileCollider != null ? projectileCollider.bounds.extents.x : 0f):F2}");

        MinionProjectile projectile = projectileObject.GetComponent<MinionProjectile>();
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
            if (settings.ProjectileTrailWidthOverride > 0f)
                projectile.SetVisibilityTrailWidth(settings.ProjectileTrailWidthOverride);
        }

        Renderer projectileRenderer = FindProjectileVisualRenderer(projectileObject);
        if (enableLogs && projectileCollider != null && projectileRenderer != null)
        {
            Vector3 colliderSize = projectileCollider.bounds.size;
            Vector3 rendererSize = projectileRenderer.bounds.size;
            Log($"Projectile bounds: variant={currentProjectileVariant}, renderer={rendererSize}, collider={colliderSize}, " +
                $"radiusRatio={(rendererSize.x > 0.001f ? projectileCollider.bounds.extents.x / (rendererSize.x * 0.5f) : 0f):F2}");
        }

        if (currentProjectileVariant == ProjectileVariant.Heavy) ApplyRangedRecoil();
        Log($"Fired {currentProjectileVariant} projectile");
    }

    private static Renderer FindProjectileVisualRenderer(GameObject projectileObject)
    {
        Renderer[] renderers = projectileObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] is TrailRenderer || renderers[i] is ParticleSystemRenderer)
                continue;
            return renderers[i];
        }
        return null;
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

        if (!IsFacingTarget())
        {
            nextProjectileTime = Time.time + 0.05f;
            Log($"Delayed projectile {source}: boss is still turning towards the target.");
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
        if (currentState == SpinnerState.Windup)
            return;

        if (currentState == SpinnerState.Spinning)
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

    private void LogDebugSnapshot()
    {
        if (!enableLogs || Time.time < nextDebugSnapshotTime)
            return;

        nextDebugSnapshotTime = Time.time + Mathf.Max(0.1f, debugSnapshotInterval);
        Vector3 velocity = rb != null ? rb.linearVelocity : frameVelocity;
        Log($"Snapshot state={currentState}, timer={stateTimer:F2}, target={(currentTarget != null ? currentTarget.name : "none")}, " +
            $"position={transform.position}, velocity={velocity}, spinHit={spinHitSomething}, projectiles={projectilesRemaining}");
    }
}

[DisallowMultipleComponent]
public sealed class BossEncounterController : MonoBehaviour
{
    [SerializeField] private CombatantStats bossStats;
    [SerializeField] private bool showVictoryScreenOnDeath = true;
    [SerializeField] private string victoryTitle = "Victory";
    [SerializeField] private string victorySubtitle = "Boss defeated";

    private GameplayHUDController hud;
    private bool completed;

    private void OnEnable()
    {
        StartCoroutine(StartBossMusicWhenVisible());
        ResolveBossStats();
        Subscribe();
        TryBindHud();
    }

    private System.Collections.IEnumerator StartBossMusicWhenVisible()
    {
        yield return new WaitForEndOfFrame();
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayBossStart();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (completed)
            return;

        TryBindHud();
    }

    public void Bind(GameObject bossRoot)
    {
        Unsubscribe();
        bossStats = bossRoot != null ? bossRoot.GetComponentInChildren<CombatantStats>(true) : null;
        Subscribe();
        TryBindHud();
    }

    private void ResolveBossStats()
    {
        if (bossStats == null)
            bossStats = GetComponentInChildren<CombatantStats>(true);
    }

    private void Subscribe()
    {
        if (bossStats != null)
            bossStats.Died += HandleBossDied;
    }

    private void Unsubscribe()
    {
        if (bossStats != null)
            bossStats.Died -= HandleBossDied;
    }

    private void TryBindHud()
    {
        if (bossStats == null)
            return;

        if (hud == null)
            hud = FindFirstObjectByType<GameplayHUDController>(FindObjectsInactive.Include);

        if (hud != null)
            hud.BindBoss(bossStats);
    }

    private void HandleBossDied()
    {
        if (completed)
            return;

        completed = true;
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayBossWin();
        TryBindHud();

        LevelFlowController flow = LevelFlowController.Instance;
        if (flow != null)
            flow.AdvanceAfterCurrentLevelExit();

        if (showVictoryScreenOnDeath)
            ShowVictoryScreen();
    }

    private void ShowVictoryScreen()
    {
        const string overlayName = "VictoryScreen_Runtime";
        if (GameObject.Find(overlayName) != null)
            return;

        GameObject root = new GameObject(overlayName);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0.02f, 0.02f, 0.03f, 0.82f);

        CreateLabel(panel.transform, victoryTitle, 72, new Vector2(0f, 48f), FontStyle.Bold);
        CreateLabel(panel.transform, victorySubtitle, 32, new Vector2(0f, -40f), FontStyle.Normal);
    }

    private static void CreateLabel(Transform parent, string text, int fontSize, Vector2 anchoredPosition, FontStyle style)
    {
        GameObject label = new GameObject(string.IsNullOrWhiteSpace(text) ? "Label" : text);
        label.transform.SetParent(parent, false);

        RectTransform rect = label.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(900f, 120f);
        rect.anchoredPosition = anchoredPosition;

        Text uiText = label.AddComponent<Text>();
        uiText.text = text;
        uiText.alignment = TextAnchor.MiddleCenter;
        uiText.fontSize = fontSize;
        uiText.fontStyle = style;
        uiText.color = Color.white;
        uiText.raycastTarget = false;
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                      ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
