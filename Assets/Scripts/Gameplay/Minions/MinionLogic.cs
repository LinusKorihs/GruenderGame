using UnityEngine;

// Final resolved action the minion wants to perform.
public class MinionIntent
{
    public CommandType CommandType;
    public object Target;
    public Vector3 TargetPosition;

    public bool HasTarget => Target != null;

    public static MinionIntent Idle()
    {
        return new MinionIntent
        {
            CommandType = CommandType.None,
            Target = null,
            TargetPosition = Vector3.zero
        };
    }

    public static MinionIntent FromCommand(MinionCommand command)
    {
        if (command == null) return Idle();

        return new MinionIntent
        {
            CommandType = command.Type,
            Target = command.Target,
            TargetPosition = command.TargetPosition
        };
    }
}

// Resolves the final intent from current command and fallback rules.
public class MinionDecisionLayer
{
    public MinionIntent ResolveIntent(MinionCommand command, float currentTime)
    {
        if (command == null) return BuildFallbackIntent();

        if (command.IsExpired(currentTime))
        {
            command.LastFailureReason = FailureReason.CommandExpired;
            return BuildFallbackIntent();
        }

        switch (command.Type)
        {
            case CommandType.Recall:
            case CommandType.FollowPlayer:
            case CommandType.MoveToPosition:
            case CommandType.Dismiss:
            case CommandType.AttackEnemy:
            case CommandType.AttackObject:
            case CommandType.SupportTarget:
                return MinionIntent.FromCommand(command);

            case CommandType.None:
            default:
                return BuildFallbackIntent();
        }
    }

    // Fallback
    private MinionIntent BuildFallbackIntent()
    {
        return MinionIntent.Idle();
    }
}

// Converts the resolved intent into a global minion state.
public class MinionStateMachine
{
    public MinionState CurrentState { get; private set; } = MinionState.Idle;

    public void UpdateState(MinionIntent intent)
    {
        if (intent == null)
        {
            CurrentState = MinionState.Idle;
            return;
        }

        switch (intent.CommandType)
        {
            case CommandType.None:
                CurrentState = MinionState.Idle;
                break;

            case CommandType.FollowPlayer:
            case CommandType.Recall:
            case CommandType.MoveToPosition:
            case CommandType.Dismiss:
                CurrentState = MinionState.Follow;
                break;

            case CommandType.AttackEnemy:
            case CommandType.AttackObject:
            case CommandType.SupportTarget:
                CurrentState = MinionState.Combat;
                break;

            default:
                CurrentState = MinionState.Idle;
                break;
        }
    }

    public void ForceState(MinionState forcedState)
    {
        CurrentState = forcedState;
    }
}

// Resolves the current combat phase by asking the active role.
public class MinionCombatPhaseController
{
    public CombatPhase CurrentPhase { get; private set; } = CombatPhase.None;

    public void UpdatePhase(
        MinionState currentState,
        IMinionRole role,
        float distanceToTarget,
        bool hasLineOfSight,
        bool isAbilityReady)
    {
        if (currentState != MinionState.Combat || role == null)
        {
            CurrentPhase = CombatPhase.None;
            return;
        }

        CurrentPhase = role.EvaluateCombatPhase(distanceToTarget, hasLineOfSight, isAbilityReady);
    }

    public void Reset()
    {
        CurrentPhase = CombatPhase.None;
    }

    public void ForcePhase(CombatPhase forcedPhase)
    {
        CurrentPhase = forcedPhase;
    }
}

// Controls how often expensive logic is allowed to run.
public class MinionTickScheduler
{
    private float nextDecisionTime;
    private float nextTargetingTime;
    private float nextSensorTime;

    public bool ShouldRunDecision(float currentTime, MinionTickRates tickRates)
    {
        if (currentTime < nextDecisionTime) return false;

        nextDecisionTime = currentTime + tickRates.DecisionTickRate;
        return true;
    }

    public bool ShouldRunTargeting(float currentTime, MinionTickRates tickRates)
    {
        if (currentTime < nextTargetingTime) return false;

        nextTargetingTime = currentTime + tickRates.TargetingTickRate;
        return true;
    }

    public bool ShouldRunSensor(float currentTime, MinionTickRates tickRates)
    {
        if (currentTime < nextSensorTime) return false;

        nextSensorTime = currentTime + tickRates.SensorTickRate;
        return true;
    }

    public void Reset()
    {
        nextDecisionTime = 0f;
        nextTargetingTime = 0f;
        nextSensorTime = 0f;
    }
}
