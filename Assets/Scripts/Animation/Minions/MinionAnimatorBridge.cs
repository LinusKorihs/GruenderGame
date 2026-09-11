using UnityEngine;

[RequireComponent(typeof(Animator))]
public class MinionAnimatorBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("State Names")]
    [SerializeField] private string idleStateName = "AN_M1_Idle";
    [SerializeField] private string walkStateName = "AN_M1_Walk Cycle";
    [SerializeField] private string attackStateName = "AN_M1_Attack Start";
    [SerializeField] private string deathStateName = "AN_M1_Death";

    [Header("Timing")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.05f;
    [SerializeField, Min(0f)] private float attackLockDuration = 0.6f;

    [Header("Debug")]
    [SerializeField] private bool logAnimatorCalls;
    [SerializeField] private bool logSetupWarnings = true;

    private string currentStateName;
    private float actionLockedUntil;
    private bool deathLocked;
    private bool initialized;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnValidate()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    public void PlayIdle()
    {
        if (IsActionLocked()) return;
        PlayState(idleStateName, restart: false);
    }

    public void PlayWalk()
    {
        if (IsActionLocked()) return;
        PlayState(walkStateName, restart: false);
    }

    public void PlayAttack()
    {
        if (deathLocked) return;

        actionLockedUntil = Time.time + attackLockDuration;
        PlayState(attackStateName, restart: true);
    }

    public void PlayDeath()
    {
        deathLocked = true;
        actionLockedUntil = float.PositiveInfinity;
        PlayState(deathStateName, restart: true);
    }

    private bool IsActionLocked()
    {
        return deathLocked || Time.time < actionLockedUntil;
    }

    private void EnsureInitialized()
    {
        if (initialized) return;

        if (animator == null)
            animator = GetComponent<Animator>();

        initialized = true;
    }

    private void PlayState(string stateName, bool restart)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(stateName)) return;
        if (!restart && currentStateName == stateName) return;
        if (!HasUsableAnimator("play state '" + stateName + "'")) return;

        if (!TryFindState(stateName, out int layer, out int stateHash))
        {
            if (logSetupWarnings)
                Debug.LogError("[MinionAnimatorBridge:" + name + "] Cannot play state '" + stateName + "' because it was not found on the Animator Controller.", animator);
            return;
        }

        animator.CrossFadeInFixedTime(stateHash, transitionDuration, layer, 0f);
        currentStateName = stateName;

        if (logAnimatorCalls)
            Debug.Log("[MinionAnimatorBridge:" + name + "] CrossFade state " + stateName + " on layer " + layer + ".", animator);
    }

    private bool HasUsableAnimator(string action)
    {
        if (animator == null)
        {
            if (logSetupWarnings)
                Debug.LogError("[MinionAnimatorBridge:" + name + "] Cannot " + action + ": no Animator assigned.", this);
            return false;
        }

        if (animator.runtimeAnimatorController == null)
        {
            if (logSetupWarnings)
                Debug.LogError("[MinionAnimatorBridge:" + name + "] Cannot " + action + ": Animator has no Runtime Animator Controller.", animator);
            return false;
        }

        if (!animator.enabled)
        {
            if (logSetupWarnings)
                Debug.LogError("[MinionAnimatorBridge:" + name + "] Cannot " + action + ": Animator component is disabled.", animator);
            return false;
        }

        if (!animator.gameObject.activeInHierarchy)
        {
            if (logSetupWarnings)
                Debug.LogError("[MinionAnimatorBridge:" + name + "] Cannot " + action + ": Animator GameObject is inactive.", animator);
            return false;
        }

        return true;
    }

    private bool TryFindState(string stateName, out int layer, out int stateHash)
    {
        layer = -1;
        stateHash = 0;

        if (animator == null || animator.runtimeAnimatorController == null) return false;

        for (int i = 0; i < animator.layerCount; i++)
        {
            int shortHash = Animator.StringToHash(stateName);
            if (animator.HasState(i, shortHash))
            {
                layer = i;
                stateHash = shortHash;
                return true;
            }

            int fullHash = Animator.StringToHash(animator.GetLayerName(i) + "." + stateName);
            if (animator.HasState(i, fullHash))
            {
                layer = i;
                stateHash = fullHash;
                return true;
            }
        }

        return false;
    }
}
