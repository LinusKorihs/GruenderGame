using UnityEngine;
using UnityEngine.InputSystem;

public class GroundCursor : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private GroundCursorSettings settings;

    [Header("Refs")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform marker;

    [Header("Input")]
    [SerializeField] private InputActionReference cursorMoveAction; // Vector2 (right stick / optional keys)
    [SerializeField] private InputActionReference cursorExtraKeyAction; // Button (e.g. Shift)

    public Vector3 WorldPos { get; private set; }
    public Transform LockedTarget { get; private set; }
    public Transform AimAssistTarget => aimAssistTarget;
    public Transform Marker => marker;
    public bool IsLocked => LockedTarget != null;

    // Returns true when the layer belongs to ground or wall masks used by the cursor.
    public bool IsEnvironmentLayer(int layer)
    {
        if (settings == null) return false;

        int mask = 1 << layer;
        bool isGround = (settings.groundMask.value & mask) != 0;
        bool isWall = (settings.wallMask.value & mask) != 0;
        return isGround || isWall;
    }

    public bool LockWithCursor => settings != null && settings.lockWithCursor;
    public float LockRangeWithCursor => settings != null ? settings.lockRangeWithCursor : 0f;

    private Vector3 externalMoveDir = Vector3.zero;
    private Vector3 smoothedDir = Vector3.forward;
    private bool smoothedDirInit;
    private bool blending;
    private Vector3 blendTargetPos;
    private float smoothedWallHeight01 = 0f;
    private Transform aimAssistTarget;
    private float aimCooldownTimer;

    public void SetMoveDirection(Vector3 worldMoveDir) => externalMoveDir = worldMoveDir;
    public bool IsAimingWithMouseKeyboard { get; private set; }
    public bool IsAimingWithGamepad { get; private set; }

    private void Awake()
    {
        ResolveInputActions();
    }

    private void ResolveInputActions()
    {
        PlayerInput playerInput = GetComponentInParent<PlayerInput>();

        cursorMoveAction = PlayerInputActionResolver.Resolve(cursorMoveAction, playerInput, "Camera", "CursorMove", this);
        cursorExtraKeyAction = PlayerInputActionResolver.Resolve(cursorExtraKeyAction, playerInput, "Camera", "CursorExtraKey", this);
    }

    private void OnEnable()
    {
        if (cursorMoveAction) cursorMoveAction.action.Enable();
        if (cursorExtraKeyAction) cursorExtraKeyAction.action.Enable();
    }

    private void OnDisable()
    {
        if (cursorMoveAction) cursorMoveAction.action.Disable();
        if (cursorExtraKeyAction) cursorExtraKeyAction.action.Disable();
    }

    private void Start()
    {
        if (marker) marker.localScale = settings.markerScale;
        smoothedDir = GetDesiredDirFlat();
        smoothedDirInit = true;

        Vector3 dir = GetSmoothedDirFlat();
        Vector3 desiredFlat = player.position + dir * settings.baseDistance;
        desiredFlat.y = 0f;

        desiredFlat = TryGetWallPoint(desiredFlat, out Vector3 wallPos, out Vector3 wallNormal) && settings.wallClimbEnabled ? wallPos + wallNormal * settings.wallSurfaceOffset : SampleGroundPoint(desiredFlat) + Vector3.up * settings.heightOffset;

        WorldPos = SampleGroundPoint(desiredFlat) + Vector3.up * settings.heightOffset;
        ApplyVisuals(WorldPos, Vector3.up);
    }

    private void LateUpdate()
    {
        if (blending)
        {
            WorldPos = Vector3.MoveTowards(WorldPos, blendTargetPos, settings.lockMoveSpeed * Time.deltaTime);
            ApplyVisuals(WorldPos, Vector3.up);

            if ((WorldPos - blendTargetPos).sqrMagnitude < 0.0001f) blending = false;

            return;
        }

        if (aimCooldownTimer > 0f) aimCooldownTimer -= Time.deltaTime;

        if (settings == null || !settings.cursorEnabled)
        {
            SetVisualsActive(false);
            return; // IMPORTANT: do NOT reset/unlock here
        }

        SetVisualsActive(true);

        // LOCKED MODE (Pikmin 2: cursor sticks to target; unlock if too far)
        if (IsLocked)
        {
            if (LockedTarget == null || !LockedTarget.gameObject.activeInHierarchy)
            {
                UnlockInternal(resetToPlayer: true);
                return;
            }

            if (Vector3.Distance(player.position, LockedTarget.position) > settings.lockRange)
            {
                UnlockInternal(resetToPlayer: true);
                return;
            }

            Vector3 lockedPos = SampleGroundPoint(LockedTarget.position) + Vector3.up * settings.heightOffset;

            if (settings.smoothLockTransition) WorldPos = Vector3.MoveTowards(WorldPos, lockedPos, settings.lockMoveSpeed * Time.deltaTime);
            else WorldPos = lockedPos;
            ApplyVisuals(WorldPos, Vector3.up);
            return;
        }

        // FREE MODE: fixed distance X in movement direction; cursor moves with player automatically
        IsAimingWithMouseKeyboard = false;
        IsAimingWithGamepad = false;

        Vector3 dir = GetSmoothedDirFlat();
        float d = settings.baseDistance; // fixed distance (no min/max ring)
        Vector3 desiredFlat = player.position + dir * d;
        desiredFlat.y = 0f;

        bool wallHit = TryGetWallPoint(desiredFlat, out Vector3 wallPos, out Vector3 wallNormal);

        Vector3 desired;
        Vector3 surfaceNormal;

        if (settings.wallClimbEnabled && wallHit)
        {
            // On wall: keep wall Y, don’t sample ground.
            desired = wallPos + wallNormal * settings.wallSurfaceOffset;
            surfaceNormal = wallNormal;
        }
        else
        {
            // On ground
            desired = SampleGroundPoint(desiredFlat) + Vector3.up * settings.heightOffset;
            surfaceNormal = Vector3.up;
        }

        // Aim Assist may override cursor position entirely (so baseDistance can't pull it behind the enemy)
        bool aimIsHolding;
        desired = ApplyAutoSnapSimple(desired, out aimIsHolding);

        // If aim assist is holding, the cursor must not be overwritten by baseDistance logic.
        WorldPos = desired;

        ApplyVisuals(WorldPos, surfaceNormal);
    }
    private Vector3 GetBasePointFlat()
    {
        Vector3 dir = GetSmoothedDirFlat();
        float d = Mathf.Clamp(settings.baseDistance, settings.minDistance, settings.maxDistance);
        Vector3 flat = player.position + dir * d;
        flat.y = 0f; // flat target (we sample ground after)
        return flat;
    }

    private Vector3 GetDesiredDirFlat()
    {
        Vector3 inputDir = externalMoveDir;
        inputDir.y = 0f;

        if (inputDir.sqrMagnitude < 0.0001f) return smoothedDir;
        return inputDir.normalized;
    }

    private Vector3 GetSmoothedDirFlat()
    {
        Vector3 desired = GetDesiredDirFlat();

        if (!smoothedDirInit)
        {
            smoothedDir = desired;
            smoothedDirInit = true;
            return smoothedDir;
        }

        // Smooth direction so cursor doesn't "teleport" when player rotates quickly
        float t = 1f - Mathf.Exp(-(settings != null ? settings.dirSmoothing : 16f) * Time.deltaTime);
        smoothedDir = Vector3.Slerp(smoothedDir, desired, t);
        smoothedDir.y = 0f;

        if (smoothedDir.sqrMagnitude < 0.0001f) smoothedDir = desired;
        return smoothedDir.normalized;
    }

    private Vector3 SampleGroundPoint(Vector3 nearPos)
    {
        Vector3 origin = nearPos + Vector3.up * settings.groundRayStartHeight;
        if (Physics.Raycast(origin, Vector3.down, out var hit, settings.groundRayLength, settings.groundMask, QueryTriggerInteraction.Ignore)) return hit.point;

        // fallback: keep Y as is
        return nearPos;
    }

    private void UnlockInternal(bool resetToPlayer)
    {
        LockedTarget = null;
        SetAimAssistTarget(null);

        if (resetToPlayer)
        {
            Vector3 baseFlat = GetBasePointFlat();
            Vector3 backPos = SampleGroundPoint(baseFlat) + Vector3.up * settings.heightOffset;

            if (settings.smoothLockTransition)
            {
                blending = true;
                blendTargetPos = backPos;
            }
            else WorldPos = backPos;

            smoothedDir = GetDesiredDirFlat();
            smoothedDirInit = true;
            ApplyVisuals(WorldPos, Vector3.up);
        }
    }

    private void ApplyVisuals(Vector3 pos, Vector3 surfaceNormal)
    {
        if (!marker) return;

        marker.position = pos;
        marker.localScale = settings.markerScale;

        if (settings.rotateMarkerToSurface)
        {
            marker.rotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal.normalized);
        }
    }

    private void SetVisualsActive(bool active)
    {
        if (marker && marker.gameObject.activeSelf != active) marker.gameObject.SetActive(active);
    }

    public void LockTo(Transform target)
    {
        LockedTarget = target;
        SetAimAssistTarget(target);

        if (LockedTarget)
        {
            Vector3 tPos = SampleGroundPoint(LockedTarget.position) + Vector3.up * settings.heightOffset;
            WorldPos = tPos;
            smoothedDir = GetDesiredDirFlat();
            smoothedDirInit = true;
            ApplyVisuals(WorldPos, Vector3.up);
        }
    }

    public void ForceUnlockToPlayer()
    {
        UnlockInternal(resetToPlayer: true);
    }

    private bool TryGetWallPoint(Vector3 desiredFlat, out Vector3 hitPos, out Vector3 hitNormal)
    {
        hitPos = desiredFlat;
        hitNormal = Vector3.up;

        if (settings.wallMask == 0) return false;

        QueryTriggerInteraction qti = settings.wallHitTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

        // Stable cast height
        Vector3 playerGround = SampleGroundPoint(player.position);
        float h = playerGround.y + 0.6f;

        Vector3 origin = new Vector3(player.position.x, h, player.position.z);
        Vector3 target = new Vector3(desiredFlat.x, h, desiredFlat.z);

        Vector3 dir = target - origin;
        float dist = dir.magnitude;
        if (dist < 0.001f) return false;
        dir /= dist;

        RaycastHit hit;
        float r = Mathf.Max(0f, settings.wallProbeRadius);

        bool hitSomething = (r > 0.001f) ? Physics.SphereCast(origin, r, dir, out hit, dist, settings.wallMask, qti) : Physics.Raycast(origin, dir, out hit, dist, settings.wallMask, qti);

        Debug.DrawLine(origin, target, hitSomething ? Color.red : Color.green, 0.1f);
        if (!hitSomething) return false;

        hitNormal = hit.normal;

        // Compute height on wall based on player distance (near = eye height, far = near ground)
        float hitDist = hit.distance; // along the ray

        // Convert distance to 0..1 where 1 = very close, 0 = far
        float t = Mathf.InverseLerp(settings.wallFarDistance, settings.wallNearDistance, hitDist);
        t = Mathf.Clamp01(t);

        // Smooth it a bit so it doesn't jitter when strafing
        float s = 1f - Mathf.Exp(-settings.wallHeightSmoothing * Time.deltaTime);
        smoothedWallHeight01 = Mathf.Lerp(smoothedWallHeight01, t, s);

        // Compute final height on wall (world Y)
        float minY = playerGround.y + settings.wallMinHeight;
        float eyeY = player.position.y + settings.wallEyeHeight; // relative to player body
        float wallY = Mathf.Lerp(minY, eyeY, smoothedWallHeight01);

        // Base point slightly in front of the wall
        Vector3 p = hit.point + hit.normal * (settings.wallPadding + r);

        // Override Y with our computed wall height
        p.y = wallY;

        hitPos = p;
        return true;
    }

    private Vector3 ApplyAutoSnapSimple(Vector3 desiredWorldPos, out bool isHoldingTarget)
    {
        isHoldingTarget = false;

        if (!settings.aimAssistEnabled)
        {
            SetAimAssistTarget(null);
            return desiredWorldPos;
        }

        if (aimCooldownTimer > 0f) aimCooldownTimer -= Time.deltaTime;
        Transform rayEnemy = GetForwardRayEnemy();

        // Hold: If there is a target, keep it while inside EXIT radius 
        if (aimAssistTarget != null)
        {
            Vector3 snapPos = GetEnemyGroundSnapPos(aimAssistTarget);

            bool rayStillHitsSame = (rayEnemy == aimAssistTarget);
            float dist = Vector3.Distance(desiredWorldPos, snapPos);

            // hold if ray hits same enemy, OR still inside exit radius
            if (rayStillHitsSame || dist <= settings.aimExitRadius)
            {
                isHoldingTarget = true;
                return snapPos;
            }

            SetAimAssistTarget(null);
            aimCooldownTimer = settings.aimCooldown;
            return desiredWorldPos;
        }

        // Accquire: cooldown gate
        if (aimCooldownTimer > 0f) return desiredWorldPos;

        if (rayEnemy != null)
        {
            SetAimAssistTarget(rayEnemy);
            isHoldingTarget = true;
            return GetEnemyGroundSnapPos(rayEnemy);
        }

        // Acquire via forward path sweep: catch enemies at any range between player and cursor,
        // even when they are closer than baseDistance (independent of aimHoldWhileRayHits).
        Transform pathEnemy = GetForwardPathEnemy(desiredWorldPos);
        if (pathEnemy != null)
        {
            SetAimAssistTarget(pathEnemy);
            isHoldingTarget = true;
            return GetEnemyGroundSnapPos(pathEnemy);
        }

        // Find best enemy within ENTER radius (around cursor)
        Collider[] hits = Physics.OverlapSphere(desiredWorldPos, settings.aimEnterRadius, settings.lockableMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestDistSq = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;

            // Use the actual snap position for distance evaluation (stable)
            Vector3 snapPos = GetEnemyGroundSnapPos(t);
            float dSq = (snapPos - desiredWorldPos).sqrMagnitude;

            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = t;
            }
        }

        if (best == null) return desiredWorldPos;

        // acquire -> highlight + hard snap
        SetAimAssistTarget(best);
        isHoldingTarget = true;
        return GetEnemyGroundSnapPos(best);
    }

    private void SetAimAssistTarget(Transform t)
    {
        if (aimAssistTarget == t) return;

        // turn off old
        if (aimAssistTarget != null)
        {
            var oldH = FindCursorHighlight(aimAssistTarget);
            oldH?.SetHighlighted(false);
        }

        aimAssistTarget = t;

        // turn on new
        if (aimAssistTarget != null && settings.highlightEnabled)
        {
            var newH = FindCursorHighlight(aimAssistTarget);
            newH?.SetHighlighted(true);
        }
    }

    private static ICursorHighlight FindCursorHighlight(Transform target)
    {
        if (!target) return null;

        ICursorHighlight highlight = target.GetComponent<ICursorHighlight>();
        if (highlight != null) return highlight;

        highlight = target.GetComponentInParent<ICursorHighlight>();
        if (highlight != null) return highlight;

        return target.GetComponentInChildren<ICursorHighlight>(true);
    }

    private Vector3 GetEnemyGroundSnapPos(Transform t)
    {
        if (!t) return WorldPos;

        var col = t.GetComponentInChildren<Collider>();
        Vector3 xzCenter = t.position;

        if (col != null)
        {
            Vector3 c = col.bounds.center;
            xzCenter = new Vector3(c.x, col.bounds.max.y + 0.5f, c.z); // start above enemy
        }
        else
        {
            xzCenter = t.position + Vector3.up * 2f;
        }

        // Ray down onto ground
        if (Physics.Raycast(xzCenter, Vector3.down, out var hit, settings.groundRayLength + 5f, settings.groundMask, QueryTriggerInteraction.Ignore)) return hit.point + Vector3.up * settings.heightOffset;

        // fallback
        return SampleGroundPoint(t.position) + Vector3.up * settings.heightOffset;
    }

    // Sweeps from player toward the cursor position to find a lockable enemy at any distance
    // up to baseDistance. Used for acquisition so nearby enemies (closer than baseDistance)
    // are always reachable with the cursor.
    private Transform GetForwardPathEnemy(Vector3 cursorWorldPos)
    {
        if (settings.lockableMask == 0) return null;

        Vector3 playerGround = SampleGroundPoint(player.position);
        float h = playerGround.y + 0.6f;

        Vector3 origin = new Vector3(player.position.x, h, player.position.z);
        Vector3 flat   = new Vector3(cursorWorldPos.x, h, cursorWorldPos.z);
        Vector3 dir    = flat - origin;
        float dist     = dir.magnitude;
        if (dist < 0.001f) return null;
        dir /= dist;

        RaycastHit hit;
        float r = Mathf.Max(0f, settings.aimHoldRayRadius);

        bool hitSomething = (r > 0.001f)
            ? Physics.SphereCast(origin, r, dir, out hit, dist, settings.lockableMask, QueryTriggerInteraction.Ignore)
            : Physics.Raycast(origin, dir, out hit, dist, settings.lockableMask, QueryTriggerInteraction.Ignore);

        return hitSomething ? hit.transform : null;
    }

    private Transform GetForwardRayEnemy()
    {
        if (!settings.aimHoldWhileRayHits) return null;
        if (settings.lockableMask == 0) return null;

        Vector3 playerGround = SampleGroundPoint(player.position);
        float h = playerGround.y + 0.6f;

        Vector3 origin = new Vector3(player.position.x, h, player.position.z);
        Vector3 dir = GetSmoothedDirFlat();

        float len = settings.baseDistance + settings.aimHoldRayExtraLength;

        RaycastHit hit;
        float r = Mathf.Max(0f, settings.aimHoldRayRadius);

        bool hitSomething = (r > 0.001f) ? Physics.SphereCast(origin, r, dir, out hit, len, settings.lockableMask, QueryTriggerInteraction.Ignore) : Physics.Raycast(origin, dir, out hit, len, settings.lockableMask, QueryTriggerInteraction.Ignore);

        return hitSomething ? hit.transform : null;
    }
}
