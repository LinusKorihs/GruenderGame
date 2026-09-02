using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBrain : MonoBehaviour
{
    [SerializeField] private PlayerConfig config;

    [Header("Input Actions")] // References for Editor display and runtime access
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference dodgeAction;
    [SerializeField] private InputActionReference punchAction;
    [SerializeField] private InputActionReference cameraLookAction;
    [SerializeField] private InputActionReference cameraToggleAction;
    [SerializeField] private InputActionReference cameraZoomAction;
    [SerializeField] private InputActionReference cameraLockOnAction;

    [SerializeField] private InputActionReference cursorMoveAction;
    [SerializeField] private InputActionReference cursorExtraKeyAction;
    [SerializeField] private PlayerMovementCC movement;
    [SerializeField] private PlayerAim aim;
    [SerializeField] private PlayerDodge dodge;
    [SerializeField] private PlayerPunch punch;
    [SerializeField] private GroundCursor cursor; // optional assign, otherwise auto-find

    // Public getters for Editor and runtime access
    public InputActionReference MoveAction => moveAction;
    public InputActionReference DodgeAction => dodgeAction;
    public InputActionReference PunchAction => punchAction;
    public InputActionReference CameraLookAction => cameraLookAction;
    public InputActionReference CameraToggleAction => cameraToggleAction;
    public InputActionReference CameraZoomAction => cameraZoomAction;
    public InputActionReference CameraLockOnAction => cameraLockOnAction;
    public InputActionReference CursorMoveAction => cursorMoveAction;
    public InputActionReference CursorExtraKeyAction => cursorExtraKeyAction;

    private void Awake()
    {
        ResolveInputActions();

        if (movement == null) movement = GetComponentInChildren<PlayerMovementCC>();
        if (aim == null) aim = GetComponentInChildren<PlayerAim>();
        if (dodge == null) dodge = GetComponentInChildren<PlayerDodge>();
        if (punch == null) punch = GetComponentInChildren<PlayerPunch>();
        if (cursor == null) cursor = GetComponentInChildren<GroundCursor>();

        if (movement == null) Debug.LogError($"{name}: PlayerMovementCC is missing", this);
        if (aim == null) Debug.LogWarning($"{name}: PlayerAim is missing (Fallback to LastMoveDir/forward)", this);
        if (dodge == null) Debug.LogWarning($"{name}: PlayerDodge is missing", this);
        if (punch == null) Debug.LogWarning($"{name}: PlayerPunch is missing", this);
        if (cursor == null) Debug.LogWarning($"{name}: GroundCursor is missing", this);

        if (config == null)
        {
            Debug.LogError($"{name}: PlayerConfig is missing", this);
            enabled = false;
        }
    }

    private void ResolveInputActions()
    {
        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput == null) playerInput = GetComponentInParent<PlayerInput>();

        moveAction = PlayerInputActionResolver.Resolve(moveAction, playerInput, "Player", "Move", this);
        dodgeAction = PlayerInputActionResolver.Resolve(dodgeAction, playerInput, "Player", "Dodge", this);
        punchAction = PlayerInputActionResolver.Resolve(punchAction, playerInput, "Player", "Punch", this);
        cameraLookAction = PlayerInputActionResolver.Resolve(cameraLookAction, playerInput, "Camera", "Look", this);
        cameraToggleAction = PlayerInputActionResolver.Resolve(cameraToggleAction, playerInput, "Camera", "Toggle", this);
        cameraZoomAction = PlayerInputActionResolver.Resolve(cameraZoomAction, playerInput, "Camera", "Zoom", this);
        cameraLockOnAction = PlayerInputActionResolver.Resolve(cameraLockOnAction, playerInput, "Camera", "LockOn", this);
        cursorMoveAction = PlayerInputActionResolver.Resolve(cursorMoveAction, playerInput, "Camera", "CursorMove", this);
        cursorExtraKeyAction = PlayerInputActionResolver.Resolve(cursorExtraKeyAction, playerInput, "Camera", "CursorExtraKey", this);
    }

    private void OnEnable()
    {
        if (moveAction != null) moveAction.action.Enable();
        if (dodgeAction != null) dodgeAction.action.Enable();
        if (punchAction != null) punchAction.action.Enable();
        if (cursorMoveAction != null) cursorMoveAction.action.Enable();
    }

    private void OnDisable()
    {
        if (moveAction != null) moveAction.action.Disable();
        if (dodgeAction != null) dodgeAction.action.Disable();
        if (punchAction != null) punchAction.action.Disable();
        if (cursorMoveAction != null) cursorMoveAction.action.Disable();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // 1) Tick modules
        if (dodge != null) dodge.Tick(dt);
        if (punch != null) punch.Tick(dt);

        // 2) Read input
        Vector2 move = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
        movement.SetMoveInput(move);

        // 3) Apply rules -> movement lock & speed
        bool isDodging = dodge != null && dodge.IsDodging;
        bool isPunching = punch != null && punch.IsPunching;

        movement.MovementLocked = isDodging; // Rule: During Dodge -> Movement Locked
        movement.SpeedMultiplier = isPunching ? config.punchSlowMultiplier : 1f; // During Punch -> Speed Multiplier

        // 4) Actions with rule gates
        // Rule: During Dodge -> No Punch
        if (!isDodging && punchAction != null && punchAction.action.WasPressedThisFrame())
        {
            Vector3 dir = GetFacingDir();
            punch.TryPunch(dir);
        }

        // Rule: During Punch -> No Dodge
        if (!isPunching && dodgeAction != null && dodgeAction.action.WasPressedThisFrame())
        {
            Vector3 dir = GetDodgeDir(move);
            dodge.TryDodge(dir);
        }

        if (movement != null)
        {
            Vector3 facing = GetFacingDir();
            if (cursor != null) cursor.SetMoveDirection(facing);
        }
    }

    private Vector3 GetFacingDir()
    {
        if (aim != null && aim.FacingDirection.sqrMagnitude > 0.0001f) return aim.FacingDirection;
        if (movement.LastMoveDir.sqrMagnitude > 0.0001f) return movement.LastMoveDir;
        return transform.forward;
    }

    private Vector3 GetDodgeDir(Vector2 moveInput)
    {
        if (movement != null && moveInput.magnitude >= config.activeMoveDeadzone)
        {
            Vector3 moveDir = movement.GetCameraRelativeMoveDirection(moveInput);
            if (moveDir.sqrMagnitude > 0.0001f) return moveDir.normalized;
        }

        if (cursor != null && movement != null && !cursor.IsLocked)
        {
            Vector3 toCursor = cursor.WorldPos - movement.transform.position;
            toCursor.y = 0f;
            if (toCursor.sqrMagnitude > 0.0001f) return toCursor.normalized;
        }

        return GetFacingDir();
    }
}

public static class PlayerInputActionResolver
{
    public static InputActionReference Resolve(
        InputActionReference current,
        PlayerInput playerInput,
        string mapName,
        string actionName,
        Object context,
        bool required = true)
    {
        if (current != null && current.action != null)
        {
            return current;
        }

        InputAction action = playerInput != null && playerInput.actions != null
            ? playerInput.actions.FindAction($"{mapName}/{actionName}", false)
            : null;

        if (action != null)
        {
            return InputActionReference.Create(action);
        }

        if (required)
        {
            string source = playerInput == null
                ? "No PlayerInput component found"
                : "PlayerInput has no actions asset, map, or action with that name";

            Debug.LogWarning($"{context.name}: Could not resolve input action '{mapName}/{actionName}'. {source}.", context);
        }

        return current;
    }
}
