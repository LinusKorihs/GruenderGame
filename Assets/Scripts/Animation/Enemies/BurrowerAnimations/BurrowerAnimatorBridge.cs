using UnityEngine;

[RequireComponent(typeof(Animator))]
public class BurrowerAnimatorBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Animator Parameter Names")]
    [SerializeField] private string wakeUpTrigger = "WakeUp";
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string secondAttackTrigger = "SecondAttack";
    [SerializeField] private string grabAttackTrigger = "GrabAttack";
    [SerializeField] private string flyDownTrigger = "FlyDown";
    [SerializeField] private string digDownTrigger = "DigDown";
    [SerializeField] private string deathFlyingTrigger = "DeathFlying";
    [SerializeField] private string deathDiggingTrigger = "DeathDigging";

    [Header("Debug")]
    [SerializeField] private bool logAnimatorCalls = true;
    [SerializeField] private bool logSetupWarnings = true;

    [Header("Direct State Playback")]
    [Tooltip("Also cross-fades directly to states after setting parameters. Useful when triggers are called but transitions do not visibly play.")]
    [SerializeField] private bool playStatesDirectly = true;
    [SerializeField, Min(0f)] private float directTransitionDuration = 0.05f;
    [SerializeField] private string hiddenStateName = "E3_HiddenArmed";
    [SerializeField] private string walkBaseStateName = "E3_Walk";
    [SerializeField] private string hoverStateName = "E3_Hover";
    [SerializeField] private string wakeUpState = "E3_FirstAttack";
    [SerializeField] private string secondAttackState = "E3_SecondAttack";
    [SerializeField] private string grabAttackState = "E3_SecondAttack";
    [SerializeField] private string flyDownState = "E3_FlyDown";
    [SerializeField] private string digDownState = "E3_DiggingAndDown";
    [SerializeField] private string deathFlyingState = "E3_DeathWhileFlying";
    [SerializeField] private string deathDiggingState = "E3_DeathWhileDigging";

    [Header("Reset Settings")]
    [SerializeField] private bool updateAnimatorAfterReset = true;

    private int wakeUpHash;
    private int speedHash;
    private int secondAttackHash;
    private int grabAttackHash;
    private int flyDownHash;
    private int digDownHash;
    private int deathFlyingHash;
    private int deathDiggingHash;

    private bool hasWakeUp;
    private bool hasSpeed;
    private bool hasSecondAttack;
    private bool hasGrabAttack;
    private bool hasFlyDown;
    private bool hasDigDown;
    private bool hasDeathFlying;
    private bool hasDeathDigging;

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

    public void WakeUp()
    {
        FireTrigger(wakeUpHash, hasWakeUp, wakeUpTrigger, wakeUpState);
    }

    public void SetSpeed(float speed)
    {
        EnsureInitialized();
        if (!CanUseParameter(speedParameter, AnimatorControllerParameterType.Float, hasSpeed, "set Speed")) return;

        animator.SetFloat(speedHash, speed);

        if (logAnimatorCalls && (float.IsNaN(lastLoggedSpeed) || !Mathf.Approximately(lastLoggedSpeed, speed)))
        {
            Debug.Log("[BurrowerAnimatorBridge:" + name + "] Speed = " + speed.ToString("0.###"), this);
            lastLoggedSpeed = speed;
        }
    }

    public void PlaySecondAttack()
    {
        FireTrigger(secondAttackHash, hasSecondAttack, secondAttackTrigger, secondAttackState);
    }

    public void PlayGrabAttack()
    {
        FireTrigger(grabAttackHash, hasGrabAttack, grabAttackTrigger, grabAttackState);
    }

    public void PlayFlyDown()
    {
        FireTrigger(flyDownHash, hasFlyDown, flyDownTrigger, flyDownState);
    }

    public void PlayDigDown()
    {
        FireTrigger(digDownHash, hasDigDown, digDownTrigger, digDownState);
    }

    public void PlayDeathFlying()
    {
        ResetAllTriggers();
        PlayStateDirectly(deathFlyingState);
    }

    public void PlayDeathDigging()
    {
        ResetAllTriggers();
        PlayStateDirectly(deathDiggingState);
    }

    public void ResetToHidden()
    {
        EnsureInitialized();
        ResetAllTriggers();
        if (hasSpeed) animator.SetFloat(speedHash, 0f);
        lastLoggedSpeed = 0f;
        PlayState(hiddenStateName);

        if (logAnimatorCalls)
            Debug.Log("[BurrowerAnimatorBridge:" + name + "] Reset to Hidden", this);
    }

    public void ResetToWalkBase()
    {
        EnsureInitialized();
        ResetAllTriggers();
        if (hasSpeed) animator.SetFloat(speedHash, 0f);
        lastLoggedSpeed = 0f;
        PlayState(walkBaseStateName);

        if (logAnimatorCalls)
            Debug.Log("[BurrowerAnimatorBridge:" + name + "] Reset to WalkBase", this);
    }

    public void ResetToHover()
    {
        EnsureInitialized();
        ResetAllTriggers();
        if (hasSpeed) animator.SetFloat(speedHash, 0f);
        lastLoggedSpeed = 0f;
        PlayState(hoverStateName);

        if (logAnimatorCalls)
            Debug.Log("[BurrowerAnimatorBridge:" + name + "] Reset to Hover", this);
    }

    public void ResetAllTriggers()
    {
        EnsureInitialized();
        if (!HasUsableAnimator("reset triggers")) return;

        if (hasWakeUp) animator.ResetTrigger(wakeUpHash);
        if (hasSecondAttack) animator.ResetTrigger(secondAttackHash);
        if (hasGrabAttack) animator.ResetTrigger(grabAttackHash);
        if (hasFlyDown) animator.ResetTrigger(flyDownHash);
        if (hasDigDown) animator.ResetTrigger(digDownHash);
        if (hasDeathFlying) animator.ResetTrigger(deathFlyingHash);
        if (hasDeathDigging) animator.ResetTrigger(deathDiggingHash);
    }

    private void FireTrigger(int triggerHash, bool hasTrigger, string triggerName, string stateName)
    {
        EnsureInitialized();
        if (!CanUseParameter(triggerName, AnimatorControllerParameterType.Trigger, hasTrigger, "play " + triggerName)) return;

        animator.ResetTrigger(triggerHash);
        animator.SetTrigger(triggerHash);
        PlayStateDirectly(stateName);

        if (logAnimatorCalls)
            Debug.Log("[BurrowerAnimatorBridge:" + name + "] Trigger " + triggerName, this);
    }

    private void PlayState(string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName))
        {
            if (logSetupWarnings)
                Debug.LogWarning("[BurrowerAnimatorBridge:" + name + "] State name is empty.", this);
            return;
        }

        if (!PlayStateDirectly(stateName)) return;

        if (updateAnimatorAfterReset && animator != null)
            animator.Update(0f);
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
        wakeUpHash = Animator.StringToHash(wakeUpTrigger);
        speedHash = Animator.StringToHash(speedParameter);
        secondAttackHash = Animator.StringToHash(secondAttackTrigger);
        grabAttackHash = Animator.StringToHash(grabAttackTrigger);
        flyDownHash = Animator.StringToHash(flyDownTrigger);
        digDownHash = Animator.StringToHash(digDownTrigger);
        deathFlyingHash = Animator.StringToHash(deathFlyingTrigger);
        deathDiggingHash = Animator.StringToHash(deathDiggingTrigger);
    }

    private void CheckAvailableParameters()
    {
        hasWakeUp = false;
        hasSpeed = false;
        hasSecondAttack = false;
        hasGrabAttack = false;
        hasFlyDown = false;
        hasDigDown = false;
        hasDeathFlying = false;
        hasDeathDigging = false;

        if (!HasUsableAnimator("validate setup")) return;

        hasWakeUp = HasParameter(wakeUpTrigger, AnimatorControllerParameterType.Trigger);
        hasSpeed = HasParameter(speedParameter, AnimatorControllerParameterType.Float);
        hasSecondAttack = HasParameter(secondAttackTrigger, AnimatorControllerParameterType.Trigger);
        hasGrabAttack = HasParameter(grabAttackTrigger, AnimatorControllerParameterType.Trigger);
        hasFlyDown = HasParameter(flyDownTrigger, AnimatorControllerParameterType.Trigger);
        hasDigDown = HasParameter(digDownTrigger, AnimatorControllerParameterType.Trigger);
        hasDeathFlying = HasParameter(deathFlyingTrigger, AnimatorControllerParameterType.Trigger);
        hasDeathDigging = HasParameter(deathDiggingTrigger, AnimatorControllerParameterType.Trigger);

        WarnIfMissing(wakeUpTrigger, hasWakeUp, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(speedParameter, hasSpeed, AnimatorControllerParameterType.Float);
        WarnIfMissing(secondAttackTrigger, hasSecondAttack, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(grabAttackTrigger, hasGrabAttack, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(flyDownTrigger, hasFlyDown, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(digDownTrigger, hasDigDown, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(deathFlyingTrigger, hasDeathFlying, AnimatorControllerParameterType.Trigger);
        WarnIfMissing(deathDiggingTrigger, hasDeathDigging, AnimatorControllerParameterType.Trigger);
    }

    private bool HasUsableAnimator(string action)
    {
        if (animator == null)
        {
            if (logSetupWarnings)
                Debug.LogError("[BurrowerAnimatorBridge:" + name + "] Cannot " + action + ": no Animator assigned.", this);
            return false;
        }

        if (animator.runtimeAnimatorController == null)
        {
            if (logSetupWarnings)
                Debug.LogError("[BurrowerAnimatorBridge:" + name + "] Cannot " + action + ": Animator has no Runtime Animator Controller.", animator);
            return false;
        }

        if (!animator.enabled)
        {
            if (logSetupWarnings)
                Debug.LogError("[BurrowerAnimatorBridge:" + name + "] Cannot " + action + ": Animator component is disabled.", animator);
            return false;
        }

        if (!animator.gameObject.activeInHierarchy)
        {
            if (logSetupWarnings)
                Debug.LogError("[BurrowerAnimatorBridge:" + name + "] Cannot " + action + ": Animator GameObject is inactive.", animator);
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
                Debug.LogError("[BurrowerAnimatorBridge:" + name + "] Cannot " + action + ": Animator parameter '" + parameterName + "' is missing or has the wrong type.", animator);
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

        Debug.LogError("[BurrowerAnimatorBridge:" + name + "] Animator parameter '" + parameterName + "' is missing or is not a " + expectedType + ".", animator);
    }

    private bool PlayStateDirectly(string stateName)
    {
        if (!playStatesDirectly || string.IsNullOrWhiteSpace(stateName)) return false;
        if (!HasUsableAnimator("play state '" + stateName + "' directly")) return false;

        if (!TryFindState(stateName, out int layer, out int stateHash))
        {
            if (logSetupWarnings)
                Debug.LogError("[BurrowerAnimatorBridge:" + name + "] Cannot play state '" + stateName + "' directly because it was not found on the Animator Controller.", animator);
            return false;
        }

        animator.CrossFadeInFixedTime(stateHash, directTransitionDuration, layer, 0f);

        if (logAnimatorCalls)
            Debug.Log("[BurrowerAnimatorBridge:" + name + "] CrossFade state " + stateName + " on layer " + layer + ".", animator);

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
