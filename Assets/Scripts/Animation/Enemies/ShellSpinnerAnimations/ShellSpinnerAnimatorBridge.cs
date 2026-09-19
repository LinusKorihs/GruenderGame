using UnityEngine;

[RequireComponent(typeof(Animator))]
public class ShellSpinnerAnimatorBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Animator Parameter Names")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string projectileAttackTrigger = "ProjectileAttack";
    [SerializeField] private string spinAttackTrigger = "SpinAttack";
    [SerializeField] private string idleBreakTrigger = "IdleBreak";
    [SerializeField] private string isDeadParameter = "IsDead";
    [SerializeField] private string needsTurnParameter = "NeedsTurn";
    [SerializeField] private string continueShootingParameter = "ContinueShooting";
    [SerializeField] private string continueSpinningParameter = "ContinueSpinning";

    [Header("Debug")]
    [SerializeField] private bool logAnimatorCalls = true;
    [SerializeField] private bool logSetupWarnings = true;

    [Header("Direct State Playback")]
    [Tooltip("Also cross-fades directly to states after setting parameters. Useful when triggers are called but transitions do not visibly play.")]
    [SerializeField] private bool playStatesDirectly = true;
    [SerializeField, Min(0f)] private float directTransitionDuration = 0.05f;
    [SerializeField] private string idleStateName = "E2_Idle";
    [SerializeField] private string projectileAttackState = "E2_ProjectilAttack_1_3";
    [SerializeField] private string spinAttackState = "E2_SpinAttack_1_4";
    [SerializeField] private string idleBreakState = "E2_IdleBreak";
    [SerializeField] private string deathState = "E2_Death";

    private int speedHash;
    private int projectileAttackHash;
    private int spinAttackHash;
    private int idleBreakHash;
    private int isDeadHash;
    private int needsTurnHash;
    private int continueShootingHash;
    private int continueSpinningHash;

    private bool hasSpeed;
    private bool hasProjectileAttack;
    private bool hasSpinAttack;
    private bool hasIdleBreak;
    private bool hasIsDead;
    private bool hasNeedsTurn;
    private bool hasContinueShooting;
    private bool hasContinueSpinning;

    private bool initialized;
    private float lastLoggedSpeed = float.NaN;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnValidate()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    public void SetSpeed(float speed)
    {
        EnsureInitialized();
        if (!CanUseParameter(speedParameter, AnimatorControllerParameterType.Float, hasSpeed, "set Speed")) return;

        animator.SetFloat(speedHash, speed);

        if (logAnimatorCalls && (float.IsNaN(lastLoggedSpeed) || !Mathf.Approximately(lastLoggedSpeed, speed)))
        {
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] Speed = " + speed.ToString("0.###"), this);
            lastLoggedSpeed = speed;
        }
    }

    public void SetNeedsTurn(bool needsTurn)
    {
        EnsureInitialized();
        if (!CanUseParameter(needsTurnParameter, AnimatorControllerParameterType.Bool, hasNeedsTurn, "set NeedsTurn")) return;

        animator.SetBool(needsTurnHash, needsTurn);

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] NeedsTurn = " + needsTurn, this);
    }

    public void SetContinueShooting(bool continueShooting)
    {
        EnsureInitialized();
        if (!CanUseParameter(continueShootingParameter, AnimatorControllerParameterType.Bool, hasContinueShooting, "set ContinueShooting")) return;

        animator.SetBool(continueShootingHash, continueShooting);

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] ContinueShooting = " + continueShooting, this);
    }

    public void SetContinueSpinning(bool continueSpinning)
    {
        EnsureInitialized();
        if (!CanUseParameter(continueSpinningParameter, AnimatorControllerParameterType.Bool, hasContinueSpinning, "set ContinueSpinning")) return;

        animator.SetBool(continueSpinningHash, continueSpinning);

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] ContinueSpinning = " + continueSpinning, this);
    }

    public void PlayProjectileAttack()
    {
        PlayProjectileAttack(false, false);
    }

    public void PlayProjectileAttack(bool needsTurn)
    {
        PlayProjectileAttack(needsTurn, false);
    }

    public void PlayProjectileAttack(bool needsTurn, bool continueShooting)
    {
        EnsureInitialized();
        if (!CanUseParameter(projectileAttackTrigger, AnimatorControllerParameterType.Trigger, hasProjectileAttack, "play ProjectileAttack")) return;

        if (hasNeedsTurn)
            animator.SetBool(needsTurnHash, needsTurn);

        if (hasContinueShooting)
            animator.SetBool(continueShootingHash, continueShooting);

        ResetAttackTriggers();
        if (playStatesDirectly)
        {
            // As with the spin attack, leaving the trigger pending after a direct crossfade
            // can enter the first projectile state a second time and replay the head tuck.
            PlayStateDirectly(projectileAttackState);
            animator.ResetTrigger(projectileAttackHash);
        }
        else
        {
            animator.SetTrigger(projectileAttackHash);
        }

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] Start ProjectileAttack via " + (playStatesDirectly ? "direct state" : "trigger"), this);
    }

    public void PlaySpinAttack()
    {
        PlaySpinAttack(false);
    }

    public void PlaySpinAttack(bool continueSpinning)
    {
        EnsureInitialized();
        if (!CanUseParameter(spinAttackTrigger, AnimatorControllerParameterType.Trigger, hasSpinAttack, "play SpinAttack")) return;

        if (hasContinueSpinning)
            animator.SetBool(continueSpinningHash, continueSpinning);

        ResetAttackTriggers();
        if (playStatesDirectly)
        {
            // Direct playback and a pending trigger both enter the first spin state.
            // Using both restarts the tuck-in animation once the trigger transition fires.
            PlayStateDirectly(spinAttackState);
            animator.ResetTrigger(spinAttackHash);
        }
        else
        {
            animator.SetTrigger(spinAttackHash);
        }

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] Start SpinAttack via " + (playStatesDirectly ? "direct state" : "trigger"), this);
    }

    public void StopSpinning()
    {
        EnsureInitialized();
        if (hasContinueSpinning)
            animator.SetBool(continueSpinningHash, false);

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] Stop spinning", this);
    }

    public void PlayIdleBreak()
    {
        EnsureInitialized();
        if (!CanUseParameter(idleBreakTrigger, AnimatorControllerParameterType.Trigger, hasIdleBreak, "play IdleBreak")) return;

        ResetAttackTriggers();
        animator.SetTrigger(idleBreakHash);
        PlayStateDirectly(idleBreakState);

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] Trigger IdleBreak", this);
    }

    public void SetDead(bool isDead)
    {
        EnsureInitialized();
        if (isDead)
        {
            ResetAllTriggers();

            if (hasSpeed) animator.SetFloat(speedHash, 0f);
            if (hasNeedsTurn) animator.SetBool(needsTurnHash, false);
            if (hasContinueShooting) animator.SetBool(continueShootingHash, false);
            if (hasContinueSpinning) animator.SetBool(continueSpinningHash, false);

            PlayStateDirectly(deathState);
        }

        if (!CanUseParameter(isDeadParameter, AnimatorControllerParameterType.Bool, hasIsDead, "set IsDead")) return;

        animator.SetBool(isDeadHash, isDead);

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] IsDead = " + isDead, this);
    }

    public void ResetToIdle()
    {
        EnsureInitialized();
        if (!HasUsableAnimator("reset to idle")) return;

        ResetAllTriggers();

        if (hasSpeed) animator.SetFloat(speedHash, 0f);
        if (hasIsDead) animator.SetBool(isDeadHash, false);
        if (hasNeedsTurn) animator.SetBool(needsTurnHash, false);
        if (hasContinueShooting) animator.SetBool(continueShootingHash, false);
        if (hasContinueSpinning) animator.SetBool(continueSpinningHash, false);

        lastLoggedSpeed = 0f;
        PlayStateDirectly(idleStateName);

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] Reset to Idle", this);
    }

    public void ResetAllTriggers()
    {
        EnsureInitialized();
        if (!HasUsableAnimator("reset triggers")) return;

        if (hasProjectileAttack) animator.ResetTrigger(projectileAttackHash);
        if (hasSpinAttack) animator.ResetTrigger(spinAttackHash);
        if (hasIdleBreak) animator.ResetTrigger(idleBreakHash);
    }

    private void ResetAttackTriggers()
    {
        if (hasProjectileAttack) animator.ResetTrigger(projectileAttackHash);
        if (hasSpinAttack) animator.ResetTrigger(spinAttackHash);
    }

    private void EnsureInitialized()
    {
        if (initialized) return;

        if (animator == null)
            animator = GetComponent<Animator>();

        CacheHashes();
        CheckAvailableParameters();
        initialized = true;
    }

    private void CacheHashes()
    {
        speedHash = Animator.StringToHash(speedParameter);
        projectileAttackHash = Animator.StringToHash(projectileAttackTrigger);
        spinAttackHash = Animator.StringToHash(spinAttackTrigger);
        idleBreakHash = Animator.StringToHash(idleBreakTrigger);
        isDeadHash = Animator.StringToHash(isDeadParameter);
        needsTurnHash = Animator.StringToHash(needsTurnParameter);
        continueShootingHash = Animator.StringToHash(continueShootingParameter);
        continueSpinningHash = Animator.StringToHash(continueSpinningParameter);
    }

    private void CheckAvailableParameters()
    {
        hasSpeed = false;
        hasProjectileAttack = false;
        hasSpinAttack = false;
        hasIdleBreak = false;
        hasIsDead = false;
        hasNeedsTurn = false;
        hasContinueShooting = false;
        hasContinueSpinning = false;

        if (!HasUsableAnimator("validate setup")) return;

        hasSpeed = HasParameter(speedParameter, AnimatorControllerParameterType.Float);
        hasProjectileAttack = HasParameter(projectileAttackTrigger, AnimatorControllerParameterType.Trigger);
        hasSpinAttack = HasParameter(spinAttackTrigger, AnimatorControllerParameterType.Trigger);
        hasIdleBreak = HasParameter(idleBreakTrigger, AnimatorControllerParameterType.Trigger);
        hasIsDead = HasParameter(isDeadParameter, AnimatorControllerParameterType.Bool);
        hasNeedsTurn = HasParameter(needsTurnParameter, AnimatorControllerParameterType.Bool);
        hasContinueShooting = HasParameter(continueShootingParameter, AnimatorControllerParameterType.Bool);
        hasContinueSpinning = HasParameter(continueSpinningParameter, AnimatorControllerParameterType.Bool);

        WarnIfMissing(speedParameter, hasSpeed, AnimatorControllerParameterType.Float);
        WarnIfMissing(projectileAttackTrigger, hasProjectileAttack, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(spinAttackTrigger, hasSpinAttack, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(idleBreakTrigger, hasIdleBreak, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(isDeadParameter, hasIsDead, AnimatorControllerParameterType.Bool);
        WarnIfMissing(needsTurnParameter, hasNeedsTurn, AnimatorControllerParameterType.Bool);
        WarnIfMissing(continueShootingParameter, hasContinueShooting, AnimatorControllerParameterType.Bool);
        WarnIfMissing(continueSpinningParameter, hasContinueSpinning, AnimatorControllerParameterType.Bool);
    }

    private bool HasUsableAnimator(string action)
    {
        if (animator == null)
        {
            if (logSetupWarnings)
                Debug.LogError("[ShellSpinnerAnimatorBridge:" + name + "] Cannot " + action + ": no Animator assigned.", this);
            return false;
        }

        if (animator.runtimeAnimatorController == null)
        {
            if (logSetupWarnings)
                Debug.LogError("[ShellSpinnerAnimatorBridge:" + name + "] Cannot " + action + ": Animator has no Runtime Animator Controller.", animator);
            return false;
        }

        if (!animator.enabled)
        {
            if (logSetupWarnings)
                Debug.LogError("[ShellSpinnerAnimatorBridge:" + name + "] Cannot " + action + ": Animator component is disabled.", animator);
            return false;
        }

        if (!animator.gameObject.activeInHierarchy)
        {
            if (logSetupWarnings)
                Debug.LogError("[ShellSpinnerAnimatorBridge:" + name + "] Cannot " + action + ": Animator GameObject is inactive.", animator);
            return false;
        }

        return true;
    }

    private bool CanUseParameter(string parameterName, AnimatorControllerParameterType expectedType, bool parameterExists, string action)
    {
        if (!HasUsableAnimator(action)) return false;

        if (!parameterExists)
        {
            CheckAvailableParameters();
            parameterExists = HasParameter(parameterName, expectedType);
        }

        if (!parameterExists)
        {
            if (logSetupWarnings)
                Debug.LogError("[ShellSpinnerAnimatorBridge:" + name + "] Cannot " + action + ": Animator parameter '" + parameterName + "' is missing or has the wrong type.", animator);
            return false;
        }

        return true;
    }

    private bool HasParameter(string parameterName, AnimatorControllerParameterType expectedType)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName && parameter.type == expectedType)
                return true;
        }

        return false;
    }

    private void WarnIfMissing(string parameterName, bool exists, AnimatorControllerParameterType expectedType)
    {
        if (!logSetupWarnings || exists) return;

        Debug.LogError("[ShellSpinnerAnimatorBridge:" + name + "] Animator parameter '" + parameterName + "' is missing or is not a " + expectedType + ".", animator);
    }

    private void PlayStateDirectly(string stateName)
    {
        if (!playStatesDirectly || string.IsNullOrWhiteSpace(stateName)) return;
        if (!HasUsableAnimator("play state '" + stateName + "' directly")) return;

        if (!TryFindState(stateName, out int layer, out int stateHash))
        {
            if (logSetupWarnings)
                Debug.LogError("[ShellSpinnerAnimatorBridge:" + name + "] Cannot play state '" + stateName + "' directly because it was not found on the Animator Controller.", animator);
            return;
        }

        animator.CrossFadeInFixedTime(stateHash, directTransitionDuration, layer, 0f);

        if (logAnimatorCalls)
            Debug.Log("[ShellSpinnerAnimatorBridge:" + name + "] CrossFade state " + stateName + " on layer " + layer + ".", animator);
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
