using UnityEngine;

public class PinchAnimatorAutoTester : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private PinchAnimatorBridge bridge;

    [Header("Keyboard Test Keys")]
    [SerializeField] private KeyCode idleKey = KeyCode.Alpha1;
    [SerializeField] private KeyCode dialogKey = KeyCode.Alpha2;
    [SerializeField] private KeyCode idleBreakKey = KeyCode.Alpha3;
    [SerializeField] private KeyCode resetKey = KeyCode.R;

    [Header("On Screen Tester")]
    [SerializeField] private bool showOnScreenTester = true;
    [SerializeField] private Rect testerWindowRect = new Rect(15f, 15f, 260f, 210f);

    [Header("Start Behaviour")]
    [SerializeField] private bool resetToIdleOnStart = true;

    private void Awake()
    {
        FindBridgeIfNeeded();
    }

    private void Start()
    {
        if (resetToIdleOnStart && bridge != null)
            bridge.ResetToStartAndIdle();
    }

    private void Update()
    {
        FindBridgeIfNeeded();

        if (bridge == null) return;

        if (LegacyKeyBinding.WasPressedThisFrame(idleKey))
            bridge.PlayIdle();

        if (LegacyKeyBinding.WasPressedThisFrame(dialogKey))
            bridge.PlayDialog();

        if (LegacyKeyBinding.WasPressedThisFrame(idleBreakKey))
            bridge.PlayIdleBreak();

        if (LegacyKeyBinding.WasPressedThisFrame(resetKey))
            bridge.ResetToStartAndIdle();
    }

    private void OnGUI()
    {
        if (!showOnScreenTester) return;
        if (!Application.isPlaying) return;

        testerWindowRect = GUI.Window(
            123456,
            testerWindowRect,
            DrawTesterWindow,
            "Pinch Animation Tester"
        );
    }

    private void DrawTesterWindow(int windowId)
    {
        FindBridgeIfNeeded();

        GUILayout.Label("Keys:");
        GUILayout.Label("1 = Idle");
        GUILayout.Label("2 = Dialog");
        GUILayout.Label("3 = IdleBreak + Coin");
        GUILayout.Label("R = Reset");

        GUILayout.Space(5);

        GUI.enabled = bridge != null;

        if (GUILayout.Button("Play Idle"))
            bridge.PlayIdle();

        if (GUILayout.Button("Play Dialog"))
            bridge.PlayDialog();

        if (GUILayout.Button("Play IdleBreak + Coin"))
            bridge.PlayIdleBreak();

        if (GUILayout.Button("Reset To Start + Idle"))
            bridge.ResetToStartAndIdle();

        GUILayout.Space(5);

        if (GUILayout.Button("Force Show Coin"))
            bridge.SetCoinVisible(true);

        if (GUILayout.Button("Force Hide Coin"))
            bridge.SetCoinVisible(false);

        GUI.enabled = true;

        GUI.DragWindow();
    }

    private void FindBridgeIfNeeded()
    {
        if (bridge != null) return;

        bridge = GetComponent<PinchAnimatorBridge>();

        if (bridge == null)
            bridge = GetComponentInChildren<PinchAnimatorBridge>();

        if (bridge == null)
            bridge = GetComponentInParent<PinchAnimatorBridge>();
    }
}
