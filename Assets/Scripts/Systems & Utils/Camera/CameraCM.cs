using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraCM : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private CameraCMSettings settings;
    [SerializeField] private CameraStage startingStage = CameraStage.Default;

    [Header("Cinemachine")]
    [SerializeField] public CinemachineOrbitalFollow orbital;

    [Header("Input Actions")]
    [SerializeField] public InputActionReference toggleAction;
    [SerializeField] public InputActionReference zoomAction;

    [Header("Cinemachine (optional but recommended)")]
    [SerializeField] private CinemachineCamera cmCamera;

    [Header("Camera Actions")]
    [SerializeField] public InputActionReference lookAction;
    [SerializeField] public InputActionReference lockOnAction;

    [Header("Debug")]
    [SerializeField] private bool enableLogs;

    private bool isLockedOn;
    private GroundCursor cursor;
    private Transform lockTarget;
    private Transform defaultLookAt;

    private CameraStage currentStage;
    private float targetRadius;
    private float targetVertical;
    private float targetFieldOfView;
    private Vector3 targetOffset;
    private float baseStageRadius;
    private Transform lockFramingTarget;
    private float lastStageRadius;
    private float lastStageVertical;
    private float lastStageFieldOfView;
    private Vector3 lastStageTargetOffset;

    private float TransitionSpeed => settings.transitionSpeed;
    private float MinRadius => settings.minRadius;
    private float MaxRadius => settings.maxRadius;
    private bool LiveStageTuning => settings.liveStageTuning;
    private float InvertSignY => settings.invertY ? 1f : -1f;
    private string[] LockOnTags => settings.lockOnTags;
    private float LockOnMaxDistance => settings.lockOnMaxDistance;
    private float GamepadSensX => settings.gamepadSensitivityX;
    private float GamepadSensY => settings.gamepadSensitivityY;
    private float MouseSensX => settings.mouseSensitivityX;
    private float MouseSensY => settings.mouseSensitivityY;
    private bool MouseScaleWithDeltaTime => settings.mouseScaleWithDeltaTime;
    private float LockOnTargetWeight => settings.lockOnTargetWeight;
    private Vector3 LockOnFramingOffset => settings.lockOnFramingOffset;
    private float LockOnHeightOffset => settings.lockOnHeightOffset;
    private float LockOnLookTargetSmooth => settings.lockOnLookTargetSmooth;
    private float LockOnRadiusPerMeter => settings.lockOnRadiusPerMeter;
    private float LockOnMaxExtraRadius => settings.lockOnMaxExtraRadius;
    private float LockOnVertical => settings.lockOnVertical;
    private float LockOnVerticalSmooth => settings.lockOnVerticalSmooth;
    private float LockOnHorizontalSmooth => settings.lockOnHorizontalSmooth;
    private float LockOnMinDistance => settings.lockOnMinDistance;

    public CameraCMSettings Settings => settings;

    private void Log(string msg) { if (enableLogs) Debug.Log(msg); }

    private void Awake()
    {
        ResolveInputActions();
    }

    private void ResolveInputActions()
    {
        PlayerInput playerInput = GetComponentInParent<PlayerInput>();
        if (playerInput == null) playerInput = FindFirstObjectByType<PlayerInput>();

        toggleAction = PlayerInputActionResolver.Resolve(toggleAction, playerInput, "Camera", "Toggle", this);
        zoomAction = PlayerInputActionResolver.Resolve(zoomAction, playerInput, "Camera", "Zoom", this);
        lookAction = PlayerInputActionResolver.Resolve(lookAction, playerInput, "Camera", "Look", this);
        lockOnAction = PlayerInputActionResolver.Resolve(lockOnAction, playerInput, "Camera", "LockOn", this);
    }

    private void OnEnable()
    {
        if (toggleAction) toggleAction.action.Enable();
        if (zoomAction) zoomAction.action.Enable();
        if (lookAction) lookAction.action.Enable();
        if (lockOnAction) lockOnAction.action.Enable();

        if (toggleAction) toggleAction.action.performed += OnToggle;
        if (lockOnAction) lockOnAction.action.performed += OnLockOn;
    }

    private void OnDisable()
    {
        if (toggleAction) toggleAction.action.performed -= OnToggle;
        if (lockOnAction) lockOnAction.action.performed -= OnLockOn;

        if (toggleAction) toggleAction.action.Disable();
        if (zoomAction) zoomAction.action.Disable();
        if (lookAction) lookAction.action.Disable();
        if (lockOnAction) lockOnAction.action.Disable();
    }

    private void Start()
    {
        if (!cmCamera) cmCamera = GetComponent<CinemachineCamera>();
        if (!orbital) orbital = GetComponent<CinemachineOrbitalFollow>();
        if (cmCamera) defaultLookAt = cmCamera.LookAt;

        EnsureWorldSpaceOrbit();
        CreateLockFramingTarget();
        cursor = FindFirstObjectByType<GroundCursor>();

        currentStage = startingStage;
        ApplyStage(currentStage, instant: true);
    }

    private void Update()
    {
        if (!settings || !orbital) return;

        if (ShouldBreakLockOn())
        {
            ClearLockOn();
            return;
        }

        HandleZoom();
        HandleLookInput();

        if (LiveStageTuning && !isLockedOn) RefreshCurrentStageTargetsFromSettings();
        if (isLockedOn && lockTarget != null) UpdateLockOnFraming();

        ApplyCameraTargets();
    }

    private void HandleZoom()
    {
        if (!zoomAction || isLockedOn) return;

        float z = zoomAction.action.ReadValue<float>();
        if (Mathf.Abs(z) <= 0.001f) return;

        bool usingMouseWheel = Mouse.current != null && Mathf.Abs(Mouse.current.scroll.ReadValue().y) > 0.01f;
        float speed = usingMouseWheel ? settings.mouseZoomSpeed : settings.gamepadZoomSpeed;
        float dt = usingMouseWheel ? 1f : Time.deltaTime;

        targetRadius = Mathf.Clamp(targetRadius - z * speed * dt, MinRadius, MaxRadius);
    }

    private void HandleLookInput()
    {
        if (isLockedOn || !lookAction) return;

        Vector2 look = lookAction.action.ReadValue<Vector2>();

        if (cursor != null)
        {
            bool mouseMoving = Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f;
            bool stickMoving = Gamepad.current != null && Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0.01f;

            if ((mouseMoving && cursor.IsAimingWithMouseKeyboard) || (stickMoving && cursor.IsAimingWithGamepad))
                return;
        }

        if (look.sqrMagnitude <= 0.0001f) return;

        bool usingMouse = Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f;
        float sensX = usingMouse ? MouseSensX : GamepadSensX;
        float sensY = usingMouse ? MouseSensY : GamepadSensY;

        float dt = 1f;
        if (!usingMouse || MouseScaleWithDeltaTime) dt = Time.deltaTime;

        var h = orbital.HorizontalAxis;
        h.Value += look.x * sensX * dt;
        h.Value = ClampHorizontalIfNeeded(h.Value);
        orbital.HorizontalAxis = h;

        CameraStageSettings stageSettings = settings.GetStage(currentStage);
        var vAxis = orbital.VerticalAxis;
        vAxis.Value = Mathf.Clamp(vAxis.Value + look.y * InvertSignY * sensY * dt, GetStageMinVertical(stageSettings), GetStageMaxVertical(stageSettings));
        orbital.VerticalAxis = vAxis;

        targetVertical = vAxis.Value;
    }

    private void ApplyCameraTargets()
    {
        orbital.Radius = Mathf.Lerp(orbital.Radius, targetRadius, Time.deltaTime * settings.zoomSmoothing);

        orbital.TargetOffset = Vector3.Lerp(orbital.TargetOffset, targetOffset, Time.deltaTime * TransitionSpeed);

        var v = orbital.VerticalAxis;
        v.Value = Mathf.Lerp(v.Value, targetVertical, Time.deltaTime * TransitionSpeed);
        orbital.VerticalAxis = v;

        if (cmCamera)
            cmCamera.Lens.FieldOfView = Mathf.Lerp(cmCamera.Lens.FieldOfView, targetFieldOfView, Time.deltaTime * TransitionSpeed);
    }

    private void OnToggle(InputAction.CallbackContext _)
    {
        ApplyStage(GetNextStage(currentStage), instant: false);
        Log($"Camera stage toggled. Now in {settings.GetStage(currentStage).displayName}.");
    }

    public void ApplyStartingStageNow()
    {
        EnsureReferences();
        EnsureWorldSpaceOrbit();
        ApplyStage(startingStage, instant: true);
    }

    public void ApplyWideStageNow()
    {
        ApplyStageNow(CameraStage.Wide);
    }

    public void ApplyDefaultStageNow()
    {
        ApplyStageNow(CameraStage.Default);
    }

    public void ApplyCloseStageNow()
    {
        ApplyStageNow(CameraStage.Close);
    }

    public void ApplyStageNow(CameraStage stage)
    {
        EnsureReferences();
        EnsureWorldSpaceOrbit();
        ApplyStage(stage, instant: true);
    }

    private CameraStage GetNextStage(CameraStage stage)
    {
        return stage switch
        {
            CameraStage.Wide => CameraStage.Default,
            CameraStage.Default => CameraStage.Close,
            CameraStage.Close => CameraStage.Wide,
            _ => CameraStage.Default
        };
    }

    private void ApplyStage(CameraStage stage, bool instant)
    {
        if (!settings || !orbital)
        {
            Debug.LogError("CameraCM is missing settings or CinemachineOrbitalFollow.");
            return;
        }

        currentStage = stage;
        CameraStageSettings stageSettings = settings.GetStage(stage);

        baseStageRadius = Mathf.Clamp(stageSettings.radius, MinRadius, MaxRadius);
        targetRadius = baseStageRadius;
        targetVertical = ClampStageVertical(stageSettings, stageSettings.vertical);
        targetFieldOfView = Mathf.Clamp(stageSettings.fieldOfView, 1f, 179f);
        targetOffset = GetStageTargetOffset(stageSettings);
        CacheStageTargets(targetRadius, targetVertical, targetFieldOfView, targetOffset);
        ApplyHorizontalLimitForStage(stageSettings);

        if (instant)
        {
            orbital.Radius = targetRadius;
            orbital.TargetOffset = targetOffset;

            var v = orbital.VerticalAxis;
            v.Value = targetVertical;
            orbital.VerticalAxis = v;

            if (cmCamera) cmCamera.Lens.FieldOfView = targetFieldOfView;
        }

        Log($"ApplyStage -> {stageSettings.displayName} radius:{targetRadius}, vertical:{targetVertical}, fov:{targetFieldOfView}, offset:{targetOffset}");
    }

    private void OnLockOn(InputAction.CallbackContext _)
    {
        if (!cmCamera)
        {
            Debug.LogWarning("[CameraCM] LockOn: No CinemachineCamera assigned/found.");
            return;
        }

        // toggle off
        if (isLockedOn)
        {
            ClearLockOn();
            return;
        }

        // toggle on
        Transform best = FindClosestLockTarget();
        if (!best)
        {
            if (settings.debugEnabled) Debug.Log("[CameraCM] LockOn: no target found.");
            return;
        }

        lockTarget = best;
        isLockedOn = true;

        UpdateLockOnFraming();
        cmCamera.LookAt = lockFramingTarget;

        if (cursor) cursor.LockTo(lockTarget);

        if (settings.debugEnabled) Debug.Log($"[CameraCM] LockOn -> {lockTarget.name}");
        
    }

    private void ClearLockOn()
    {
        isLockedOn = false;
        lockTarget = null;

        CameraStageSettings stageSettings = settings.GetStage(currentStage);
        targetRadius = Mathf.Clamp(stageSettings.radius, MinRadius, MaxRadius);
        targetVertical = ClampStageVertical(stageSettings, stageSettings.vertical);
        targetFieldOfView = Mathf.Clamp(stageSettings.fieldOfView, 1f, 179f);
        targetOffset = GetStageTargetOffset(stageSettings);
        CacheStageTargets(targetRadius, targetVertical, targetFieldOfView, targetOffset);

        if (cmCamera) cmCamera.LookAt = defaultLookAt;

        if (settings.debugEnabled) Debug.Log("[CameraCM] LockOn cleared.");
        if (cursor) cursor.ForceUnlockToPlayer();
    }

    private Transform FindClosestLockTarget()
    {
        Vector3 originPos = GetPlayerPosition();

        bool useCursorOrigin = cursor != null && cursor.LockWithCursor;
        if (useCursorOrigin) originPos = cursor.WorldPos;

        float maxDist = LockOnMaxDistance;
        if (useCursorOrigin && cursor.LockRangeWithCursor > 0f)
            maxDist = Mathf.Min(maxDist, cursor.LockRangeWithCursor);

        float bestDistSq = maxDist * maxDist;
        Transform best = null;

        if (LockOnTags == null || LockOnTags.Length == 0) return null;

        for (int t = 0; t < LockOnTags.Length; t++)
        {
            string tag = LockOnTags[t];
            if (string.IsNullOrWhiteSpace(tag)) continue;

            GameObject[] objs;
            try
            {
                objs = GameObject.FindGameObjectsWithTag(tag);
            }
            catch
            {
                continue;
            }

            for (int i = 0; i < objs.Length; i++)
            {
                var go = objs[i];
                if (!go) continue;

                float dSq = (go.transform.position - originPos).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = go.transform;
                }
            }
        }
        return best;
    }

    private bool ShouldBreakLockOn()
    {
        if (!isLockedOn) return false;
        if (lockTarget == null || !lockTarget.gameObject.activeInHierarchy) return true;

        // If cursor-based lock was lost, camera lock should also break
        if (cursor != null && !cursor.IsLocked) return true;

        if (cursor != null && cursor.IsLocked && cursor.LockedTarget != lockTarget) return true;

        float distSq = (lockTarget.position - GetPlayerPosition()).sqrMagnitude;
        return distSq > LockOnMaxDistance * LockOnMaxDistance;
    }

    private void CreateLockFramingTarget()
    {
        if (lockFramingTarget != null) return;

        GameObject go = new GameObject("LockOnFramingTarget");
        go.hideFlags = HideFlags.HideInHierarchy;
        lockFramingTarget = go.transform;

        Vector3 startPos = GetPlayerPosition() + Vector3.up * Mathf.Max(LockOnHeightOffset, LockOnFramingOffset.y);
        lockFramingTarget.position = startPos;
    }

    private void UpdateLockOnFraming()
    {
        if (!cmCamera || !lockTarget || !lockFramingTarget) return;

        Vector3 playerPos = GetPlayerPosition();
        Vector3 enemyPos = lockTarget.position;

        Vector3 flatToEnemy = enemyPos - playerPos;
        flatToEnemy.y = 0f;
        if (flatToEnemy.sqrMagnitude < 0.0001f) return;
        flatToEnemy.Normalize();

        float desiredYaw = Mathf.Atan2(flatToEnemy.x, flatToEnemy.z) * Mathf.Rad2Deg;
        float horizontalT = 1f - Mathf.Exp(-LockOnHorizontalSmooth * Time.deltaTime);

        var h = orbital.HorizontalAxis;
        h.Value = Mathf.LerpAngle(h.Value, desiredYaw, horizontalT);
        orbital.HorizontalAxis = h;

        Vector3 side = Vector3.Cross(Vector3.up, flatToEnemy);
        if (side.sqrMagnitude > 0.0001f) side.Normalize();

        Vector3 localOffset = LockOnFramingOffset;
        Vector3 framedPos = Vector3.Lerp(playerPos, enemyPos, Mathf.Clamp01(LockOnTargetWeight));
        framedPos += side * localOffset.x;
        framedPos += Vector3.up * localOffset.y;
        framedPos += flatToEnemy * localOffset.z;

        float lookT = 1f - Mathf.Exp(-LockOnLookTargetSmooth * Time.deltaTime);
        lockFramingTarget.position = Vector3.Lerp(lockFramingTarget.position, framedPos, lookT);

        targetVertical = Mathf.Lerp(targetVertical, LockOnVertical, Time.deltaTime * LockOnVerticalSmooth);

        float distance = Vector3.Distance(playerPos, enemyPos);
        float extraRadius = Mathf.Min(distance * LockOnRadiusPerMeter, LockOnMaxExtraRadius);

        float desiredRadius = Mathf.Max(LockOnMinDistance, baseStageRadius + extraRadius);
        targetRadius = Mathf.Clamp(desiredRadius, MinRadius, MaxRadius);
    }

    private Vector3 GetPlayerPosition()
    {
        if (cmCamera && cmCamera.Follow) return cmCamera.Follow.position;
        return transform.position;
    }

    private void RefreshCurrentStageTargetsFromSettings()
    {
        CameraStageSettings stageSettings = settings.GetStage(currentStage);
        float stageRadius = Mathf.Clamp(stageSettings.radius, MinRadius, MaxRadius);
        float stageVertical = ClampStageVertical(stageSettings, stageSettings.vertical);
        float stageFieldOfView = Mathf.Clamp(stageSettings.fieldOfView, 1f, 179f);
        Vector3 stageTargetOffset = GetStageTargetOffset(stageSettings);

        if (!Mathf.Approximately(stageRadius, lastStageRadius))
        {
            baseStageRadius = stageRadius;
            targetRadius = stageRadius;
            lastStageRadius = stageRadius;
        }

        if (!Mathf.Approximately(stageVertical, lastStageVertical))
        {
            targetVertical = stageVertical;
            lastStageVertical = stageVertical;
        }

        if (!Mathf.Approximately(stageFieldOfView, lastStageFieldOfView))
        {
            targetFieldOfView = stageFieldOfView;
            lastStageFieldOfView = stageFieldOfView;
        }

        if ((stageTargetOffset - lastStageTargetOffset).sqrMagnitude > 0.000001f)
        {
            targetOffset = stageTargetOffset;
            lastStageTargetOffset = stageTargetOffset;
        }
    }

    private Vector3 GetStageTargetOffset(CameraStageSettings stageSettings)
    {
        return stageSettings.targetOffset;
    }

    private void CacheStageTargets(float radius, float vertical, float fieldOfView, Vector3 offset)
    {
        lastStageRadius = radius;
        lastStageVertical = vertical;
        lastStageFieldOfView = fieldOfView;
        lastStageTargetOffset = offset;
    }

    private float ClampHorizontalIfNeeded(float value)
    {
        CameraStageSettings stageSettings = settings.GetStage(currentStage);
        if (!stageSettings.limitHorizontalRotation) return value;

        float min = Mathf.Min(stageSettings.minHorizontal, stageSettings.maxHorizontal);
        float max = Mathf.Max(stageSettings.minHorizontal, stageSettings.maxHorizontal);
        return Mathf.Clamp(Mathf.DeltaAngle(0f, value), min, max);
    }

    private void ApplyHorizontalLimitForStage(CameraStageSettings stageSettings)
    {
        if (!stageSettings.limitHorizontalRotation || orbital == null) return;

        var h = orbital.HorizontalAxis;
        h.Value = ClampHorizontalForStage(h.Value, stageSettings);
        orbital.HorizontalAxis = h;
    }

    private float ClampHorizontalForStage(float value, CameraStageSettings stageSettings)
    {
        float min = Mathf.Min(stageSettings.minHorizontal, stageSettings.maxHorizontal);
        float max = Mathf.Max(stageSettings.minHorizontal, stageSettings.maxHorizontal);
        return Mathf.Clamp(Mathf.DeltaAngle(0f, value), min, max);
    }

    private float ClampStageVertical(CameraStageSettings stageSettings, float value)
    {
        GetStageVerticalLimits(stageSettings, out float min, out float max);
        return Mathf.Clamp(value, min, max);
    }

    private float GetStageMinVertical(CameraStageSettings stageSettings)
    {
        GetStageVerticalLimits(stageSettings, out float min, out _);
        return min;
    }

    private float GetStageMaxVertical(CameraStageSettings stageSettings)
    {
        GetStageVerticalLimits(stageSettings, out _, out float max);
        return max;
    }

    private void GetStageVerticalLimits(CameraStageSettings stageSettings, out float min, out float max)
    {
        if (stageSettings.minVertical <= 0f && stageSettings.maxVertical <= 0f)
        {
            min = settings.minVertical;
            max = settings.maxVertical;
            return;
        }

        min = Mathf.Min(stageSettings.minVertical, stageSettings.maxVertical);
        max = Mathf.Max(stageSettings.minVertical, stageSettings.maxVertical);
    }

    private void EnsureWorldSpaceOrbit()
    {
        if (!orbital) return;

        var tracker = orbital.TrackerSettings;
        tracker.BindingMode = BindingMode.WorldSpace;
        orbital.TrackerSettings = tracker;
    }

    private void EnsureReferences()
    {
        if (!cmCamera) cmCamera = GetComponent<CinemachineCamera>();
        if (!orbital) orbital = GetComponent<CinemachineOrbitalFollow>();
        if (cmCamera && defaultLookAt == null) defaultLookAt = cmCamera.LookAt;
    }
}
