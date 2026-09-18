using UnityEngine;

[RequireComponent(typeof(PlayerMovementCC))]
[RequireComponent(typeof(PlayerAim))]
public class PlayerPunch : MonoBehaviour
{
    [SerializeField] private PlayerConfig config;

    [Header("Punch")]
    private float Cooldown => config.punchCooldown;
    private float Range => config.punchRange;
    private float Radius => config.punchRadius;
    [SerializeField] private LayerMask hitMask;
    //private int Damage => config.damage;
    private float HitboxBufferUpwards => config.hitboxBufferUpwards;

    [Header("Knockback (optional)")]
    private float KnockbackForce => config.knockbackForce;
    private float UpwardKnock => config.upwardKnock;

    [Header("Sound (optional)")]
    [SerializeField] private SoundCue punchSound = new SoundCue("Player.Punch");
    [SerializeField] private SoundCue hitSound = new SoundCue();

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool enableLogs;
    [SerializeField, Min(0.05f)] private float animationEventFallbackDelay = 0.35f;

    private PlayerMovementCC movement;
    private PlayerAim aim;
    private KelpAnimatorBridge kelpAnimator;

    public bool IsPunching => punchLockTimer > 0f;

    private float cooldownTimer;
    private float punchLockTimer;
    private float pendingHitTimer;
    private bool hitPending;
    private Vector3 pendingDirection;
    private readonly System.Collections.Generic.HashSet<int> hitStatsIds = new System.Collections.Generic.HashSet<int>();

    private void Awake()
    {
        movement = GetComponent<PlayerMovementCC>();
        aim = GetComponent<PlayerAim>();
    }

    public void Tick(float dt)
    {
        if (cooldownTimer > 0f) cooldownTimer -= dt;
        if (punchLockTimer > 0f) punchLockTimer -= dt;

        if (hitPending)
        {
            pendingHitTimer -= dt;
            if (pendingHitTimer <= 0f)
            {
                if (enableLogs) Debug.Log("[PlayerPunch] Animation event missing; resolving hit from fallback timer.", this);
                ResolvePendingHit();
            }
        }
    }

    public bool TryPunch(Vector3 dir)
    {
        if (cooldownTimer > 0f) return false;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return false;
        dir.Normalize();

        punchLockTimer = config.punchLockDuration;
        cooldownTimer = Cooldown;
        pendingDirection = dir;
        pendingHitTimer = animationEventFallbackDelay;
        hitPending = true;
        ResolveKelpAnimator()?.PlayPunch();
        punchSound.Play(transform);
        return true;
    }

    public void OnAnimationPunchHit()
    {
        ResolvePendingHit();
    }

    private void ResolvePendingHit()
    {
        if (!hitPending) return;
        hitPending = false;

        Vector3 origin = movement != null && movement.BodyTransform != null
            ? movement.BodyTransform.position
            : transform.position;
        Vector3 center = origin + Vector3.up * HitboxBufferUpwards + pendingDirection * Range;
        Collider[] hits = Physics.OverlapSphere(center, Radius, hitMask, QueryTriggerInteraction.Ignore);
        CombatantStats attackerStats = GetComponentInParent<CombatantStats>();
        float damage = attackerStats != null ? attackerStats.GetStat(CombatStatType.Damage) : 10f;
        int damagedTargets = 0;
        hitStatsIds.Clear();

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            CombatantStats targetStats = EnemyTargetUtility.GetStats(col.transform);
            if (targetStats == null || targetStats == attackerStats || targetStats.IsDead)
                continue;

            int statsId = targetStats.GetInstanceID();
            if (!hitStatsIds.Add(statsId))
                continue;

            float dealtDamage = targetStats.ApplyDamage(damage);
            if (dealtDamage <= 0f)
                continue;

            damagedTargets++;
            Rigidbody targetRb = col.attachedRigidbody != null ? col.attachedRigidbody : col.GetComponentInParent<Rigidbody>();
            if (targetRb != null && !targetRb.isKinematic)
            {
                Vector3 knockback = pendingDirection * KnockbackForce;
                knockback.y += UpwardKnock;
                targetRb.AddForce(knockback, ForceMode.VelocityChange);
            }

            if (enableLogs)
                Debug.Log($"[PlayerPunch] Hit {targetStats.name}: raw={damage:F1}, dealt={dealtDamage:F1}.", targetStats);
        }

        if (enableLogs)
            Debug.Log($"[PlayerPunch] Resolved hit: colliders={hits.Length}, damagedTargets={damagedTargets}, center={center}.", this);

        if (damagedTargets > 0)
            hitSound.Play(transform);
    }

    private Vector3 GetPunchDirection()
    {
        if (aim != null && aim.FacingDirection.sqrMagnitude > 0.0001f) return aim.FacingDirection.normalized;
        if (movement != null && movement.LastMoveDir.sqrMagnitude > 0.0001f) return movement.LastMoveDir.normalized;

        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        return fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Vector3 dir = GetPunchDirection();
        var m = movement != null ? movement : GetComponent<PlayerMovementCC>();
        Vector3 origin = (m != null && m.BodyTransform != null) ? m.BodyTransform.position : transform.position;
        Vector3 center = origin + Vector3.up * HitboxBufferUpwards + dir * Range;

        Gizmos.DrawWireSphere(center, Radius);
        Gizmos.DrawLine(origin + Vector3.up * HitboxBufferUpwards, center);
    }

    private KelpAnimatorBridge ResolveKelpAnimator()
    {
        if (kelpAnimator != null) return kelpAnimator;

        PlayerKelpVisualInstaller installer = GetComponentInParent<PlayerKelpVisualInstaller>();
        if (installer != null && installer.Bridge != null)
        {
            kelpAnimator = installer.Bridge;
        }

        if (kelpAnimator == null)
        {
            kelpAnimator = transform.root.GetComponentInChildren<KelpAnimatorBridge>(true);
        }

        return kelpAnimator;
    }
}
