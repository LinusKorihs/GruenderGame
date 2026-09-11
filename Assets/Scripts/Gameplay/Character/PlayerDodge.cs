using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerMovementCC))]
public class PlayerDodge : MonoBehaviour
{
    [SerializeField] private PlayerConfig config;
    [Header("Dodge / Dash")]
    private float DodgeSpeed => config.dodgeSpeed;
    private float DodgeDuration => config.dodgeDuration;
    private float DodgeCooldown => config.dodgeCooldown;

    public bool IsDodging => dodgeTimer > 0f;
    private float cooldownTimer;
    private float dodgeTimer;

    private PlayerMovementCC movement;
    private KelpAnimatorBridge kelpAnimator;

    private void Awake()
    {
        movement = GetComponent<PlayerMovementCC>();
    }

    public void Tick(float dt)
    {
        if (cooldownTimer > 0f) cooldownTimer -= dt;
        if (dodgeTimer > 0f) dodgeTimer -= dt;
    }

    public bool TryDodge(Vector3 dir)
    {
        if (cooldownTimer > 0f) return false;
        if (dodgeTimer > 0f) return false;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return false;
        dir.Normalize();

        movement.AddExternalVelocity(dir * DodgeSpeed, DodgeDuration);

        dodgeTimer = DodgeDuration;
        cooldownTimer = DodgeCooldown;
        ResolveKelpAnimator()?.PlayDodge();
        return true;
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
