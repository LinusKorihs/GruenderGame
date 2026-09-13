using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMinionCommander : MonoBehaviour
{
    private enum CommandPreviewType
    {
        None,
        Attack,
        Support,
        Invalid
    }

    // Stores a dismissed minion's local-space offset so it can track the player's movement and rotation.
    private struct FormationSlot
    {
        public MinionCore Minion;
        public Vector2 LocalXZ; // x = right-axis offset, y = forward-axis offset, relative to player
        public Vector3 LastIssuedWorldPos;
    }

    [Header("References")]
    [SerializeField] private PlayerMinionCommanderSettings settings;
    [SerializeField] private GroundCursor cursor;
    [SerializeField] private Transform player;
    [SerializeField] private Transform playerMesh;

    [Header("Input")]
    [SerializeField] private InputActionReference commandAction;
    [SerializeField] private InputActionReference callAction;
    [SerializeField] private InputActionReference dismissAction;
    [SerializeField] private InputActionReference selectPreviousMinionTypeAction;
    [SerializeField] private InputActionReference selectNextMinionTypeAction;


    [Header("Minion Selection")]
    [SerializeField] private MinionCore[] controlledMinions;
    [SerializeField] private bool autoFindMinionsIfEmpty = true;
    [SerializeField] private MinionRoleType selectedRole = MinionRoleType.Melee;
    [Header("Command Preview")]
    [SerializeField] private Renderer[] previewRenderers;

    [Header("Debug")]
    [SerializeField] private bool enableLogs;

    private readonly Collider[] commandHits = new Collider[32];

    // Runtime list of live minions — cleared/updated as minions die or are registered.
    private readonly List<MinionCore> runtimeMinions = new List<MinionCore>();
    private MinionCore[] cachedRuntimeArray = System.Array.Empty<MinionCore>();
    private bool runtimeListDirty = true;

    // Formation tracking: dismissed minion slots relative to player position/facing.
    private readonly List<FormationSlot> activeFormationSlots = new List<FormationSlot>();
    private float nextFormationUpdateTime;

    private float nextAutoFindRefreshTime;
    private PlayerAim playerAim;
    private KelpAnimatorBridge kelpAnimator;
    private MaterialPropertyBlock previewPropertyBlock;
    private Color lastAppliedPreviewColor;
    private bool hasAppliedPreviewColor;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private int knownMeleeCount;
    private int knownRangedCount;
    private int knownSupportCount;
    private int lastLiveMelee = -1;
    private int lastLiveRanged = -1;
    private int lastLiveSupport = -1;
    private MinionRoleType lastNotifiedRole;
    private int nextMeleeCommandIndex;
    private int nextRangedCommandIndex;
    private int nextSupportCommandIndex;
    private void Log(string msg) { if (enableLogs) Debug.Log(msg); }

    public event Action MinionSelectionChanged;
    public event Action<MinionRoleType> EmptyMinionSelectionRequested;

    private void Awake()
    {
        ResolveInputActions();

        if (cursor == null) cursor = GetComponentInChildren<GroundCursor>();
        if (player == null) player = transform;
        // Resolve PlayerAim for formation facing. Search the player hierarchy first, then the scene.
        playerAim = player.GetComponentInParent<PlayerAim>();
        if (playerAim == null) playerAim = player.GetComponentInChildren<PlayerAim>();
        // Auto-pick preview renderers from cursor hierarchy if none were assigned.
        if ((previewRenderers == null || previewRenderers.Length == 0) && cursor != null)
        {
            previewRenderers = cursor.GetComponentsInChildren<Renderer>(true);

            // Fallback: if marker is not a child of cursor, resolve renderers directly from marker.
            if ((previewRenderers == null || previewRenderers.Length == 0) && cursor.Marker != null)
            {
                previewRenderers = cursor.Marker.GetComponentsInChildren<Renderer>(true);
            }
        }

        previewPropertyBlock = new MaterialPropertyBlock();
    }

    private void ResolveInputActions()
    {
        PlayerInput playerInput = GetComponentInParent<PlayerInput>();

        commandAction = PlayerInputActionResolver.Resolve(commandAction, playerInput, "Player", "MinionCommand", this);
        callAction = PlayerInputActionResolver.Resolve(callAction, playerInput, "Player", "MinionCall", this);
        dismissAction = PlayerInputActionResolver.Resolve(dismissAction, playerInput, "Player", "MinionDismiss", this);
        selectPreviousMinionTypeAction = PlayerInputActionResolver.Resolve(selectPreviousMinionTypeAction, playerInput, "Player", "MinionSelectPrevious", this, required: false);
        selectNextMinionTypeAction = PlayerInputActionResolver.Resolve(selectNextMinionTypeAction, playerInput, "Player", "MinionSelectNext", this, required: false);
    }

    // Exposed for editor display only.
    public InputActionReference CommandAction => commandAction;
    public InputActionReference CallAction    => callAction;
    public InputActionReference DismissAction => dismissAction;
    public InputActionReference SelectPreviousMinionTypeAction => selectPreviousMinionTypeAction;
    public InputActionReference SelectNextMinionTypeAction => selectNextMinionTypeAction;
    public MinionRoleType SelectedRole => selectedRole;

    public void SetKnownMinionCounts(int melee, int ranged, int support)
    {
        int previousMelee = knownMeleeCount;
        int previousRanged = knownRangedCount;
        int previousSupport = knownSupportCount;

        knownMeleeCount = Mathf.Max(knownMeleeCount, Mathf.Max(0, melee));
        knownRangedCount = Mathf.Max(knownRangedCount, Mathf.Max(0, ranged));
        knownSupportCount = Mathf.Max(knownSupportCount, Mathf.Max(0, support));

        if (previousMelee != knownMeleeCount ||
            previousRanged != knownRangedCount ||
            previousSupport != knownSupportCount)
        {
            MinionSelectionChanged?.Invoke();
        }
    }

    private void Start()
    {
        // Register manually assigned minions first.
        if (controlledMinions != null)
        {
            for (int i = 0; i < controlledMinions.Length; i++)
                RegisterMinion(controlledMinions[i]);
        }

        // Auto-find minions only when none were manually assigned.
        if (autoFindMinionsIfEmpty && runtimeMinions.Count == 0)
            RefreshAutoFoundMinions();

        SeedKnownCountsFromRunSetupData();
        RefreshSelectionState(forceNotify: true);
    }

    private void OnDestroy()
    {
        for (int i = runtimeMinions.Count - 1; i >= 0; i--)
            UnregisterMinion(runtimeMinions[i]);
    }

    // Register a minion and subscribe to its death event.
    public void RegisterMinion(MinionCore minion)
    {
        if (minion == null || runtimeMinions.Contains(minion)) return;
        runtimeMinions.Add(minion);
        runtimeListDirty = true;
        minion.Died += OnMinionDied;
        RefreshSelectionState();
    }

    public void ClearRegisteredMinions()
    {
        for (int i = runtimeMinions.Count - 1; i >= 0; i--)
        {
            MinionCore minion = runtimeMinions[i];
            if (minion != null)
            {
                minion.Died -= OnMinionDied;
            }
        }

        runtimeMinions.Clear();
        activeFormationSlots.Clear();
        cachedRuntimeArray = System.Array.Empty<MinionCore>();
        runtimeListDirty = true;
        knownMeleeCount = 0;
        knownRangedCount = 0;
        knownSupportCount = 0;
        ResetRoleCommandIndices();
        RefreshSelectionState();
    }

    private void UnregisterMinion(MinionCore minion)
    {
        if (minion == null) return;
        if (runtimeMinions.Remove(minion))
            runtimeListDirty = true;
        minion.Died -= OnMinionDied;
        RemoveFormationSlot(minion);
        RefreshSelectionState();
    }

    private void OnMinionDied(MinionCore minion)
    {
        UnregisterMinion(minion);
    }

    private void RefreshAutoFoundMinions()
    {
        MinionCore[] found = FindObjectsByType<MinionCore>(FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
            RegisterMinion(found[i]);

        nextAutoFindRefreshTime = Time.time + Mathf.Max(0.05f, settings != null ? settings.autoFindRefreshInterval : 5f);
    }

    private void OnEnable()
    {
        if (commandAction != null) commandAction.action.Enable();
        if (callAction    != null) callAction.action.Enable();
        if (dismissAction != null) dismissAction.action.Enable();
        if (selectPreviousMinionTypeAction != null) selectPreviousMinionTypeAction.action.Enable();
        if (selectNextMinionTypeAction != null) selectNextMinionTypeAction.action.Enable();
    }

    private void OnDisable()
    {
        if (commandAction != null) commandAction.action.Disable();
        if (callAction    != null) callAction.action.Disable();
        if (dismissAction != null) dismissAction.action.Disable();
        if (selectPreviousMinionTypeAction != null) selectPreviousMinionTypeAction.action.Disable();
        if (selectNextMinionTypeAction != null) selectNextMinionTypeAction.action.Disable();
    }

    private void Update()
    {
        // Preview updates continuously so the player gets immediate cursor feedback.
        UpdateCommandPreview();
        RefreshSelectionState();

        if (WasSelectPreviousPressedThisFrame())
        {
            SelectPreviousMinionType();
        }

        if (WasSelectNextPressedThisFrame())
        {
            SelectNextMinionType();
        }

        if (WasCommandPressedThisFrame())
        {
            OrderNextMinion();
        }

        if (WasCallPressedThisFrame())
        {
            CallMinions();
        }

        if (WasDismissPressedThisFrame())
        {
            DismissMinions();
        }

        UpdateFormationSlots();
    }

    // Switches the active support action (Heal/Buff/Debuff) for all support minions.
    public void SetSupportModeForAll(SupportMode mode)
    {
        MinionCore[] minions = ResolveControlledMinions();
        for (int i = 0; i < minions.Length; i++)
        {
            MinionCore minion = minions[i];
            if (minion == null || minion.RoleType != MinionRoleType.Support) continue;
            minion.TrySetSupportMode(mode);
        }
    }

    // Issues one command per press for the currently selected minion role.
    public void OrderNextMinion()
    {
        MinionCore[] minions = ResolveControlledMinions();
        if (minions.Length == 0 || cursor == null) return;

        if (!EnsureSelectedRoleAvailable())
        {
            EmptyMinionSelectionRequested?.Invoke(selectedRole);
            return;
        }

        Transform target = ResolveCommandTarget();
        if (target == null)
        {
            OrderSelectedMinionToPosition(minions, cursor.WorldPos);
            return;
        }

        string enemyTagValue = settings.enemyTag;
        string breakableTagValue = settings.breakableTag;
        bool isEnemy     = HasTag(target, enemyTagValue);
        bool isBreakable = HasTag(target, breakableTagValue);
        bool isAllyMinion = target.GetComponentInParent<MinionCore>() != null && target != player;

        if (isEnemy)
        {
            MinionCore chosen = PickNextAttacker(minions, target, selectedRole);
            if (chosen != null)
            {
                RemoveFormationSlot(chosen);
                chosen.SetAttackEnemyCommand(target);
                ResolveKelpAnimator()?.PlayOrderMinions();
                Log($"[MinionCommander] Order: {chosen.name} ({chosen.RoleType}) → attack enemy '{target.name}'.");
            }
            else
            {
                EmptyMinionSelectionRequested?.Invoke(selectedRole);
                Log($"[MinionCommander] Order: no available minion to attack '{target.name}' (all already engaged).");
            }

            return;
        }

        if (isBreakable)
        {
            MinionCore chosen = PickNextAttacker(minions, target, selectedRole, requireEnemy: false);
            if (chosen != null)
            {
                RemoveFormationSlot(chosen);
                chosen.SetAttackObjectCommand(target);
                ResolveKelpAnimator()?.PlayOrderMinions();
                Log($"[MinionCommander] Order: {chosen.name} ({chosen.RoleType}) → attack object '{target.name}'.");
            }
            else
            {
                EmptyMinionSelectionRequested?.Invoke(selectedRole);
                Log($"[MinionCommander] Order: no available minion to attack object '{target.name}'.");
            }

            return;
        }

        if (isAllyMinion)
        {
            if (selectedRole != MinionRoleType.Support)
            {
                EmptyMinionSelectionRequested?.Invoke(selectedRole);
                Log($"[MinionCommander] Order: selected role {selectedRole} cannot support ally '{target.name}'.");
                return;
            }

            // Support minions can be ordered to support an ally.
            for (int i = 0; i < minions.Length; i++)
            {
                MinionCore minion = minions[i];
                if (!IsLiveMinion(minion) || minion.RoleType != MinionRoleType.Support) continue;

                if (minion.CanAcceptSupportTarget(target))
                {
                    RemoveFormationSlot(minion);
                    minion.SetSupportCommand(target);
                    ResolveKelpAnimator()?.PlayOrderMinions();
                    Log($"[MinionCommander] Order: {minion.name} (Support) → support ally '{target.name}'.");
                    return;
                }
            }

            EmptyMinionSelectionRequested?.Invoke(selectedRole);
            return;
        }

        EmptyMinionSelectionRequested?.Invoke(selectedRole);
    }

    // Returns the next available attacker in the selected role, rotating through repeated commands.
    private MinionCore PickNextAttacker(MinionCore[] minions, Transform target, MinionRoleType role, bool requireEnemy = true)
    {
        return PickNextRoleMinion(minions, role, minion =>
            CanIssueAttackCommand(minion, target, requireEnemy));
    }

    private bool HasAvailableAttacker(MinionCore[] minions, Transform target, MinionRoleType role, bool requireEnemy = true)
    {
        if (minions == null || target == null) return false;

        for (int i = 0; i < minions.Length; i++)
        {
            MinionCore minion = minions[i];
            if (!IsLiveMinion(minion)) continue;
            if (minion.RoleType != role) continue;
            if (CanIssueAttackCommand(minion, target, requireEnemy)) return true;
        }

        return false;
    }

    private static bool CanIssueAttackCommand(MinionCore minion, Transform target, bool requireEnemy)
    {
        if (!IsLiveMinion(minion) || target == null) return false;
        if (minion.IsTargeting(target)) return false;

        if (requireEnemy && minion.RoleType == MinionRoleType.Support)
        {
            return minion.ActiveSupportMode == SupportMode.Debuff;
        }

        return true;
    }

    // Sends an impulse wave: all minions within callRange resume following the player.
    public void CallMinions()
    {
        MinionCore[] minions = ResolveControlledMinions();
        if (minions.Length == 0) return;

        float rangeSq = settings.callRange * settings.callRange;
        int count = 0;

        for (int i = 0; i < minions.Length; i++)
        {
            MinionCore minion = minions[i];
            if (minion == null) continue;

            Vector3 delta = minion.transform.position - player.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > rangeSq) continue;

            minion.SetRecallCommand();
            count++;
            Log($"[MinionCommander] Call: {minion.name} ({minion.RoleType}) recalled to player.");
        }

        Log($"[MinionCommander] Call wave fired — {count} minion(s) recalled (range: {settings.callRange}m).");
        if (count > 0)
        {
            ResolveKelpAnimator()?.PlayCallMinions();
            PlayCallDismissPulse(settings.callPulseColor);
        }

        activeFormationSlots.Clear();
    }

    // Sends all in-range minions to typed formation positions around the player, then idles them.
    public void DismissMinions()
    {
        MinionCore[] minions = ResolveControlledMinions();
        if (minions.Length == 0) return;

        float rangeSq = settings.callRange * settings.callRange;

        // Collect affected minions per role.
        var meleeGroup   = new List<MinionCore>();
        var rangedGroup  = new List<MinionCore>();
        var supportGroup = new List<MinionCore>();

        for (int i = 0; i < minions.Length; i++)
        {
            MinionCore minion = minions[i];
            if (minion == null) continue;

            Vector3 delta = minion.transform.position - player.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > rangeSq) continue;

            switch (minion.RoleType)
            {
                case MinionRoleType.Melee:   meleeGroup.Add(minion);   break;
                case MinionRoleType.Ranged:  rangedGroup.Add(minion);  break;
                case MinionRoleType.Support: supportGroup.Add(minion); break;
            }
        }

        int totalCount = meleeGroup.Count + rangedGroup.Count + supportGroup.Count;
        if (totalCount == 0)
        {
            Log("[MinionCommander] Dismiss: no minions within range.");
            return;
        }

        // Formation: three group centres spread sideways relative to player facing. Melee left, Ranged middle, Support right.
        Vector3 forward = GetFormationForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float gs = settings.dismissFormationGroupSpacing;

        Vector3 meleeCentre   = player.position - right * gs;
        Vector3 rangedCentre  = player.position;
        Vector3 supportCentre = player.position + right * gs;

        activeFormationSlots.Clear();
        SendGroupToFormation(meleeGroup,   meleeCentre,   forward, right, -gs);
        SendGroupToFormation(rangedGroup,  rangedCentre,  forward, right,  0f);
        SendGroupToFormation(supportGroup, supportCentre, forward, right, +gs);

        ResolveKelpAnimator()?.PlayDismiss();
        PlayCallDismissPulse(settings.dismissPulseColor);

        Log($"[MinionCommander] Dismiss: {totalCount} minion(s) sent to formation " +
            $"(Melee: {meleeGroup.Count}, Ranged: {rangedGroup.Count}, Support: {supportGroup.Count}).");
    }

    private void PlayCallDismissPulse(Color color)
    {
        if (settings == null || !settings.enableCallDismissPulse) return;
        if (playerMesh == null) return;

        float endRadius = settings.pulseEndRadius > 0f ? settings.pulseEndRadius : settings.callRange;
        CommandPulseEffect.Spawn(
            playerMesh,
            color,
            settings.pulseStartRadius,
            endRadius,
            settings.pulseDuration,
            settings.pulseRingWidth,
            settings.pulseGroundOffset,
            settings.pulseLightIntensity);
    }

    // Distributes a group of minions to staggered positions around a centre point and records their local slots.
    private void SendGroupToFormation(List<MinionCore> group, Vector3 centre, Vector3 forward, Vector3 right, float centreLateralOffset)
    {
        if (group.Count == 0) return;

        float spacing = settings.dismissFormationMemberSpacing;
        // Offset each member alternately: 0, +1, -1, +2, -2, ...
        for (int i = 0; i < group.Count; i++)
        {
            MinionCore minion = group[i];
            int slot = i / 2 + 1;
            float sign = (i % 2 == 0) ? -1f : 1f;
            float memberLateral = i == 0 ? 0f : slot * sign * spacing;
            Vector3 formationPos = centre + right * memberLateral - forward * 1.5f;

            minion.SetDismissCommand(formationPos, settings.dismissResumeFollowRange);
            Log($"[MinionCommander] Dismiss: {minion.name} ({minion.RoleType}) → formation pos {formationPos}.");

            activeFormationSlots.Add(new FormationSlot
            {
                Minion             = minion,
                LocalXZ            = new Vector2(centreLateralOffset + memberLateral, -1.5f),
                LastIssuedWorldPos = formationPos,
            });
        }
    }

    // Recomputes world-space slot positions at formationUpdateInterval and pushes them to dismissed minions.
    private void UpdateFormationSlots()
    {
        if (activeFormationSlots.Count == 0 || settings == null) return;
        if (Time.time < nextFormationUpdateTime) return;
        nextFormationUpdateTime = Time.time + settings.formationUpdateInterval;

        Vector3 forward = GetFormationForward();
        if (forward == Vector3.zero) return;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        float thresholdSq = settings.formationUpdateThreshold * settings.formationUpdateThreshold;

        for (int i = activeFormationSlots.Count - 1; i >= 0; i--)
        {
            FormationSlot slot = activeFormationSlots[i];
            if (slot.Minion == null)
            {
                activeFormationSlots.RemoveAt(i);
                continue;
            }

            Vector3 newTarget = player.position
                + right   * slot.LocalXZ.x
                + forward * slot.LocalXZ.y;

            if ((newTarget - slot.LastIssuedWorldPos).sqrMagnitude < thresholdSq) continue;

            slot.Minion.SetDismissCommand(newTarget, settings.dismissResumeFollowRange);
            slot.LastIssuedWorldPos = newTarget;
            activeFormationSlots[i] = slot;
        }
    }

    // Returns the player's movement-facing direction (XZ normalized) used for formation placement.
    private Vector3 GetFormationForward()
    {
        Vector3 dir;
        if (playerAim != null)
        {
            dir = playerAim.FacingDirection;
        }
        else
        {
            dir = player.forward;
        }
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
        return dir.normalized;
    }

    // Draws the call/dismiss range sphere in the editor for tuning.
    private void OnDrawGizmosSelected()
    {
        if (settings == null) return;

        Transform origin = player != null ? player : transform;
        Color c = settings.callRangeGizmoColor;
        Gizmos.color = c;
        Gizmos.DrawWireSphere(origin.position, settings.callRange);
        Gizmos.color = new Color(c.r, c.g, c.b, c.a * 0.15f);
        Gizmos.DrawSphere(origin.position, settings.callRange);
    }

    // Resolves the command target strictly from the cursor position.
    private Transform ResolveCommandTarget()
    {
        return ResolveTargetFromCursor(includeLockTarget: false, includeAimAssistTarget: false, acquireRadius: settings.commandAcquireRadius);
    }

    // Resolves preview target strictly from the cursor position (no lock/aim-assist shortcuts).
    private Transform ResolvePreviewTarget()
    {
        return ResolveTargetFromCursor(includeLockTarget: false, includeAimAssistTarget: false, acquireRadius: settings.previewAcquireRadius);
    }

    // Shared target resolution for both command issuing and preview modes.
    private Transform ResolveTargetFromCursor(bool includeLockTarget, bool includeAimAssistTarget, float acquireRadius)
    {
        if (includeLockTarget && cursor.IsLocked && IsValidTarget(cursor.LockedTarget))
        {
            return cursor.LockedTarget;
        }

        if (includeAimAssistTarget && IsValidTarget(cursor.AimAssistTarget))
        {
            return cursor.AimAssistTarget;
        }

        Vector3 origin = cursor.WorldPos;
        string enemyTagValue = settings.enemyTag;
        string breakableTagValue = settings.breakableTag;
        QueryTriggerInteraction qti = settings.includeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
        int hitCount = Physics.OverlapSphereNonAlloc(origin, Mathf.Max(0.05f, acquireRadius), commandHits, settings.commandTargetMask, qti);
        if (hitCount <= 0) return null;

        Transform bestEnemy = null;
        float bestEnemySq = float.PositiveInfinity;
        Transform bestBreakable = null;
        float bestBreakableSq = float.PositiveInfinity;
        Transform bestUnknown = null;
        float bestUnknownSq = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = commandHits[i];
            if (hit == null) continue;

            Transform candidate = hit.transform;
            if (!IsValidTarget(candidate)) continue;

            float sq = (candidate.position - origin).sqrMagnitude;
            Transform enemyTarget = FindTaggedTransform(candidate, enemyTagValue);
            if (enemyTarget != null)
            {
                if (!HasLineOfSightToTarget(enemyTarget)) continue;

                if (sq < bestEnemySq)
                {
                    bestEnemySq = sq;
                    bestEnemy = enemyTarget;
                }
                continue;
            }

            Transform breakableTarget = FindTaggedTransform(candidate, breakableTagValue);
            if (breakableTarget != null && breakableTagValue != enemyTagValue)
            {
                if (!HasLineOfSightToTarget(breakableTarget)) continue;

                if (sq < bestBreakableSq)
                {
                    bestBreakableSq = sq;
                    bestBreakable = breakableTarget;
                }

                continue;
            }

            // Ally-minion targeting is intentionally disabled for now.
            if (candidate.GetComponentInParent<MinionCore>() != null)
            {
                continue;
            }

            // Skip candidates behind walls.
            if (!HasLineOfSightToTarget(candidate)) continue;

            // Unknown object under cursor
            if (IsEnvironmentCandidate(candidate))
            {
                continue;
            }

            if (sq < bestUnknownSq)
            {
                bestUnknownSq = sq;
                bestUnknown = candidate;
            }
        }

        if (bestEnemy != null) return bestEnemy;
        if (bestBreakable != null) return bestBreakable;
        return bestUnknown;
    }

    // Computes and applies command-preview color to cursor visuals.
    private void UpdateCommandPreview()
    {
        if (!settings.enableCommandPreview)
        {
            return;
        }

        MinionCore[] minions = ResolveControlledMinions();
        if (cursor == null || minions.Length == 0)
        {
            ApplyPreviewColor(settings.previewNoTargetColor);
            return;
        }

        Transform target = ResolvePreviewTarget();
        if (!IsValidTarget(target))
        {
            ApplyPreviewColor(settings.previewNoTargetColor);
            return;
        }

        CommandPreviewType previewType = EvaluatePreviewType(target, minions);
        switch (previewType)
        {
            case CommandPreviewType.Attack:
                ApplyPreviewColor(settings.previewAttackColor);
                break;

            case CommandPreviewType.Support:
                ApplyPreviewColor(settings.previewSupportColor);
                break;

            case CommandPreviewType.Invalid:
                ApplyPreviewColor(settings.previewInvalidColor);
                break;

            default:
                ApplyPreviewColor(settings.previewNoTargetColor);
                break;
        }
    }

    private CommandPreviewType EvaluatePreviewType(Transform target, MinionCore[] minions)
    {
        string enemyTagValue = settings.enemyTag;
        string breakableTagValue = settings.breakableTag;
        bool isEnemy = HasTag(target, enemyTagValue);
        bool isBreakable = HasTag(target, breakableTagValue);
        bool isAllyMinion = target.GetComponentInParent<MinionCore>() != null && target != player;

        if (isEnemy)
        {
            return HasAvailableAttacker(minions, target, selectedRole)
                ? CommandPreviewType.Attack
                : CommandPreviewType.Invalid;
        }

        if (isBreakable)
        {
            return HasAvailableAttacker(minions, target, selectedRole, requireEnemy: false)
                ? CommandPreviewType.Attack
                : CommandPreviewType.Invalid;
        }

        if (isAllyMinion)
        {
            bool hasSupport = false;

            for (int i = 0; i < minions.Length; i++)
            {
                MinionCore minion = minions[i];
                if (minion == null || minion.RoleType != MinionRoleType.Support) continue;

                hasSupport = true;
                if (minion.CanAcceptSupportTarget(target)) return CommandPreviewType.Support;
            }

            return hasSupport ? CommandPreviewType.Invalid : CommandPreviewType.None;
        }

        // Non-environment objects under cursor that don't match valid command tags are invalid targets.
        if (IsEnvironmentCandidate(target))
        {
            return CommandPreviewType.None;
        }

        return CommandPreviewType.Invalid;
    }

    private void ApplyPreviewColor(Color color)
    {
        if (previewRenderers == null || previewRenderers.Length == 0)
        {
            return;
        }

        if (hasAppliedPreviewColor && ColorsAlmostEqual(lastAppliedPreviewColor, color))
        {
            return;
        }

        hasAppliedPreviewColor = true;
        lastAppliedPreviewColor = color;

        Color emission = color * Mathf.Max(0f, settings.previewEmissionIntensity);

        for (int i = 0; i < previewRenderers.Length; i++)
        {
            Renderer renderer = previewRenderers[i];
            if (renderer == null) continue;

            renderer.GetPropertyBlock(previewPropertyBlock);
            previewPropertyBlock.SetColor(BaseColorId, color);
            previewPropertyBlock.SetColor(ColorId, color);
            previewPropertyBlock.SetColor(EmissionColorId, emission);
            renderer.SetPropertyBlock(previewPropertyBlock);

            // Additional fallback for shaders that ignore property blocks for color channels.
            Material[] materials = renderer.materials;
            for (int m = 0; m < materials.Length; m++)
            {
                Material mat = materials[m];
                if (mat == null) continue;

                if (mat.HasProperty(BaseColorId))
                {
                    mat.SetColor(BaseColorId, color);
                }

                if (mat.HasProperty(ColorId))
                {
                    mat.SetColor(ColorId, color);
                }

                if (mat.HasProperty(EmissionColorId))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor(EmissionColorId, emission);
                }
            }
        }
    }

    private static bool ColorsAlmostEqual(Color a, Color b)
    {
        const float epsilon = 0.001f;
        return Mathf.Abs(a.r - b.r) < epsilon
               && Mathf.Abs(a.g - b.g) < epsilon
               && Mathf.Abs(a.b - b.b) < epsilon
               && Mathf.Abs(a.a - b.a) < epsilon;
    }

    private static bool HasTag(Transform target, string tag)
    {
        return FindTaggedTransform(target, tag) != null;
    }

    private static Transform FindTaggedTransform(Transform target, string tag)
    {
        if (!IsValidTarget(target)) return null;
        if (string.IsNullOrWhiteSpace(tag)) return null;

        Transform current = target;
        while (current != null)
        {
            if (current.CompareTag(tag))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private bool IsEnvironmentCandidate(Transform target)
    {
        if (!IsValidTarget(target)) return false;
        if (cursor == null) return false;
        return cursor.IsEnvironmentLayer(target.gameObject.layer);
    }

    public void SelectNextMinionType()
    {
        SelectMinionTypeOffset(1);
    }

    public void SelectPreviousMinionType()
    {
        SelectMinionTypeOffset(-1);
    }

    public MinionRoleType GetAdjacentRole(int offset)
    {
        int index = RoleToIndex(selectedRole);
        int wrapped = (index + offset) % 3;
        if (wrapped < 0) wrapped += 3;
        return IndexToRole(wrapped);
    }

    public int GetLiveCount(MinionRoleType role)
    {
        CountLiveRoles(out int melee, out int ranged, out int support);
        return GetLiveCountFromCounts(role, melee, ranged, support);
    }

    public int GetKnownTotalCount(MinionRoleType role)
    {
        SeedKnownCountsFromRunSetupData();
        CountLiveRoles(out int melee, out int ranged, out int support);
        UpdateKnownCounts(melee, ranged, support);

        return role switch
        {
            MinionRoleType.Melee => Mathf.Max(knownMeleeCount, melee),
            MinionRoleType.Ranged => Mathf.Max(knownRangedCount, ranged),
            MinionRoleType.Support => Mathf.Max(knownSupportCount, support),
            _ => 0
        };
    }

    private void SelectMinionTypeOffset(int offset)
    {
        if (offset == 0) return;
        if (!HasAnyLiveMinions())
        {
            EmptyMinionSelectionRequested?.Invoke(selectedRole);
            return;
        }

        MinionRoleType original = selectedRole;
        int direction = offset > 0 ? 1 : -1;
        int index = RoleToIndex(selectedRole);

        for (int step = 0; step < 3; step++)
        {
            index = (index + direction) % 3;
            if (index < 0) index += 3;

            MinionRoleType candidate = IndexToRole(index);
            if (GetLiveCount(candidate) <= 0) continue;

            selectedRole = candidate;
            RefreshSelectionState(forceNotify: selectedRole != original);
            return;
        }

        EmptyMinionSelectionRequested?.Invoke(selectedRole);
    }

    private bool EnsureSelectedRoleAvailable()
    {
        if (GetLiveCount(selectedRole) > 0) return true;

        MinionRoleType[] roles = { MinionRoleType.Melee, MinionRoleType.Ranged, MinionRoleType.Support };
        for (int i = 0; i < roles.Length; i++)
        {
            if (GetLiveCount(roles[i]) <= 0) continue;

            selectedRole = roles[i];
            RefreshSelectionState(forceNotify: true);
            return true;
        }

        return false;
    }

    private bool HasAnyLiveMinions()
    {
        CountLiveRoles(out int melee, out int ranged, out int support);
        return melee + ranged + support > 0;
    }

    private void RefreshSelectionState(bool forceNotify = false)
    {
        SeedKnownCountsFromRunSetupData();
        CountLiveRoles(out int melee, out int ranged, out int support);
        UpdateKnownCounts(melee, ranged, support);

        if (melee + ranged + support > 0 && GetLiveCountFromCounts(selectedRole, melee, ranged, support) <= 0)
        {
            if (melee > 0) selectedRole = MinionRoleType.Melee;
            else if (ranged > 0) selectedRole = MinionRoleType.Ranged;
            else selectedRole = MinionRoleType.Support;
        }

        bool changed = forceNotify
            || melee != lastLiveMelee
            || ranged != lastLiveRanged
            || support != lastLiveSupport
            || selectedRole != lastNotifiedRole;

        lastLiveMelee = melee;
        lastLiveRanged = ranged;
        lastLiveSupport = support;
        lastNotifiedRole = selectedRole;

        if (changed)
        {
            MinionSelectionChanged?.Invoke();
        }
    }

    private void UpdateKnownCounts(int melee, int ranged, int support)
    {
        knownMeleeCount = Mathf.Max(knownMeleeCount, melee);
        knownRangedCount = Mathf.Max(knownRangedCount, ranged);
        knownSupportCount = Mathf.Max(knownSupportCount, support);
    }

    private void SeedKnownCountsFromRunSetupData()
    {
        RunSetupData data = RunSetupData.Instance;
        if (data == null) return;

        SetKnownMinionCounts(data.typeA, data.typeB, data.typeC);
    }

    private void CountLiveRoles(out int melee, out int ranged, out int support)
    {
        melee = 0;
        ranged = 0;
        support = 0;

        MinionCore[] minions = ResolveControlledMinions();
        for (int i = 0; i < minions.Length; i++)
        {
            MinionCore minion = minions[i];
            if (!IsLiveMinion(minion)) continue;

            switch (minion.RoleType)
            {
                case MinionRoleType.Melee:
                    melee++;
                    break;
                case MinionRoleType.Ranged:
                    ranged++;
                    break;
                case MinionRoleType.Support:
                    support++;
                    break;
            }
        }
    }

    private static int GetLiveCountFromCounts(MinionRoleType role, int melee, int ranged, int support)
    {
        return role switch
        {
            MinionRoleType.Melee => melee,
            MinionRoleType.Ranged => ranged,
            MinionRoleType.Support => support,
            _ => 0
        };
    }

    private void OrderSelectedMinionToPosition(MinionCore[] minions, Vector3 targetPosition)
    {
        MinionCore chosen = PickNextSelectedMinion(minions);
        if (chosen == null)
        {
            EmptyMinionSelectionRequested?.Invoke(selectedRole);
            return;
        }

        RemoveFormationSlot(chosen);
        float resumeRange = settings != null ? settings.dismissResumeFollowRange : 10f;
        int liveRoleCount = GetLiveCount(selectedRole);
        int roleOrdinal = GetLiveRoleOrdinal(minions, chosen);
        Vector3 commandPosition = GetMoveToPositionSlot(targetPosition, roleOrdinal, liveRoleCount);
        chosen.SetMoveToPositionCommand(commandPosition, resumeRange);
        ResolveKelpAnimator()?.PlayOrderMinions();
        Log($"[MinionCommander] Order: {chosen.name} ({chosen.RoleType}) -> move to cursor slot {commandPosition}.");
    }

    private MinionCore PickNextSelectedMinion(MinionCore[] minions)
    {
        return PickNextRoleMinion(minions, selectedRole, null);
    }

    private MinionCore PickNextRoleMinion(MinionCore[] minions, MinionRoleType role, Func<MinionCore, bool> canUse)
    {
        if (minions == null || minions.Length == 0) return null;

        int startIndex = Mathf.Clamp(GetNextRoleCommandIndex(role), 0, minions.Length - 1);

        for (int step = 0; step < minions.Length; step++)
        {
            int index = (startIndex + step) % minions.Length;
            MinionCore minion = minions[index];
            if (!IsLiveMinion(minion)) continue;
            if (minion.RoleType != role) continue;
            if (canUse != null && !canUse(minion)) continue;

            SetNextRoleCommandIndex(role, (index + 1) % minions.Length);
            return minion;
        }

        return null;
    }

    private int GetNextRoleCommandIndex(MinionRoleType role)
    {
        return role switch
        {
            MinionRoleType.Melee => nextMeleeCommandIndex,
            MinionRoleType.Ranged => nextRangedCommandIndex,
            MinionRoleType.Support => nextSupportCommandIndex,
            _ => 0
        };
    }

    private void SetNextRoleCommandIndex(MinionRoleType role, int index)
    {
        switch (role)
        {
            case MinionRoleType.Melee:
                nextMeleeCommandIndex = index;
                break;
            case MinionRoleType.Ranged:
                nextRangedCommandIndex = index;
                break;
            case MinionRoleType.Support:
                nextSupportCommandIndex = index;
                break;
        }
    }

    private void ResetRoleCommandIndices()
    {
        nextMeleeCommandIndex = 0;
        nextRangedCommandIndex = 0;
        nextSupportCommandIndex = 0;
    }

    private static int GetLiveRoleOrdinal(MinionCore[] minions, MinionCore selected)
    {
        if (minions == null || selected == null) return 0;

        int ordinal = 0;
        for (int i = 0; i < minions.Length; i++)
        {
            MinionCore minion = minions[i];
            if (!IsLiveMinion(minion)) continue;
            if (minion.RoleType != selected.RoleType) continue;
            if (minion == selected) return ordinal;
            ordinal++;
        }

        return 0;
    }

    private static Vector3 GetMoveToPositionSlot(Vector3 targetPosition, int roleOrdinal, int liveRoleCount)
    {
        if (liveRoleCount <= 1 || roleOrdinal <= 0)
        {
            return targetPosition;
        }

        const float slotRadius = 0.85f;
        int ringIndex = roleOrdinal - 1;
        int ringCapacity = Mathf.Max(1, liveRoleCount - 1);
        float angle = (ringIndex / (float)ringCapacity) * Mathf.PI * 2f;
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * slotRadius;
        return targetPosition + offset;
    }

    private void RemoveFormationSlot(MinionCore minion)
    {
        if (minion == null) return;

        for (int i = activeFormationSlots.Count - 1; i >= 0; i--)
        {
            if (activeFormationSlots[i].Minion == minion)
            {
                activeFormationSlots.RemoveAt(i);
            }
        }
    }

    private static bool IsLiveMinion(MinionCore minion)
    {
        if (minion == null || !minion.gameObject.activeInHierarchy) return false;

        CombatantStats stats = minion.GetComponent<CombatantStats>();
        return stats == null || !stats.IsDead;
    }

    private static int RoleToIndex(MinionRoleType role)
    {
        return role switch
        {
            MinionRoleType.Melee => 0,
            MinionRoleType.Ranged => 1,
            MinionRoleType.Support => 2,
            _ => 0
        };
    }

    private static MinionRoleType IndexToRole(int index)
    {
        return index switch
        {
            0 => MinionRoleType.Melee,
            1 => MinionRoleType.Ranged,
            2 => MinionRoleType.Support,
            _ => MinionRoleType.Melee
        };
    }

    private MinionCore[] ResolveControlledMinions()
    {
        // If auto-find is on and we still have no minions, try a periodic refresh.
        if (autoFindMinionsIfEmpty && (controlledMinions == null || controlledMinions.Length == 0))
        {
            if (runtimeMinions.Count == 0 && Time.time >= nextAutoFindRefreshTime) RefreshAutoFoundMinions();
        }

        if (runtimeListDirty)
        {
            cachedRuntimeArray = runtimeMinions.Count > 0
                ? runtimeMinions.ToArray()
                : System.Array.Empty<MinionCore>();
            runtimeListDirty = false;
        }

        return cachedRuntimeArray;
    }

    private bool WasCommandPressedThisFrame()
    {
        if (commandAction != null) return commandAction.action.WasPressedThisFrame();
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    private bool WasCallPressedThisFrame()
    {
        if (callAction != null) return callAction.action.WasPressedThisFrame();
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
    }

    private bool WasDismissPressedThisFrame()
    {
        if (dismissAction != null) return dismissAction.action.WasPressedThisFrame();
        return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
    }

    private bool WasSelectPreviousPressedThisFrame()
    {
        if (selectPreviousMinionTypeAction != null)
        {
            return selectPreviousMinionTypeAction.action.WasPressedThisFrame();
        }

        bool keyboard = Keyboard.current != null
            && (Keyboard.current.qKey.wasPressedThisFrame || Keyboard.current.leftArrowKey.wasPressedThisFrame);
        bool gamepad = Gamepad.current != null && Gamepad.current.leftShoulder.wasPressedThisFrame;
        return keyboard || gamepad;
    }

    private bool WasSelectNextPressedThisFrame()
    {
        if (selectNextMinionTypeAction != null)
        {
            return selectNextMinionTypeAction.action.WasPressedThisFrame();
        }

        bool keyboard = Keyboard.current != null
            && (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame);
        bool gamepad = Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame;
        return keyboard || gamepad;
    }

    private KelpAnimatorBridge ResolveKelpAnimator()
    {
        if (kelpAnimator != null) return kelpAnimator;

        PlayerKelpVisualInstaller installer = GetComponentInParent<PlayerKelpVisualInstaller>();
        if (installer != null && installer.Bridge != null)
        {
            kelpAnimator = installer.Bridge;
        }

        if (kelpAnimator == null)
        {
            kelpAnimator = transform.root.GetComponentInChildren<KelpAnimatorBridge>(true);
        }

        return kelpAnimator;
    }

    // LOS check from the player to a potential command target.
    private bool HasLineOfSightToTarget(Transform target)
    {
        if (settings == null || !settings.requireLineOfSightForCursorTargets) return true;
        if (player == null || target == null) return true;

        float heightOffset = settings.cursorLOSHeightOffset;
        Vector3 start = player.position + Vector3.up * heightOffset;
        Vector3 end   = target.position + Vector3.up * heightOffset;
        Vector3 dir   = end - start;
        float   dist  = dir.magnitude;
        if (dist <= 0.0001f) return true;

        if (Physics.Raycast(start, dir / dist, out RaycastHit hit, dist, settings.cursorLOSBlockMask, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == target || hit.transform.IsChildOf(target);
        }

        return true;
    }

    private static bool IsValidTarget(Transform target)
    {
        return target != null && target.gameObject.activeInHierarchy;
    }

    private sealed class CommandPulseEffect : MonoBehaviour
    {
        private const int CircleSegments = 96;

        private Transform followTarget;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private Material material;
        private Light pulseLight;
        private Color color;
        private float startRadius;
        private float endRadius;
        private float duration;
        private float ringWidth;
        private float groundOffset;
        private float lightIntensity;
        private float startTime;

        public static void Spawn(
            Transform followTarget,
            Color color,
            float startRadius,
            float endRadius,
            float duration,
            float ringWidth,
            float groundOffset,
            float lightIntensity)
        {
            GameObject effectObject = new GameObject("Minion Command Pulse");
            effectObject.transform.position = followTarget != null ? followTarget.position : Vector3.zero;

            CommandPulseEffect effect = effectObject.AddComponent<CommandPulseEffect>();
            effect.Initialize(followTarget, color, startRadius, endRadius, duration, ringWidth, groundOffset, lightIntensity);
        }

        private void Initialize(
            Transform target,
            Color pulseColor,
            float initialRadius,
            float finalRadius,
            float lifetime,
            float width,
            float yOffset,
            float pointLightIntensity)
        {
            followTarget = target;
            color = pulseColor;
            startRadius = Mathf.Max(0.01f, initialRadius);
            endRadius = Mathf.Max(startRadius, finalRadius);
            duration = Mathf.Max(0.05f, lifetime);
            ringWidth = Mathf.Max(0.01f, width);
            groundOffset = yOffset;
            lightIntensity = Mathf.Max(0f, pointLightIntensity);
            startTime = Time.time;

            meshFilter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            mesh = new Mesh { name = "Minion Command Pulse Ring" };
            meshFilter.sharedMesh = mesh;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                material = new Material(shader);
                meshRenderer.sharedMaterial = material;
            }

            pulseLight = gameObject.AddComponent<Light>();
            pulseLight.type = LightType.Point;
            pulseLight.color = color;
            pulseLight.intensity = lightIntensity;
            pulseLight.range = Mathf.Max(endRadius * 0.55f, 1f);

            UpdateVisual(0f);
        }

        private void Update()
        {
            float t = Mathf.Clamp01((Time.time - startTime) / duration);
            UpdateVisual(t);

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }

            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        private void UpdateVisual(float t)
        {
            if (followTarget != null)
            {
                transform.position = followTarget.position;
            }

            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float radius = Mathf.Lerp(startRadius, endRadius, eased);
            float alpha = color.a * (1f - t);
            Color currentColor = new Color(color.r, color.g, color.b, alpha);

            if (material != null)
            {
                material.color = currentColor;
            }

            if (mesh != null)
            {
                BuildFlatRingMesh(radius, Mathf.Lerp(ringWidth, ringWidth * 0.25f, t));
            }

            if (pulseLight != null)
            {
                pulseLight.transform.position = transform.position + Vector3.up * 1.2f;
                pulseLight.intensity = lightIntensity * (1f - t);
            }
        }

        private void BuildFlatRingMesh(float radius, float width)
        {
            int vertexCount = CircleSegments * 2;
            int triangleIndexCount = CircleSegments * 6;
            Vector3[] vertices = new Vector3[vertexCount];
            Color[] colors = new Color[vertexCount];
            int[] triangles = new int[triangleIndexCount];

            float outerRadius = Mathf.Max(0.01f, radius);
            float innerRadius = Mathf.Max(0.01f, outerRadius - Mathf.Max(0.01f, width));
            Vector3 yOffset = Vector3.up * groundOffset;

            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = (i / (float)CircleSegments) * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                int vertexIndex = i * 2;
                vertices[vertexIndex] = yOffset + direction * innerRadius;
                vertices[vertexIndex + 1] = yOffset + direction * outerRadius;
                colors[vertexIndex] = color;
                colors[vertexIndex + 1] = color;

                int nextVertexIndex = ((i + 1) % CircleSegments) * 2;
                int triangleIndex = i * 6;
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = nextVertexIndex;
                triangles[triangleIndex + 2] = vertexIndex + 1;
                triangles[triangleIndex + 3] = vertexIndex + 1;
                triangles[triangleIndex + 4] = nextVertexIndex;
                triangles[triangleIndex + 5] = nextVertexIndex + 1;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }
    }
}
