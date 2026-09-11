using UnityEngine;

// Set IsTrigger on the attached Collider so it registers hits on contact.
[RequireComponent(typeof(Collider), typeof(Rigidbody))]
public class MinionProjectile : MonoBehaviour
{
    [Header("Collision")]
    [Tooltip("Layers treated as solid walls that destroy this projectile on contact (e.g. Default, Walls). Exclude character layers.")]
    [SerializeField] private LayerMask wallBlockMask = ~0;

    private Transform target;
    private IAimTarget aimTargetOverride; // optional: enemy that redirects the aim point
    private float damage;
    private float speed;
    private bool homing;
    private string ownerTag;    // tag of the team that fired this (ignored on hit)
    private string playerTag;   // tag of the player — projectiles pass through them
    private float lifetime;
    private float spawnTime;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    // Called immediately after Instantiate to configure the projectile. Owner tag is optional but prevents the projectile from hitting the shooter team.
    public void Initialize(
        Transform target,
        float damage,
        float speed,
        bool homing,
        string ownerTag,
        float lifetime = 5f,
        string playerTag = "Player")
    {
        this.target    = target;
        aimTargetOverride = target != null ? target.GetComponent<IAimTarget>() : null;
        this.damage    = damage;
        this.speed     = Mathf.Max(0.5f, speed);
        this.homing    = homing;
        this.ownerTag  = ownerTag;
        this.playerTag = playerTag;
        this.lifetime  = Mathf.Max(0.1f, lifetime);
        spawnTime      = Time.time;

        // Face the target immediately on spawn.
        if (target != null)
        {
            Vector3 dir = GetAimPosition() - transform.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }
    }

    // Returns the world-space point projectiles should steer toward.
    // Uses IAimTarget if the target implements it (e.g. burrowed enemy with a visible head).
    private Vector3 GetAimPosition()
    {
        if (aimTargetOverride != null)
        {
            Transform aimT = aimTargetOverride.GetAimTransform();
            if (aimT != null) return aimT.position;
        }
        return target != null ? target.position : transform.position;
    }

    private void Update()
    {
        if (Time.time - spawnTime >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Homing: steer smoothly toward the current aim position each frame.
        if (homing && target != null && target.gameObject.activeInHierarchy)
        {
            Vector3 dir = GetAimPosition() - transform.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 10f * Time.deltaTime);
            }
        }

        float stepDist = speed * Time.deltaTime;

        // Raycast ahead before moving so the projectile cannot tunnel through thin walls.
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit wallHit, stepDist + 0.05f, wallBlockMask, QueryTriggerInteraction.Ignore))
        {
            // Make sure we didn’t hit the owner team or the current target.
            bool hitSelf   = !string.IsNullOrEmpty(ownerTag) && wallHit.collider.CompareTag(ownerTag);
            bool hitTarget = target != null && (wallHit.transform == target || wallHit.transform.IsChildOf(target));
            if (!hitSelf && !hitTarget)
            {
                transform.position = wallHit.point;
                Destroy(gameObject);
                return;
            }
        }

        // Travel forward.
        transform.position += transform.forward * stepDist;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        // Skip colliders on the same team as the shooter.
        // Check both the collider's own tag and its root so child colliders are covered.
        if (!string.IsNullOrEmpty(ownerTag) &&
            (other.CompareTag(ownerTag) || other.transform.root.CompareTag(ownerTag))) return;

        // Minion projectiles must not damage the player — pass straight through.
        if (!string.IsNullOrEmpty(playerTag) &&
            (other.CompareTag(playerTag) || other.transform.root.CompareTag(playerTag))) return;

        CombatantStats stats = EnemyTargetUtility.GetStats(other.transform);
        if (stats == null) return;   // No damageable target — pass through (triggers, environment, etc.)

        if (!stats.IsDead)
        {
            stats.ApplyDamage(damage);
        }

        Destroy(gameObject);
    }
}
