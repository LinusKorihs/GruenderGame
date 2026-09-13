using UnityEngine;

// Centralized definitions for minion commands.
public enum CommandType
{
    None,
    FollowPlayer,
    Recall,
    MoveToPosition,
    AttackEnemy,
    AttackObject,
    SupportTarget,
    Dismiss  // Player dismissed the minion to a formation position; blocks auto-combat.
}

// Global state of the minion (very small on purpose)
public enum MinionState
{
    Idle,
    Follow,
    Combat
}

// Sub-state inside combat (prevents jitter & logic conflicts)
public enum CombatPhase
{
    None,
    Approach,
    Reposition,
    AttackWindow,
    Cast,
    Recover
}

// Defines the base role of a minion
public enum MinionRoleType
{
    Melee,
    Ranged,
    Support
}

// Active support behavior (only one active at a time)
public enum SupportMode
{
    Heal,
    Buff,
    Debuff
}

// Who issued the command (important for priority/debugging)
public enum CommandSource
{
    Player,
    System,
    AI
}

// Defines how a command can interrupt current actions
public enum InterruptPolicy
{
    None,           // Cannot interrupt anything
    Soft,           // Can interrupt non-critical actions
    Hard            // Can interrupt everything
}

// Used to track why something failed (useful for debugging)
public enum FailureReason
{
    None,
    TargetDead,
    TargetLost,
    TargetUnreachable,
    NoLineOfSight,
    CommandExpired,
    PathInvalid,
    AbilityBlocked
}

// General target categories (can be replaced by interfaces later)
public enum TargetType
{
    Enemy,
    Ally,
    Breakable
}

public class MinionCommand
{
    public CommandType Type;
    public object Target; // replace with proper interface later
    public UnityEngine.Vector3 TargetPosition;

    public int Priority;
    public float IssuedTime;
    public float TimeToLive;

    public CommandSource Source;
    public InterruptPolicy InterruptPolicy;

    public FailureReason LastFailureReason;

    // Checks if command is still valid
    public bool IsExpired(float currentTime)
    {
        return TimeToLive > 0f && currentTime > IssuedTime + TimeToLive;
    }
}

// Defines desired combat distances and tolerances
[System.Serializable]
public class RangePolicy
{
    [Tooltip("Minimum effective range. Too close is bad, minions will try to reposition if closer than this.")]
    public float MinRange;
    [Tooltip("Ideal distance for the minion.")]
    public float DesiredRange;
    [Tooltip("Maximum effective range. Too far is bad, minions will try to reposition if farther than this.")]
    public float MaxRange;

    [Tooltip("Tolerance for repositioning to prevent jitter.")]
    public float RepositionTolerance;

    // Checks if within acceptable range
    public bool IsInRange(float distance)
    {
        return distance >= MinRange && distance <= MaxRange;
    }

    // Checks if Minion should reposition (too close or too far)
    public bool ShouldReposition(float distance)
    {
        return distance < MinRange - RepositionTolerance || distance > MaxRange + RepositionTolerance;
    }
}

// Simple tick configuration (performance scaling, optional)
[System.Serializable]
public struct MinionTickRates
{
    [Tooltip("How often the minion makes decisions.")]
    public float DecisionTickRate;
    [Tooltip("How often the minion updates its target.")]
    public float TargetingTickRate;
    [Tooltip("How often the minion updates its sensors.")]
    public float SensorTickRate;
}
