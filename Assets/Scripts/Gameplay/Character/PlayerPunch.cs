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

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private PlayerMovementCC movement;
    private PlayerAim aim;
    private KelpAnimatorBridge kelpAnimator;

    public bool IsPunching => punchLockTimer > 0f;

    private float cooldownTimer;
    private float punchLockTimer;

    private void Awake()
    {
        movement = GetComponent<PlayerMovementCC>();
        aim = GetComponent<PlayerAim>();
    }

    public void Tick(float dt)
    {
        if (cooldownTimer > 0f) cooldownTimer -= dt;
        if (punchLockTimer > 0f) punchLockTimer -= dt;
    }

    public bool TryPunch(Vector3 dir)
    {
        if (cooldownTimer > 0f) return false;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return false;
        dir.Normalize();

        Vector3 origin = movement != null ? movement.BodyTransform.position : transform.position;
        Vector3 center = origin + Vector3.up * HitboxBufferUpwards + dir * Range;


        Collider[] hits = Physics.OverlapSphere(center, Radius, hitMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            Rigidbody rb = col.attachedRigidbody != null ? col.attachedRigidbody : col.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                Vector3 kb = dir * KnockbackForce;
                if (UpwardKnock != 0f) kb.y += UpwardKnock;

                rb.AddForce(kb, ForceMode.VelocityChange);
            }
        }

        punchLockTimer = config.punchLockDuration;
        cooldownTimer = Cooldown;
        ResolveKelpAnimator()?.PlayPunch();
        return true;
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
