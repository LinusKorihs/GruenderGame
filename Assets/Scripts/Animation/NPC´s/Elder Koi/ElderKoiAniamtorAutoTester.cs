using System.Collections;
using UnityEngine;

public class ElderKoiAnimatorAutoTester : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private ElderKoiAnimatorBridge bridge;

    [Header("Keyboard Test Keys")]
    [SerializeField] private KeyCode idleKey = KeyCode.Alpha1;
    [SerializeField] private KeyCode randomDialogKey = KeyCode.Alpha2;
    [SerializeField] private KeyCode idleBreakKey = KeyCode.Alpha3;
    [SerializeField] private KeyCode dialogV1Key = KeyCode.Alpha4;
    [SerializeField] private KeyCode dialogV2Key = KeyCode.Alpha5;
    [SerializeField] private KeyCode dialogV3Key = KeyCode.Alpha6;
    [SerializeField] private KeyCode resetKey = KeyCode.R;

    [Header("Auto Test")]
    [SerializeField] private bool autoRunOnStart = false;
    [SerializeField] private bool loopTest = false;
    [SerializeField] private float idleWait = 1.5f;
    [SerializeField] private float dialogWait = 2.0f;
    [SerializeField] private float idleBreakWait = 3.0f;

    [Header("On Screen Tester")]
    [SerializeField] private bool showOnScreenTester = true;
    [SerializeField] private Rect testerWindowRect = new Rect(15f, 15f, 280f, 250f);

    [Header("Start Behaviour")]
    [SerializeField] private bool resetToIdleOnStart = true;

    private Coroutine testRoutine;

    private void Awake()
    {
        FindBridgeIfNeeded();
    }

    private void Start()
    {
        if (resetToIdleOnStart && bridge != null)
            bridge.ResetToStartAndIdle();

        if (autoRunOnStart)
            StartAutoTest();
    }

    private void Update()
    {
        FindBridgeIfNeeded();

        if (bridge == null) return;

        if (LegacyKeyBinding.WasPressedThisFrame(idleKey))
            bridge.PlayIdle();

        if (LegacyKeyBinding.WasPressedThisFrame(randomDialogKey))
            bridge.PlayRandomDialog();

        if (LegacyKeyBinding.WasPressedThisFrame(idleBreakKey))
            bridge.PlayIdleBreak();

        if (LegacyKeyBinding.WasPressedThisFrame(dialogV1Key))
            bridge.PlayV1Dialog();

        if (LegacyKeyBinding.WasPressedThisFrame(dialogV2Key))
            bridge.PlayV2Dialog();

        if (LegacyKeyBinding.WasPressedThisFrame(dialogV3Key))
            bridge.PlayV3Dialog();

        if (LegacyKeyBinding.WasPressedThisFrame(resetKey))
            bridge.ResetToStartAndIdle();
    }

    private void OnGUI()
    {
        if (!showOnScreenTester) return;
        if (!Application.isPlaying) return;

        testerWindowRect = GUI.Window(
            654321,
            testerWindowRect,
            DrawTesterWindow,
            "Elder Koi Animation Tester"
        );
    }

    private void DrawTesterWindow(int windowId)
    {
        FindBridgeIfNeeded();

        GUILayout.Label("Keys:");
        GUILayout.Label("1 = Idle");
        GUILayout.Label("2 = Random Dialog");
        GUILayout.Label("3 = IdleBreak");
        GUILayout.Label("4 / 5 / 6 = Dialog V1 / V2 / V3");
        GUILayout.Label("R = Reset");

        GUILayout.Space(5);

        GUI.enabled = bridge != null;

        if (GUILayout.Button("Play Idle"))
            bridge.PlayIdle();

        if (GUILayout.Button("Play Random Dialog"))
            bridge.PlayRandomDialog();

        if (GUILayout.Button("Play IdleBreak"))
            bridge.PlayIdleBreak();

        GUILayout.Space(5);

        if (GUILayout.Button("Play Dialog V1"))
            bridge.PlayV1Dialog();

        if (GUILayout.Button("Play Dialog V2"))
            bridge.PlayV2Dialog();

        if (GUILayout.Button("Play Dialog V3"))
            bridge.PlayV3Dialog();

        GUILayout.Space(5);

        if (GUILayout.Button("Reset To Start + Idle"))
            bridge.ResetToStartAndIdle();

        GUILayout.Space(5);

        if (GUILayout.Button("Start Auto Test"))
            StartAutoTest();

        if (GUILayout.Button("Stop Auto Test"))
            StopAutoTest();

        GUI.enabled = true;

        GUI.DragWindow();
    }

    private void StartAutoTest()
    {
        StopAutoTest();
        testRoutine = StartCoroutine(AutoTestRoutine());
    }

    private void StopAutoTest()
    {
        if (testRoutine != null)
        {
            StopCoroutine(testRoutine);
            testRoutine = null;
        }
    }

    private IEnumerator AutoTestRoutine()
    {
        do
        {
            if (bridge == null)
                yield break;

            bridge.PlayIdle();
            yield return new WaitForSeconds(idleWait);

            bridge.PlayRandomDialog();
            yield return new WaitForSeconds(dialogWait);

            bridge.PlayRandomDialog();
            yield return new WaitForSeconds(dialogWait);

            bridge.PlayRandomDialog();
            yield return new WaitForSeconds(dialogWait);

            bridge.PlayIdleBreak();
            yield return new WaitForSeconds(idleBreakWait);

        } while (loopTest);

        testRoutine = null;
    }

    private void FindBridgeIfNeeded()
    {
        if (bridge != null) return;

        bridge = GetComponent<ElderKoiAnimatorBridge>();

        if (bridge == null)
            bridge = GetComponentInChildren<ElderKoiAnimatorBridge>();

        if (bridge == null)
            bridge = GetComponentInParent<ElderKoiAnimatorBridge>();
    }

    [ContextMenu("Test / Play Idle")]
    private void TestPlayIdle()
    {
        FindBridgeIfNeeded();

        if (bridge != null)
            bridge.PlayIdle();
    }

    [ContextMenu("Test / Play Random Dialog")]
    private void TestPlayRandomDialog()
    {
        FindBridgeIfNeeded();

        if (bridge != null)
            bridge.PlayRandomDialog();
    }

    [ContextMenu("Test / Play IdleBreak")]
    private void TestPlayIdleBreak()
    {
        FindBridgeIfNeeded();

        if (bridge != null)
            bridge.PlayIdleBreak();
    }

    [ContextMenu("Test / Reset To Start + Idle")]
    private void TestResetToStartAndIdle()
    {
        FindBridgeIfNeeded();

        if (bridge != null)
            bridge.ResetToStartAndIdle();
    }
}
