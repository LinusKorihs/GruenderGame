using UnityEngine;

public class PinchAnimatorBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Optional Coin / Prop")]
    [SerializeField] private GameObject coinRoot;
    [SerializeField] private Renderer[] coinRenderers;

    [Header("Animator State Names")]
    [SerializeField] private string idleStateName = "Pinch_Idle";
    [SerializeField] private string dialogStateName = "Pinch_Dialog";
    [SerializeField] private string idleBreakStateName = "Pinch_IdleBreak";

    [Header("Animator Trigger Names")]
    [SerializeField] private string dialogTriggerName = "Dialog";
    [SerializeField] private string idleBreakTriggerName = "IdleBreak";

    [Header("Settings")]
    [SerializeField] private int layerIndex = 0;
    [SerializeField] private float transitionDuration = 0.1f;
    [SerializeField] private bool useAnimatorParameters = true;

    [Header("Coin Settings")]
    [SerializeField] private bool hideCoinOnStart = true;
    [SerializeField] private bool forceCoinHiddenOutsideIdleBreak = true;
    [SerializeField] private bool disableCoinGameObjectWhenHidden = true;

    private Vector3 startLocalPosition;
    private Quaternion startLocalRotation;
    private Vector3 startLocalScale;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        CacheStartTransform();
        CacheCoinRenderers();

        if (hideCoinOnStart)
            SetCoinVisible(false);
    }

    private void LateUpdate()
    {
        if (!forceCoinHiddenOutsideIdleBreak) return;
        if (animator == null) return;

        bool shouldShowCoin = IsCurrentOrNextState(idleBreakStateName);
        SetCoinVisible(shouldShowCoin);
    }

    private void CacheStartTransform()
    {
        startLocalPosition = transform.localPosition;
        startLocalRotation = transform.localRotation;
        startLocalScale = transform.localScale;
    }

    private void CacheCoinRenderers()
    {
        if (coinRoot != null)
            coinRenderers = coinRoot.GetComponentsInChildren<Renderer>(true);
    }

    public void PlayIdle()
    {
        if (animator == null) return;

        SetCoinVisible(false);
        ResetAllTriggers();
        PlayStateDirectly(idleStateName);
    }

    public void PlayDialog()
    {
        if (animator == null) return;
        if (IsCurrentOrNextState(dialogStateName)) return;

        SetCoinVisible(false);
        PlayByTriggerOrDirectState(dialogTriggerName, dialogStateName);
    }

    public void PlayIdleBreak()
    {
        if (animator == null) return;

        SetCoinVisible(true);
        PlayByTriggerOrDirectState(idleBreakTriggerName, idleBreakStateName);
    }

    public void ResetToIdle()
    {
        if (animator == null) return;

        SetCoinVisible(false);
        ResetAllTriggers();

        animator.Rebind();
        animator.Update(0f);

        animator.Play(idleStateName, layerIndex, 0f);
        animator.Update(0f);

        SetCoinVisible(false);
    }

    public void ResetToStartAndIdle()
    {
        transform.localPosition = startLocalPosition;
        transform.localRotation = startLocalRotation;
        transform.localScale = startLocalScale;

        ResetToIdle();
    }

    public void SetCoinVisible(bool visible)
    {
        if (coinRoot != null && disableCoinGameObjectWhenHidden)
            coinRoot.SetActive(visible);

        if (coinRenderers == null || coinRenderers.Length == 0)
            return;

        foreach (Renderer coinRenderer in coinRenderers)
        {
            if (coinRenderer != null)
                coinRenderer.enabled = visible;
        }
    }

    private void PlayByTriggerOrDirectState(string triggerName, string stateName)
    {
        if (animator == null) return;

        ResetAllTriggers();

        if (useAnimatorParameters && HasParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            animator.SetTrigger(triggerName);
        }
        else
        {
            PlayStateDirectly(stateName);
        }
    }

    private void PlayStateDirectly(string stateName)
    {
        if (animator == null) return;
        if (string.IsNullOrEmpty(stateName)) return;

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

    private bool IsCurrentOrNextState(string stateName)
    {
        if (string.IsNullOrEmpty(stateName)) return false;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(layerIndex);

        if (currentState.IsName(stateName))
            return true;

        if (animator.IsInTransition(layerIndex))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(layerIndex);

            if (nextState.IsName(stateName))
                return true;
        }

        return false;
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

    public void AnimationEvent_ShowCoin()
    {
        SetCoinVisible(true);
    }

    public void AnimationEvent_HideCoin()
    {
        SetCoinVisible(false);
    }
}
