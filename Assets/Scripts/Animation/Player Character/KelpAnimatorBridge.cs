using UnityEngine;

[RequireComponent(typeof(Animator))]
public class KelpAnimatorBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Animator Parameter Names")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string punchTrigger = "Punch";
    [SerializeField] private string callMinionsTrigger = "CallMinions";
    [SerializeField] private string orderMinionsTrigger = "OrderMinions";
    [SerializeField] private string dismissTrigger = "Dismiss";
    [SerializeField] private string dodgeTrigger = "Dodge";
    [SerializeField] private string interactTrigger = "Interact";
    [SerializeField] private string idleBreakTrigger = "IdleBreak";
    [SerializeField] private string isDeadParameter = "IsDead";

    [Header("State Names")]
    [SerializeField] private string idleStateName = "Kelp_Idle";
    [SerializeField] private string deathStateName = "Kelp_Death";

    private int speedHash;
    private int punchHash;
    private int callMinionsHash;
    private int orderMinionsHash;
    private int dismissHash;
    private int dodgeHash;
    private int interactHash;
    private int idleBreakHash;
    private int isDeadHash;

    private bool hasSpeed;
    private bool hasPunch;
    private bool hasCallMinions;
    private bool hasOrderMinions;
    private bool hasDismiss;
    private bool hasDodge;
    private bool hasInteract;
    private bool hasIdleBreak;
    private bool hasIsDead;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        CacheHashes();
        CheckAvailableParameters();
    }

    private void OnValidate()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }

    private void CacheHashes()
    {
        speedHash = Animator.StringToHash(speedParameter);
        punchHash = Animator.StringToHash(punchTrigger);
        callMinionsHash = Animator.StringToHash(callMinionsTrigger);
        orderMinionsHash = Animator.StringToHash(orderMinionsTrigger);
        dismissHash = Animator.StringToHash(dismissTrigger);
        dodgeHash = Animator.StringToHash(dodgeTrigger);
        interactHash = Animator.StringToHash(interactTrigger);
        idleBreakHash = Animator.StringToHash(idleBreakTrigger);
        isDeadHash = Animator.StringToHash(isDeadParameter);
    }

    private void CheckAvailableParameters()
    {
        hasSpeed = HasParameter(speedParameter, AnimatorControllerParameterType.Float);
        hasPunch = HasParameter(punchTrigger, AnimatorControllerParameterType.Trigger);
        hasCallMinions = HasParameter(callMinionsTrigger, AnimatorControllerParameterType.Trigger);
        hasOrderMinions = HasParameter(orderMinionsTrigger, AnimatorControllerParameterType.Trigger);
        hasDismiss = HasParameter(dismissTrigger, AnimatorControllerParameterType.Trigger);
        hasDodge = HasParameter(dodgeTrigger, AnimatorControllerParameterType.Trigger);
        hasInteract = HasParameter(interactTrigger, AnimatorControllerParameterType.Trigger);
        hasIdleBreak = HasParameter(idleBreakTrigger, AnimatorControllerParameterType.Trigger);
        hasIsDead = HasParameter(isDeadParameter, AnimatorControllerParameterType.Bool);
    }

    private bool HasParameter(string parameterName, AnimatorControllerParameterType expectedType)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("KelpAnimatorBridge: Kein Animator oder kein Animator Controller gefunden.");
            return false;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName && parameter.type == expectedType)
            {
                return true;
            }
        }

        return false;
    }

    public void SetSpeed(float speed)
    {
        if (!hasSpeed)
        {
            Debug.LogWarning("KelpAnimatorBridge: Animator Parameter fehlt oder ist kein Float: " + speedParameter);
            return;
        }

        animator.SetFloat(speedHash, speed);
    }

    public void PlayPunch()
    {
        FireTrigger(punchHash, hasPunch, punchTrigger);
    }

    public void PlayCallMinions()
    {
        FireTrigger(callMinionsHash, hasCallMinions, callMinionsTrigger);
    }

    public void PlayOrderMinions()
    {
        FireTrigger(orderMinionsHash, hasOrderMinions, orderMinionsTrigger);
    }

    public void PlayDismiss()
    {
        FireTrigger(dismissHash, hasDismiss, dismissTrigger);
    }

    public void PlayDodge()
    {
        FireTrigger(dodgeHash, hasDodge, dodgeTrigger);
    }

    public void PlayInteract()
    {
        FireTrigger(interactHash, hasInteract, interactTrigger);
    }

    public void PlayIdleBreak()
    {
        FireTrigger(idleBreakHash, hasIdleBreak, idleBreakTrigger);
    }

    public void SetDead(bool isDead)
    {
        if (!hasIsDead)
        {
            Debug.LogWarning("KelpAnimatorBridge: Animator Parameter fehlt oder ist kein Bool: " + isDeadParameter);
            return;
        }

        if (isDead)
        {
            ResetAllTriggers();

            if (hasSpeed)
            {
                animator.SetFloat(speedHash, 0f);
            }
        }

        animator.SetBool(isDeadHash, isDead);
    }

    public bool IsDeathAnimationComplete(float normalizedThreshold = 0.96f)
    {
        if (animator == null || string.IsNullOrWhiteSpace(deathStateName))
            return false;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsName(deathStateName) && !animator.IsInTransition(0) &&
               state.normalizedTime >= Mathf.Clamp01(normalizedThreshold);
    }

    public void ResetToIdle()
    {
        ResetAllTriggers();

        if (hasSpeed)
        {
            animator.SetFloat(speedHash, 0f);
        }

        if (hasIsDead)
        {
            animator.SetBool(isDeadHash, false);
        }

        if (!string.IsNullOrEmpty(idleStateName))
        {
            animator.Play(idleStateName, 0, 0f);
            animator.Update(0f);
        }
    }

    public void ResetAllTriggers()
    {
        if (hasPunch) animator.ResetTrigger(punchHash);
        if (hasCallMinions) animator.ResetTrigger(callMinionsHash);
        if (hasOrderMinions) animator.ResetTrigger(orderMinionsHash);
        if (hasDismiss) animator.ResetTrigger(dismissHash);
        if (hasDodge) animator.ResetTrigger(dodgeHash);
        if (hasInteract) animator.ResetTrigger(interactHash);
        if (hasIdleBreak) animator.ResetTrigger(idleBreakHash);
    }

    private void FireTrigger(int triggerHash, bool hasTrigger, string triggerName)
    {
        if (!hasTrigger)
        {
            Debug.LogWarning("KelpAnimatorBridge: Animator Trigger fehlt: " + triggerName);
            return;
        }

        ResetAllTriggers();
        animator.SetTrigger(triggerHash);
    }
}
