using UnityEngine;

public class ElderKoiAnimatorBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Animator State Names")]
    [SerializeField] private string idleStateName = "ElderKoi_Idle";
    [SerializeField] private string idleBreakStateName = "ElderKoi_IdleBreak";
    [SerializeField] private string dialogV1StateName = "ElderKoi_V1Dialog";
    [SerializeField] private string dialogV2StateName = "ElderKoi_V2Dialog";
    [SerializeField] private string dialogV3StateName = "ElderKoi_V3Dialog";

    [Header("Animator Parameter Names")]
    [SerializeField] private string dialogTriggerName = "Dialog";
    [SerializeField] private string idleBreakTriggerName = "IdleBreak";
    [SerializeField] private string dialogVariantParameterName = "DialogVariant";

    [Header("Settings")]
    [SerializeField] private int layerIndex = 0;
    [SerializeField] private float transitionDuration = 0.1f;
    [SerializeField] private bool useAnimatorParameters = true;
    [SerializeField] private bool playDialogStatesDirectly;
    [SerializeField] private bool avoidSameDialogTwice = true;

    private Vector3 startLocalPosition;
    private Quaternion startLocalRotation;
    private Vector3 startLocalScale;

    private int lastDialogVariant = 0;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        CacheStartTransform();
    }

    private void CacheStartTransform()
    {
        startLocalPosition = transform.localPosition;
        startLocalRotation = transform.localRotation;
        startLocalScale = transform.localScale;
    }

    public void PlayIdle()
    {
        if (animator == null) return;

        ResetAllTriggers();

        if (HasParameter(dialogVariantParameterName, AnimatorControllerParameterType.Int))
            animator.SetInteger(dialogVariantParameterName, 0);

        PlayStateDirectly(idleStateName);
    }

    public void PlayRandomDialog(bool restartIfAlreadyPlaying = false)
    {
        if (IsDialogAnimationPlaying() && !restartIfAlreadyPlaying)
            return;

        int randomVariant = Random.Range(1, 4);

        if (avoidSameDialogTwice && lastDialogVariant != 0)
        {
            int safetyCounter = 0;

            while (randomVariant == lastDialogVariant && safetyCounter < 10)
            {
                randomVariant = Random.Range(1, 4);
                safetyCounter++;
            }
        }

        PlayDialogVariant(randomVariant, restartIfAlreadyPlaying);
    }

    public void PlayDialogVariant(int variant, bool restartIfAlreadyPlaying = false)
    {
        if (animator == null) return;
        if (IsDialogAnimationPlaying() && !restartIfAlreadyPlaying) return;

        variant = Mathf.Clamp(variant, 1, 3);
        lastDialogVariant = variant;

        ResetAllTriggers();

        string targetStateName = GetDialogStateName(variant);

        if (!playDialogStatesDirectly &&
            useAnimatorParameters &&
            HasParameter(dialogTriggerName, AnimatorControllerParameterType.Trigger) &&
            HasParameter(dialogVariantParameterName, AnimatorControllerParameterType.Int))
        {
            animator.SetInteger(dialogVariantParameterName, variant);
            animator.SetTrigger(dialogTriggerName);
        }
        else
        {
            PlayStateDirectly(targetStateName, restartIfAlreadyPlaying);
        }
    }

    public void PlayV1Dialog()
    {
        PlayDialogVariant(1);
    }

    public void PlayV2Dialog()
    {
        PlayDialogVariant(2);
    }

    public void PlayV3Dialog()
    {
        PlayDialogVariant(3);
    }

    public void PlayIdleBreak()
    {
        if (animator == null) return;

        ResetAllTriggers();

        if (useAnimatorParameters && HasParameter(idleBreakTriggerName, AnimatorControllerParameterType.Trigger))
        {
            animator.SetTrigger(idleBreakTriggerName);
        }
        else
        {
            PlayStateDirectly(idleBreakStateName);
        }
    }

    public void ResetToIdle()
    {
        if (animator == null) return;

        ResetAllTriggers();

        if (HasParameter(dialogVariantParameterName, AnimatorControllerParameterType.Int))
            animator.SetInteger(dialogVariantParameterName, 0);

        animator.Rebind();
        animator.Update(0f);

        animator.Play(idleStateName, layerIndex, 0f);
        animator.Update(0f);
    }

    public void ResetToStartAndIdle()
    {
        transform.localPosition = startLocalPosition;
        transform.localRotation = startLocalRotation;
        transform.localScale = startLocalScale;

        ResetToIdle();
    }

    private string GetDialogStateName(int variant)
    {
        switch (variant)
        {
            case 1:
                return dialogV1StateName;
            case 2:
                return dialogV2StateName;
            case 3:
                return dialogV3StateName;
            default:
                return dialogV1StateName;
        }
    }

    private bool IsDialogAnimationPlaying()
    {
        if (animator == null) return false;

        if (IsDialogState(animator.GetCurrentAnimatorStateInfo(layerIndex)))
            return true;

        return animator.IsInTransition(layerIndex) &&
               IsDialogState(animator.GetNextAnimatorStateInfo(layerIndex));
    }

    private bool IsDialogState(AnimatorStateInfo stateInfo)
    {
        return stateInfo.IsName(dialogV1StateName) ||
               stateInfo.IsName(dialogV2StateName) ||
               stateInfo.IsName(dialogV3StateName);
    }

    private void PlayStateDirectly(string stateName, bool restartIfAlreadyPlaying = false)
    {
        if (animator == null) return;
        if (string.IsNullOrWhiteSpace(stateName)) return;

        if (restartIfAlreadyPlaying)
        {
            animator.Play(stateName, layerIndex, 0f);
            return;
        }

        animator.CrossFadeInFixedTime(stateName, transitionDuration, layerIndex, 0f);
    }

    private void ResetAllTriggers()
    {
        if (animator == null) return;

        if (HasParameter(dialogTriggerName, AnimatorControllerParameterType.Trigger))
            animator.ResetTrigger(dialogTriggerName);

        if (HasParameter(idleBreakTriggerName, AnimatorControllerParameterType.Trigger))
            animator.ResetTrigger(idleBreakTriggerName);
    }

    private bool HasParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (animator == null) return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName && parameter.type == parameterType)
                return true;
        }

        return false;
    }

    public void AnimationEvent_ReturnToIdle()
    {
        PlayIdle();
    }
}
