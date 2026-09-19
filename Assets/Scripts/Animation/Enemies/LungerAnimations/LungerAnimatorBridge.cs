using UnityEngine;

[RequireComponent(typeof(Animator))]
public class LungerAnimatorBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Animator Parameter Names")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string mainAttackTrigger = "MainAttack";
    [SerializeField] private string lungeAttackTrigger = "LungeAttack";
    [SerializeField] private string idleBreakTrigger = "IdleBreak";
    [SerializeField] private string isDeadParameter = "IsDead";

    [Header("Debug")]
    [SerializeField] private bool logAnimatorCalls = true;
    [SerializeField] private bool logSetupWarnings = true;

    [Header("Direct State Playback")]
    [Tooltip("Also cross-fades directly to states after setting parameters. Useful when triggers are called but transitions do not visibly play.")]
    [SerializeField] private bool playStatesDirectly = true;
    [SerializeField, Min(0f)] private float directTransitionDuration = 0.05f;
    [SerializeField] private string mainAttackState = "E1_MainAttack";
    [SerializeField] private string lungeAttackState = "E1_LungeAttack";
    [SerializeField] private string idleBreakState = "E1_IdleBreak";
    [SerializeField] private string deathState = "E1_Death";

    private int speedHash;
    private int mainAttackHash;
    private int lungeAttackHash;
    private int idleBreakHash;
    private int isDeadHash;

    private bool hasSpeedParameter;
    private bool hasMainAttackTrigger;
    private bool hasLungeAttackTrigger;
    private bool hasIdleBreakTrigger;
    private bool hasIsDeadParameter;
    private float lastLoggedSpeed = float.NaN;
    private bool initialized;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnValidate()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }

    private void CacheAnimatorHashes()
    {
        speedHash = Animator.StringToHash(speedParameter);
        mainAttackHash = Animator.StringToHash(mainAttackTrigger);
        lungeAttackHash = Animator.StringToHash(lungeAttackTrigger);
        idleBreakHash = Animator.StringToHash(idleBreakTrigger);
        isDeadHash = Animator.StringToHash(isDeadParameter);
    }

    public void SetSpeed(float speed)
    {
        EnsureInitialized();
        if (!CanUseParameter(speedParameter, AnimatorControllerParameterType.Float, hasSpeedParameter, "set Speed")) return;

        animator.SetFloat(speedHash, speed);

        if (logAnimatorCalls && (float.IsNaN(lastLoggedSpeed) || !Mathf.Approximately(lastLoggedSpeed, speed)))
        {
            Debug.Log("[LungerAnimatorBridge:" + name + "] Speed = " + speed.ToString("0.###"), this);
            lastLoggedSpeed = speed;
        }
    }

    public void PlayMainAttack()
    {
        EnsureInitialized();
        if (!CanUseParameter(mainAttackTrigger, AnimatorControllerParameterType.Trigger, hasMainAttackTrigger, "play MainAttack")) return;

        animator.SetTrigger(mainAttackHash);
        PlayStateDirectly(mainAttackState);

        if (logAnimatorCalls)
            Debug.Log("[LungerAnimatorBridge:" + name + "] Trigger MainAttack", this);
    }

    public void PlayLungeAttack()
    {
        EnsureInitialized();
        if (!CanUseParameter(lungeAttackTrigger, AnimatorControllerParameterType.Trigger, hasLungeAttackTrigger, "play LungeAttack")) return;

        animator.SetTrigger(lungeAttackHash);
        PlayStateDirectly(lungeAttackState);

        if (logAnimatorCalls)
            Debug.Log("[LungerAnimatorBridge:" + name + "] Trigger LungeAttack", this);
    }

    public void PlayIdleBreak()
    {
        EnsureInitialized();
        if (!CanUseParameter(idleBreakTrigger, AnimatorControllerParameterType.Trigger, hasIdleBreakTrigger, "play IdleBreak")) return;

        animator.SetTrigger(idleBreakHash);
        PlayStateDirectly(idleBreakState);

        if (logAnimatorCalls)
            Debug.Log("[LungerAnimatorBridge:" + name + "] Trigger IdleBreak", this);
    }

    public void SetDead(bool isDead)
    {
        EnsureInitialized();
        if (isDead)
            PlayStateDirectly(deathState);

        if (!CanUseParameter(isDeadParameter, AnimatorControllerParameterType.Bool, hasIsDeadParameter, "set IsDead")) return;

        animator.SetBool(isDeadHash, isDead);

        if (logAnimatorCalls)
            Debug.Log("[LungerAnimatorBridge:" + name + "] IsDead = " + isDead, this);
    }

    public void ResetToIdle()
    {
        EnsureInitialized();
        if (!HasUsableAnimator("reset to idle")) return;

        if (hasMainAttackTrigger) animator.ResetTrigger(mainAttackHash);
        if (hasLungeAttackTrigger) animator.ResetTrigger(lungeAttackHash);
        if (hasIdleBreakTrigger) animator.ResetTrigger(idleBreakHash);

        if (!hasSpeedParameter || !hasIsDeadParameter)
        {
            ValidateAnimatorSetup();
        }

        if (!hasSpeedParameter || !hasIsDeadParameter) return;

        animator.SetFloat(speedHash, 0f);
        animator.SetBool(isDeadHash, false);
        lastLoggedSpeed = 0f;

        if (logAnimatorCalls)
            Debug.Log("[LungerAnimatorBridge:" + name + "] Reset to Idle", this);
    }

    private void EnsureInitialized()
    {
        if (initialized) return;

        if (animator == null)
            animator = GetComponent<Animator>();

        CacheAnimatorHashes();
        ValidateAnimatorSetup();
        initialized = true;
    }

    private void ValidateAnimatorSetup()
    {
        hasSpeedParameter = false;
        hasMainAttackTrigger = false;
        hasLungeAttackTrigger = false;
        hasIdleBreakTrigger = false;
        hasIsDeadParameter = false;

        if (!HasUsableAnimator("validate setup")) return;

        hasSpeedParameter = HasParameter(speedParameter, AnimatorControllerParameterType.Float);
        hasMainAttackTrigger = HasParameter(mainAttackTrigger, AnimatorControllerParameterType.Trigger);
        hasLungeAttackTrigger = HasParameter(lungeAttackTrigger, AnimatorControllerParameterType.Trigger);
        hasIdleBreakTrigger = HasParameter(idleBreakTrigger, AnimatorControllerParameterType.Trigger);
        hasIsDeadParameter = HasParameter(isDeadParameter, AnimatorControllerParameterType.Bool);

        WarnIfMissing(speedParameter, hasSpeedParameter, AnimatorControllerParameterType.Float);
        WarnIfMissing(mainAttackTrigger, hasMainAttackTrigger, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(lungeAttackTrigger, hasLungeAttackTrigger, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(idleBreakTrigger, hasIdleBreakTrigger, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(isDeadParameter, hasIsDeadParameter, AnimatorControllerParameterType.Bool);
    }

    private bool HasUsableAnimator(string action)
    {
        if (animator == null)
        {
            if (logSetupWarnings)
                Debug.LogError("[LungerAnimatorBridge:" + name + "] Cannot " + action + ": no Animator assigned.", this);
            return false;
        }

        if (animator.runtimeAnimatorController == null)
        {
            if (logSetupWarnings)
                Debug.LogError("[LungerAnimatorBridge:" + name + "] Cannot " + action + ": Animator has no Runtime Animator Controller.", animator);
            return false;
        }

        if (!animator.enabled)
        {
            if (logSetupWarnings)
                Debug.LogError("[LungerAnimatorBridge:" + name + "] Cannot " + action + ": Animator component is disabled.", animator);
            return false;
        }

        if (!animator.gameObject.activeInHierarchy)
        {
            if (logSetupWarnings)
                Debug.LogError("[LungerAnimatorBridge:" + name + "] Cannot " + action + ": Animator GameObject is inactive.", animator);
            return false;
        }

        return true;
    }

    private bool CanUseParameter(string parameterName, AnimatorControllerParameterType expectedType, bool parameterExists, string action)
    {
        if (!HasUsableAnimator(action)) return false;

        if (!parameterExists)
        {
            ValidateAnimatorSetup();
            parameterExists = HasParameter(parameterName, expectedType);
        }

        if (!parameterExists)
        {
            if (logSetupWarnings)
                Debug.LogError("[LungerAnimatorBridge:" + name + "] Cannot " + action + ": Animator parameter '" + parameterName + "' is missing or has the wrong type.", animator);
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

        Debug.LogError("[LungerAnimatorBridge:" + name + "] Animator parameter '" + parameterName + "' is missing or is not a " + expectedType + ".", animator);
    }

    private void PlayStateDirectly(string stateName)
    {
        if (!playStatesDirectly || string.IsNullOrWhiteSpace(stateName)) return;
        if (!HasUsableAnimator("play state '" + stateName + "' directly")) return;

        if (!TryFindState(stateName, out int layer, out int stateHash))
        {
            if (logSetupWarnings)
                Debug.LogError("[LungerAnimatorBridge:" + name + "] Cannot play state '" + stateName + "' directly because it was not found on the Animator Controller.", animator);
            return;
        }

        animator.CrossFadeInFixedTime(stateHash, directTransitionDuration, layer, 0f);

        if (logAnimatorCalls)
            Debug.Log("[LungerAnimatorBridge:" + name + "] CrossFade state " + stateName + " on layer " + layer + ".", animator);
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
