using System;
using UnityEngine;

public class KelpAnimationEvents : MonoBehaviour
{
    public static event Action<GameObject> PlayerDeathAnimationFinished;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    public void OnFootstep()
    {
        LogEvent("Footstep");
    }

    public void OnIdleBreakStart()
    {
        LogEvent("IdleBreak started");
    }

    public void OnIdleBreakEnd()
    {
        LogEvent("IdleBreak ended");
    }

    public void OnPunchHit()
    {
        LogEvent("Punch hit frame reached");
        PlayerPunch punch = GetComponentInParent<PlayerPunch>();
        if (punch != null)
            punch.OnAnimationPunchHit();
    }

    public void OnDodgeStart()
    {
        LogEvent("Dodge started");
    }

    public void OnDodgeEnd()
    {
        LogEvent("Dodge ended");
    }

    public void OnCallMinionsMoment()
    {
        LogEvent("Call Minions moment reached");
    }

    public void OnOrderMinionsMoment()
    {
        LogEvent("Order Minions moment reached");
    }

    public void OnDismissMinionsMoment()
    {
        LogEvent("Dismiss Minions moment reached");
    }

    public void OnInteractMoment()
    {
        LogEvent("Interact moment reached");
    }

    public void OnDeathFinished()
    {
        LogEvent("Death animation finished");
        PlayerDeathAnimationFinished?.Invoke(PlayerRootResolver.FromGameObject(gameObject));
    }

    // Alias-Methoden, falls Animation Events kürzer benannt wurden

    public void OnCallMinions()
    {
        LogEvent("Call Minions event reached");
    }

    public void OnOrderMinions()
    {
        LogEvent("Order Minions event reached");
    }

    public void OnDismiss()
    {
        LogEvent("Dismiss event reached");
    }

    public void OnInteract()
    {
        LogEvent("Interact event reached");
    }

    public void OnDeathHit()
    {
        LogEvent("Death hit frame reached");
    }

    private void LogEvent(string message)
    {
        if (!showDebugLogs)
        {
            return;
        }

        Debug.Log("[Kelp Animation Event] " + gameObject.name + ": " + message);
    }
}
