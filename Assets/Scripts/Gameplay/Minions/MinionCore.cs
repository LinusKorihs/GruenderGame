using System;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CombatantStats))]
public partial class MinionCore : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private MinionSettings settings;
    [SerializeField] private MinionRoleType roleType = MinionRoleType.Melee;

    [Header("Runtime Targets")]
    [SerializeField] private Transform followTarget;

    [Header("Animation")]
    [SerializeField] private MinionAnimatorBridge animationBridge;

    // Runtime copies loaded from MinionSettings and the role's MinionBehaviourSettings at Initialize().
    private bool autoAssignCombatCommands;
    private float autoTargetRadius;
    private string enemyTag;
    private string allyTag;
    private string breakableTag;
    private float abilityCooldown;
    private GameObject rangedProjectilePrefab;
    private bool useHomingProjectiles;
    private float projectileSpeed;

    private float moveSpeed;
    private float rotationSpeed;
    private float followStopDistance;
    private bool useNavMeshNavigation;
    private float navRepathInterval;
    private float navTargetSampleRadius;
    private float navWaypointTolerance;
    private bool recallOnPathFailure;
    private float pathFailureCooldown;
    private bool useLocalSeparation;
    private float separationRadius;
    private float separationStrength;
    private float maxSeparationStep;
    private LayerMask separationMask;
    private bool snapToGround;
    private LayerMask groundMask;
    private float groundRayStartHeight;
    private float groundRayLength;
    private float groundOffset;
    private bool useSimpleGravity;
    private float gravityAcceleration;
    private float maxFallSpeed;
    private LayerMask lineOfSightBlockMask;
    private float lineOfSightHeightOffset;
    private bool requireLineOfSightForAllAttacks;
    private bool returnToFollowWhenLineOfSightBlocked;
    private StatusEffectDefinition supportBuffEffect;
    private StatusEffectDefinition supportDebuffEffect;

    private Transform currentTarget;
    private bool hasLineOfSight = true;
    private bool isAbilityReady = true;
    private bool debugOverrideLineOfSight;
    private bool debugLineOfSightValue = true;
    private bool debugOverrideAbilityReady;
    private bool debugAbilityReadyValue = true;
    private bool debugUseDistanceOverride;
    private float debugDistanceOverride;
    private bool isGrounded;
    private float verticalVelocity;
    private NavMeshPath navPath;
    private int navCornerIndex;
    private bool hasNavPath;
    private float nextNavRepathTime;
    private Vector3 navLastDestination;
    private float nextPathFailureRecoveryTime;
    // Reused LOS hit buffer to avoid per-frame allocations during combat checks.
    private readonly RaycastHit[] lineOfSightHits = new RaycastHit[16];

    private bool debugForceState;
    private MinionState debugForcedState;
    private bool debugForceCombatPhase;
    private CombatPhase debugForcedCombatPhase;

    // Tracks movement direction this frame so separation can allow sliding past other minions.
    private bool wasMovingThisFrame;
    private Vector3 lastMoveDir;

    [Header("Debug Visuals")]
    [SerializeField] private bool drawRoleRangeGizmos;

    [Header("Debug")]
    [SerializeField] private bool enableLogs;

    [Header("Runtime Debug")]
    [SerializeField] private MinionState currentState;
    [SerializeField] private CombatPhase currentCombatPhase;
    [SerializeField] private CommandType currentCommandType;
    [SerializeField] private float currentDistanceToTarget;

    // Previous-frame values used solely to detect and log transitions.
    private MinionState      _prevLogState   = MinionState.Idle;
    private CombatPhase      _prevLogPhase   = CombatPhase.None;
    private CommandType      _prevLogCommand = CommandType.None;

    private IMinionRole currentRole;
    private MinionCommand currentCommand;

    private MinionDecisionLayer decisionLayer;
    private MinionStateMachine stateMachine;
    private MinionCombatPhaseController combatPhaseController;
    private MinionTickScheduler tickScheduler;
    private MinionAbilitySystem abilitySystem;
    private CombatantStats sharedCombatStats;
    private readonly Collider[] separationHits = new Collider[24];

    private MinionIntent currentIntent;

    // Set by SetDismissCommand; prevents auto-combat while the minion is dismissed to a formation position.
    private bool isDismissed;
    // How far the player must move from the minion before the minion resumes following (set at dismiss time).
    private float dismissResumeRange;

    public MinionRoleType RoleType => roleType; // Exposed runtime metadata for commander/input systems.
    public SupportMode? ActiveSupportMode => currentRole != null ? currentRole.GetSupportMode() : null;
    public bool IsDismissed => isDismissed;
    public Transform FollowTarget => followTarget;

    // Fired just before the GameObject is destroyed due to death.
    public event Action<MinionCore> Died;

    public void SetFollowTarget(Transform target, bool followImmediately = true)
    {
        followTarget = target;

        if (followImmediately && target != null)
        {
            SetFollowCommand();
        }
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnDestroy()
    {
        if (sharedCombatStats != null) sharedCombatStats.Died -= OnCombatantDied;
    }

    private void OnCombatantDied()
    {
        Log("Died.");
        PlayDeathAnimation();
        ClearCommand();
        stateMachine.ForceState(MinionState.Idle);
        Died?.Invoke(this);
    }

    internal void Log(string msg)
    {
        if (enableLogs) Debug.Log($"[Minion:{name}] {msg}");
    }

    private void Initialize()
    {
        if (settings == null)
        {
            Debug.LogError($"[{name}] Missing MinionSettings.");
            return;
        }

        decisionLayer = new MinionDecisionLayer();
        stateMachine = new MinionStateMachine();
        combatPhaseController = new MinionCombatPhaseController();
        tickScheduler = new MinionTickScheduler();
        abilitySystem = new MinionAbilitySystem();
        abilitySystem.Logger = Log;
        sharedCombatStats = GetComponent<CombatantStats>();
        if (animationBridge == null)
            animationBridge = GetComponentInChildren<MinionAnimatorBridge>(true);
        navPath = new NavMeshPath();

        if (sharedCombatStats != null) sharedCombatStats.Died += OnCombatantDied;

        // Load per-role behaviour values from the ScriptableObject.
        RoleSettings rs = settings.GetForRole(roleType);
        if (rs == null)
        {
            Debug.LogError($"[{name}] No RoleSettings found for role: {roleType}");
            return;
        }

        MinionBehaviourSettings b = rs.Behaviour ?? new MinionBehaviourSettings();
        moveSpeed                       = b.MoveSpeed;
        rotationSpeed                   = b.RotationSpeed;
        followStopDistance              = b.FollowStopDistance;
        useNavMeshNavigation            = b.UseNavMeshNavigation;
        navRepathInterval               = b.NavRepathInterval;
        navTargetSampleRadius           = b.NavTargetSampleRadius;
        navWaypointTolerance            = b.NavWaypointTolerance;
        recallOnPathFailure             = b.RecallOnPathFailure;
        pathFailureCooldown             = b.PathFailureCooldown;
        useLocalSeparation              = b.UseLocalSeparation;
        separationRadius                = b.SeparationRadius;
        separationStrength              = b.SeparationStrength;
        maxSeparationStep               = b.MaxSeparationStep;
        separationMask                  = b.SeparationMask;
        snapToGround                    = b.SnapToGround;
        groundMask                      = b.GroundMask;
        groundRayStartHeight            = b.GroundRayStartHeight;
        groundRayLength                 = b.GroundRayLength;
        groundOffset                    = b.GroundOffset;
        useSimpleGravity                = b.UseSimpleGravity;
        gravityAcceleration             = b.GravityAcceleration;
        maxFallSpeed                    = b.MaxFallSpeed;
        lineOfSightBlockMask            = b.LineOfSightBlockMask;
        lineOfSightHeightOffset         = b.LineOfSightHeightOffset;
        requireLineOfSightForAllAttacks = b.RequireLineOfSightForAllAttacks;
        returnToFollowWhenLineOfSightBlocked = b.ReturnToFollowWhenLineOfSightBlocked;
        autoAssignCombatCommands        = b.AutoAssignCombatCommands;
        autoTargetRadius                = b.AutoTargetRadius;
        rangedProjectilePrefab          = b.ProjectilePrefab;
        useHomingProjectiles            = b.UseHomingProjectiles;
        projectileSpeed                 = b.ProjectileSpeed;

        // Tags are shared across roles and live on the root MinionSettings.
        enemyTag     = settings.EnemyTag;
        allyTag      = settings.AllyTag;
        breakableTag = settings.BreakableTag;

        // Ability cooldown is per-role so each type can have a different attack rhythm.
        abilityCooldown = b.AbilityCooldown;

        // Support effects are stored on SupportRoleSettings only.
        if (rs is SupportRoleSettings sr)
        {
            supportBuffEffect   = sr.SupportBuffEffect;
            supportDebuffEffect = sr.SupportDebuffEffect;
        }

        currentRole = MinionRoleFactory.Create(roleType, settings);

        if (currentRole == null)
        {
            Debug.LogError($"[{name}] Failed to create role for type: {roleType}");
            return;
        }

        abilitySystem.BuildDefaultLoadout(
            currentRole, supportBuffEffect, supportDebuffEffect, abilityCooldown,
            rangedProjectilePrefab, useHomingProjectiles, projectileSpeed);

        currentCommand = new MinionCommand
        {
            Type = CommandType.None,
            Target = null,
            TargetPosition = transform.position,
            Priority = 0,
            IssuedTime = 0f,
            TimeToLive = 0f,
            Source = CommandSource.System,
            InterruptPolicy = InterruptPolicy.None,
            LastFailureReason = FailureReason.None
        };

        currentIntent = MinionIntent.Idle();
    }

    private void Update()
    {
        if (settings == null || currentRole == null) return;
        if (sharedCombatStats != null && sharedCombatStats.IsDead) return;

        if (snapToGround)
        {
            isGrounded = SnapToGround();
        }

        ApplyGravity(Time.deltaTime);

        float currentTime = Time.time;

        // Decision is not needed every frame.
        if (tickScheduler.ShouldRunDecision(currentTime, settings.TickRates))
        {
            TryAssignAutoCommand(currentTime);

            currentIntent = decisionLayer.ResolveIntent(currentCommand, currentTime);
            stateMachine.UpdateState(currentIntent);

            if (debugForceState)
            {
                stateMachine.ForceState(debugForcedState);
            }
        }

        // Combat phase can be updated every frame for a smooth prototype.
        UpdateCombatPhase();

        // Execute the current state and combat phase.
        wasMovingThisFrame = false;
        ExecuteCurrentState(currentTime);
        UpdateAnimationState();

        // Keep nearby minions from stacking into the same spot.
        ApplyLocalSeparation(Time.deltaTime);

        // Push debug values into inspector.
        UpdateDebugData();
    }

    private void UpdateCombatPhase()
    {
        currentTarget = ResolveActiveTarget();
        currentDistanceToTarget = debugUseDistanceOverride ? debugDistanceOverride : GetDistanceToTarget(currentTarget);
        hasLineOfSight = EvaluateLineOfSight(currentTarget);
        isAbilityReady = EvaluateAbilityReady(currentTarget);

        combatPhaseController.UpdatePhase(
            stateMachine.CurrentState,
            currentRole,
            currentDistanceToTarget,
            hasLineOfSight,
            isAbilityReady
        );

        if (debugForceCombatPhase)
        {
            combatPhaseController.ForcePhase(debugForcedCombatPhase);
        }
    }

    private void ExecuteCurrentState(float currentTime)
    {
        switch (stateMachine.CurrentState)
        {
            case MinionState.Idle:
                return;

            case MinionState.Follow:
                ExecuteFollow();
                return;

            case MinionState.Combat:
                ExecuteCombat(currentTime);
                return;
        }
    }

    private void UpdateAnimationState()
    {
        if (animationBridge == null) return;
        if (sharedCombatStats != null && sharedCombatStats.IsDead) return;

        if (wasMovingThisFrame)
            animationBridge.PlayWalk();
        else
            animationBridge.PlayIdle();
    }

    private void PlayAttackAnimation()
    {
        if (animationBridge != null)
            animationBridge.PlayAttack();
    }

    private void PlayDeathAnimation()
    {
        if (animationBridge != null)
            animationBridge.PlayDeath();
    }
}

