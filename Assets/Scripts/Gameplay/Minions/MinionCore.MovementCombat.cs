using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public partial class MinionCore
{
    private void ExecuteFollow()
    {
        // Dismiss: move toward the commanded formation position, idle when arrived.
        if (currentCommand != null && currentCommand.Type == CommandType.Dismiss)
        {
            Vector3 toFormation = currentCommand.TargetPosition - transform.position;
            toFormation.y = 0f;
            float dist = toFormation.magnitude;

            if (dist <= Mathf.Max(0f, followStopDistance))
            {
                // Arrived at formation position — stay put in idle. If player moves away, command will be re-issued to move back to formation.
                ResetNavigationPath();
                ClearCommand();
                stateMachine.ForceState(MinionState.Idle);
                return;
            }

            MoveTowardsDistance(currentCommand.TargetPosition, followStopDistance);
            return;
        }

        Transform target = ResolveFollowTarget();
        if (!IsValidTarget(target))
        {
            ClearCommand();
            stateMachine.ForceState(MinionState.Idle);
            return;
        }

        // Calculate horizontal distance (ignore Y to match MoveTowardsDistance)
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance <= Mathf.Max(0f, followStopDistance))
        {
            if (distance > 0.0001f)
            {
                SmoothFaceDirection(toTarget / Mathf.Max(distance, 0.0001f));
            }

            // Recall complete: minion has physically reached the player — switch to steady Follow.
            if (currentCommand != null && currentCommand.Type == CommandType.Recall)
            {
                Log("Recall complete — switching to Follow.");
                SetFollowCommand();
            }

            return;
        }

        MoveTowardsDistance(target.position, followStopDistance);
    }

    private Transform ResolveFollowTarget()
    {
        if (IsValidTarget(followTarget)) return followTarget;

        Transform commandTarget = AsTransform(currentCommand != null ? currentCommand.Target : null);
        if (IsValidTarget(commandTarget)) return commandTarget;

        return null;
    }

    private void ExecuteCombat(float currentTime)
    {
        if (!IsValidTarget(currentTarget))
        {
            HandleMissingCombatTarget();
            return;
        }

        RangePolicy rangePolicy = currentRole.GetRangePolicy();
        float desiredRange = rangePolicy != null ? rangePolicy.DesiredRange : 0f;

        switch (combatPhaseController.CurrentPhase)
        {
            case CombatPhase.None:
                break;

            case CombatPhase.Approach:
                MoveTowardsDistance(currentTarget.position, desiredRange);
                break;

            case CombatPhase.Reposition:
                if (!hasLineOfSight && returnToFollowWhenLineOfSightBlocked)
                {
                    HandleBlockedLineOfSight();
                    break;
                }

                if (!hasLineOfSight)
                {
                    // When LOS is blocked but we should keep fighting, path toward the commanded target position but stop short of the desired range so the minion does not get stuck trying to reach an unreachable point.
                    float losRecoveryStopDistance = rangePolicy != null ? Mathf.Max(0f, rangePolicy.MinRange) : 0f;
                    MoveTowardsDistance(currentTarget.position, losRecoveryStopDistance);
                    break;
                }

                RepositionAroundTarget(currentTarget.position, desiredRange);
                break;

            case CombatPhase.AttackWindow:
            case CombatPhase.Cast:
                HoldRange(currentTarget.position, desiredRange);

                // Abilities are blocked when an obstacle breaks line of sight to the target.
                bool canAttack = !requireLineOfSightForAllAttacks || hasLineOfSight;
                if (canAttack)
                {
                    if (abilitySystem.TryUseBestAbility(transform, currentTarget, currentTime, sharedCombatStats, roleType))
                        PlayAttackAnimation();
                }
                break;

            case CombatPhase.Recover:
                HoldRange(currentTarget.position, desiredRange);
                break;
        }
    }

    private void MoveTowardsDistance(Vector3 targetPosition, float stopDistance)
    {
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;

        float distance = toTarget.magnitude;
        if (distance <= Mathf.Max(0f, stopDistance))
        {
            if (distance > 0.0001f)
            {
                SmoothFaceDirection(toTarget / distance);
            }

            ResetNavigationPath();
            return;
        }

        // NavMesh drives around corners/obstacles while keeping command/state logic unchanged.
        // AttackObject targets are solid NavMesh obstacles (breakable walls). Always use direct movement so the minion walks straight toward the wall and reaches melee range.
        bool isAttackObjectCommand = currentCommand != null && currentCommand.Type == CommandType.AttackObject;

        if (useNavMeshNavigation && !isAttackObjectCommand)
        {
            Vector3 stopPoint = targetPosition;

            if (stopDistance > 0.001f)
            {
                stopPoint -= (toTarget / Mathf.Max(distance, 0.0001f)) * stopDistance;
                stopPoint.y = targetPosition.y;
            }

            if (TryMoveAlongNavPath(stopPoint))
            {
                return;
            }

            if (ShouldUseDirectNavigationFallback(stopPoint))
            {
                MoveDirectly(toTarget / Mathf.Max(distance, 0.0001f));
                return;
            }

            HandleUnreachablePath();
            return;
        }

        Vector3 direction = toTarget / Mathf.Max(distance, 0.0001f);
        MoveDirectly(direction);
    }

    private void MoveDirectly(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return;

        direction.Normalize();
        transform.position += direction * GetRuntimeMoveSpeed() * Time.deltaTime;
        wasMovingThisFrame = true;
        lastMoveDir = direction;
        SmoothFaceDirection(direction);
    }

    private bool TryMoveAlongNavPath(Vector3 desiredDestination)
    {
        float now = Time.time;
        float repathInterval = Mathf.Max(0.05f, navRepathInterval);

        // Rebuild when the goal moved enough or the cached path is stale.
        if (!hasNavPath || now >= nextNavRepathTime || (navLastDestination - desiredDestination).sqrMagnitude > 0.35f * 0.35f)
        {
            if (!TryBuildNavPath(desiredDestination))
            {
                return false;
            }
        }

        Vector3[] corners = navPath.corners;
        if (corners == null || corners.Length == 0)
        {
            hasNavPath = false;
            return false;
        }

        navCornerIndex = Mathf.Clamp(navCornerIndex, 1, corners.Length - 1);
        float cornerTolerance = Mathf.Max(0.05f, navWaypointTolerance);

        while (navCornerIndex < corners.Length)
        {
            Vector3 toCorner = corners[navCornerIndex] - transform.position;
            toCorner.y = 0f;

            if (toCorner.sqrMagnitude <= cornerTolerance * cornerTolerance)
            {
                navCornerIndex++;
                continue;
            }

            Vector3 direction = toCorner.normalized;
            transform.position += direction * GetRuntimeMoveSpeed() * Time.deltaTime;
            wasMovingThisFrame = true;
            lastMoveDir = direction;
            SmoothFaceDirection(direction);
            return true;
        }

        return true;
    }

    private bool TryBuildNavPath(Vector3 desiredDestination)
    {
        if (navPath == null)
        {
            navPath = new NavMeshPath();
        }

        // Both start and destination must resolve onto the baked NavMesh before path calculation.
        nextNavRepathTime = Time.time + Mathf.Max(0.05f, navRepathInterval);

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit startHit, 1.0f, NavMesh.AllAreas))
        {
            hasNavPath = false;
            return false;
        }

        if (!NavMesh.SamplePosition(desiredDestination, out NavMeshHit destinationHit, Mathf.Max(0.1f, navTargetSampleRadius), NavMesh.AllAreas))
        {
            hasNavPath = false;
            return false;
        }

        bool calculated = NavMesh.CalculatePath(startHit.position, destinationHit.position, NavMesh.AllAreas, navPath);
        if (!calculated || navPath.status != NavMeshPathStatus.PathComplete || navPath.corners == null || navPath.corners.Length < 2)
        {
            hasNavPath = false;
            return false;
        }

        hasNavPath = true;
        navCornerIndex = 1;
        navLastDestination = desiredDestination;
        return true;
    }

    private bool ShouldUseDirectNavigationFallback(Vector3 desiredDestination)
    {
        bool hasStart = NavMesh.SamplePosition(transform.position, out _, 1.0f, NavMesh.AllAreas);
        bool hasDestination = NavMesh.SamplePosition(
            desiredDestination,
            out _,
            Mathf.Max(0.1f, navTargetSampleRadius),
            NavMesh.AllAreas);

        return !hasStart || !hasDestination;
    }

    private void HandleUnreachablePath()
    {
        ResetNavigationPath();

        if (Time.time < nextPathFailureRecoveryTime)
        {
            return;
        }

        nextPathFailureRecoveryTime = Time.time + Mathf.Max(0.1f, pathFailureCooldown);

        if (currentCommand != null)
        {
            currentCommand.LastFailureReason = FailureReason.TargetLost;
        }

        // Combat commands: keep the command alive and reset nav so it retries next interval.
        if (currentCommand != null
            && (currentCommand.Type == CommandType.AttackEnemy
                || currentCommand.Type == CommandType.AttackObject
                || currentCommand.Type == CommandType.SupportTarget))
        {
            return;
        }

        // Dismiss: path to formation failed — keep dismissed flag, go idle at current position.
        if (isDismissed)
        {
            Log("Path failure (Dismiss) — going Idle at current position.");
            ClearCommand();
            stateMachine.ForceState(MinionState.Idle);
            return;
        }

        // Follow-type commands: recall to player if configured, otherwise idle.
        if (recallOnPathFailure
            && currentCommand != null
            && currentCommand.Type != CommandType.Recall
            && IsValidTarget(followTarget))
        {
            Log("Path failure — recalling to player.");
            SetRecallCommand();
            return;
        }

        Log("Path failure — clearing command, going Idle.");
        ClearCommand();
        stateMachine.ForceState(MinionState.Idle);
    }

    private void ResetNavigationPath()
    {
        hasNavPath = false;
        navCornerIndex = 0;
        nextNavRepathTime = 0f;
        navLastDestination = transform.position;
    }

    private void ApplyLocalSeparation(float deltaTime)
    {
        if (!useLocalSeparation) return;

        // During post-attack recovery (Recover) the minion is briefly stationary — skip separation.
        // Approach is intentionally NOT skipped: the anti-movement stripping below keeps the forward
        // direction intact while still allowing minions to slide sideways past each other.
        CombatPhase phase = combatPhaseController.CurrentPhase;
        if (phase == CombatPhase.Recover) return;

        float radius = Mathf.Max(0.05f, separationRadius);
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            radius,
            separationHits,
            separationMask,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount <= 0) return;

        Vector3 push = Vector3.zero;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = separationHits[i];
            if (hit == null) continue;

            MinionCore other = hit.GetComponentInParent<MinionCore>();
            if (other == null || other == this) continue;

            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;

            float distance = away.magnitude;
            if (distance >= radius) continue;

            Vector3 dir = distance > 0.0001f ? away / distance : (transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.right);
            float overlap = radius - distance;
            float weight = overlap / radius;
            push += dir * weight;
        }

        if (push.sqrMagnitude <= 0.000001f) return;

        // When the minion is actively moving, remove the component of the separation push
        // that directly opposes its movement direction. This lets minions slide past each
        // other instead of blocking head-on, without disabling separation when idle.
        if (wasMovingThisFrame && lastMoveDir.sqrMagnitude > 0.001f)
        {
            Vector3 md = lastMoveDir.normalized;
            float along = Vector3.Dot(push, md);
            if (along < 0f)
                push -= md * along; // Strip anti-movement component
        }

        if (push.sqrMagnitude <= 0.000001f) return;

        Vector3 step = push * Mathf.Max(0f, separationStrength) * deltaTime;
        step.y = 0f;

        float maxStep = Mathf.Max(0.01f, maxSeparationStep);
        if (step.magnitude > maxStep)
        {
            step = step.normalized * maxStep;
        }

        // When using NavMesh, snap the result back onto the walkable surface so separation does not push the minion into an unwalkable area. Otherwise, just apply the separation step directly.
        if (useNavMeshNavigation)
        {
            Vector3 newPos = transform.position + step;
            if (NavMesh.SamplePosition(newPos, out NavMeshHit navHit, Mathf.Max(0.15f, separationRadius * 0.5f), NavMesh.AllAreas))
            {
                transform.position = navHit.position;
            }
            // else: no valid NavMesh point nearby — skip this step to stay on the navmesh.
        }
        else
        {
            transform.position += step;
        }
    }

    private void RepositionAroundTarget(Vector3 targetPosition, float desiredRange)
    {
        Vector3 offset = transform.position - targetPosition;
        offset.y = 0f;

        float distance = offset.magnitude;
        Vector3 facingDirection = (targetPosition - transform.position);
        facingDirection.y = 0f;

        if (distance < Mathf.Max(0.01f, desiredRange - 0.25f))
        {
            // Compute a point at desiredRange from the target in the direction away from it, and move toward that point to maintain spacing if the minion got too close.
            Vector3 away = offset.sqrMagnitude > 0.0001f ? offset.normalized : -transform.forward;
            Vector3 retreatPoint = targetPosition + away * Mathf.Max(0.1f, desiredRange);
            retreatPoint.y = targetPosition.y;
            MoveTowardsDistance(retreatPoint, 0f);
            return;
        }
        else if (distance > desiredRange + 0.25f)
        {
            MoveTowardsDistance(targetPosition, desiredRange);
            return;
        }

        if (!hasLineOfSight)
        {
            // When the range is already valid but vision is blocked, strafe around the target on the desired radius.
            Vector3 radial = offset.sqrMagnitude > 0.0001f ? offset.normalized : -transform.forward;
            Vector3 tangent = Vector3.Cross(Vector3.up, radial).normalized;
            if (tangent.sqrMagnitude <= 0.0001f)
            {
                tangent = transform.right.sqrMagnitude > 0.0001f ? transform.right : Vector3.right;
            }

            float orbitSign = (GetInstanceID() & 1) == 0 ? 1f : -1f;
            Vector3 orbitPoint = targetPosition + (radial + tangent * orbitSign).normalized * Mathf.Max(0.1f, desiredRange);
            orbitPoint.y = targetPosition.y;
            MoveTowardsDistance(orbitPoint, 0f);
            return;
        }

        if (facingDirection.sqrMagnitude > 0.0001f)
        {
            SmoothFaceDirection(facingDirection.normalized);
        }
    }

    private void HandleBlockedLineOfSight()
    {
        Log("Line of sight blocked — returning to Follow.");
        ResetNavigationPath();

        if (currentCommand != null)
        {
            currentCommand.LastFailureReason = FailureReason.NoLineOfSight;
        }

        if (IsValidTarget(followTarget))
        {
            SetFollowCommand();
            return;
        }

        ClearCommand();
        stateMachine.ForceState(MinionState.Idle);
    }

    private void HoldRange(Vector3 targetPosition, float desiredRange)
    {
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance > desiredRange + 0.35f)
        {
            MoveTowardsDistance(targetPosition, desiredRange);
            return;
        }

        if (distance < Mathf.Max(0f, desiredRange - 0.35f))
        {
            RepositionAroundTarget(targetPosition, desiredRange);
            return;
        }

        if (toTarget.sqrMagnitude > 0.0001f)
        {
            SmoothFaceDirection(toTarget.normalized);
        }
    }

    private void SmoothFaceDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private void UpdateDebugData()
    {
        currentState       = stateMachine.CurrentState;
        currentCombatPhase = combatPhaseController.CurrentPhase;
        currentCommandType = currentCommand != null ? currentCommand.Type : CommandType.None;

        if (currentState != _prevLogState)
        {
            Log($"State: {_prevLogState} → {currentState}");
            _prevLogState = currentState;
        }

        if (currentCombatPhase != _prevLogPhase)
        {
            Log($"CombatPhase: {_prevLogPhase} → {currentCombatPhase}");
            _prevLogPhase = currentCombatPhase;
        }

        if (currentCommandType != _prevLogCommand)
        {
            string targetName = currentCommand?.Target is UnityEngine.Object obj ? obj.name : "none";
            Log($"Command: {_prevLogCommand} → {currentCommandType} (target: {targetName})");
            _prevLogCommand = currentCommandType;
        }
    }

    private float GetDistanceToTarget(Transform target)
    {
        if (!IsValidTarget(target)) return 0f;

        // Combat distance is evaluated horizontally so slopes do not block attacks or state transitions.
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        return toTarget.magnitude;
    }

    private bool SnapToGround()
    {
        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.01f, groundRayStartHeight);

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, Mathf.Max(0.1f, groundRayLength), groundMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.transform == transform || hit.transform.IsChildOf(transform))
        {
            return false;
        }

        Vector3 position = transform.position;
        position.y = hit.point.y + groundOffset;
        transform.position = position;
        return true;
    }

    private void ApplyGravity(float deltaTime)
    {
        if (!useSimpleGravity) return;

        if (isGrounded)
        {
            verticalVelocity = 0f;
            return;
        }

        verticalVelocity -= Mathf.Max(0f, gravityAcceleration) * deltaTime;
        verticalVelocity = Mathf.Max(-Mathf.Max(0f, maxFallSpeed), verticalVelocity);
        transform.position += Vector3.up * (verticalVelocity * deltaTime);
    }

    private bool EvaluateLineOfSight(Transform target)
    {
        if (debugOverrideLineOfSight) return debugLineOfSightValue;
        if (!IsValidTarget(target)) return false;

        // Cast at combat height and inspect the nearest hit first so walls block before the target is considered visible.
        Vector3 start = transform.position + Vector3.up * Mathf.Max(0f, lineOfSightHeightOffset);
        Vector3 end = target.position + Vector3.up * Mathf.Max(0f, lineOfSightHeightOffset);
        Vector3 direction = end - start;
        float distance = direction.magnitude;
        if (distance <= 0.0001f) return true;

        int hitCount = Physics.RaycastNonAlloc(
            start,
            direction / distance,
            lineOfSightHits,
            distance,
            lineOfSightBlockMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0) return true;

        Array.Sort(lineOfSightHits, 0, hitCount, RaycastHitDistanceComparer.Instance);

        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = lineOfSightHits[i].transform;
            if (hitTransform == null) continue;

            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                continue;
            }

            if (hitTransform == target || hitTransform.IsChildOf(target))
            {
                return true;
            }

            return false;
        }

        return true;
    }

    private bool EvaluateAbilityReady(Transform target)
    {
        if (debugOverrideAbilityReady) return debugAbilityReadyValue;
        if (!IsValidTarget(target) || abilitySystem == null) return false;

        return abilitySystem.HasAnyReadyAbility(transform, target, Time.time, sharedCombatStats, roleType);
    }

    private float GetRuntimeMoveSpeed()
    {
        if (sharedCombatStats == null)
        {
            return moveSpeed;
        }

        return Mathf.Max(0.1f, sharedCombatStats.GetStat(CombatStatType.MoveSpeed));
    }

    private void HandleMissingCombatTarget()
    {
        Log("Combat target missing or dead — clearing command, going Idle.");
        ResetNavigationPath();

        if (currentCommand != null)
        {
            currentCommand.LastFailureReason = FailureReason.TargetLost;
        }

        combatPhaseController.Reset();
        ClearCommand();
        stateMachine.ForceState(MinionState.Idle);
    }

    private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
    {
        public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();

        public int Compare(RaycastHit x, RaycastHit y)
        {
            return x.distance.CompareTo(y.distance);
        }
    }
}
