using System.Collections;
using UnityEngine;

public class BurrowerAnimatorAutoTester : MonoBehaviour
{
    private enum TestScenario
    {
        WakeUpToWalk,
        SecondAttackToHover,
        GrabAttackFromHover,
        LowHpEscapeFromWalk,
        LowHpEscapeFromHover,
        FullCycleNoLowHp,
        DeathFlying,
        DeathDigging
    }

    [Header("References")]
    [SerializeField] private BurrowerAnimatorBridge burrowerAnimator;

    [Header("Transform Reset")]
    [Tooltip("Das Objekt, das auf den Ursprungspunkt zurückgesetzt werden soll. Leer lassen = dieses GameObject.")]
    [SerializeField] private Transform objectToReset;

    [Tooltip("Speichert beim Start die aktuelle Position/Rotation/Scale als Ursprungspunkt.")]
    [SerializeField] private bool captureOriginOnStart = true;

    [Tooltip("Setzt die Position vor jedem automatischen Test zurück.")]
    [SerializeField] private bool resetTransformBeforeAutoTest = true;

    private Vector3 originPosition;
    private Quaternion originRotation;
    private Vector3 originScale;
    private bool originCaptured;

    [Header("Auto Test Settings")]
    [SerializeField] private bool enableLogs;
    [SerializeField] private bool autoRunOnStart = true;
    [SerializeField] private bool loopTest = false;
    [SerializeField] private TestScenario scenario = TestScenario.FullCycleNoLowHp;

    [Header("Timing")]
    [SerializeField] private float hiddenWait = 1.0f;
    [SerializeField] private float wakeUpSequenceWait = 3.0f;
    [SerializeField] private float secondAttackWait = 2.0f;
    [SerializeField] private float grabAttackWait = 2.0f;
    [SerializeField] private float lowHpEscapeWait = 3.0f;
    [SerializeField] private float deathWait = 2.0f;
    [SerializeField] private float delayBetweenLoops = 1.0f;

    [Header("Optional Speed Test")]
    [SerializeField] private bool setSpeedDuringWalk = false;
    [SerializeField] private float walkSpeed = 1.0f;
    [SerializeField] private float walkSpeedWait = 0.5f;

    private Coroutine testRoutine;

    private void Awake()
    {
        if (burrowerAnimator == null)
        {
            burrowerAnimator = GetComponent<BurrowerAnimatorBridge>();
        }

        if (objectToReset == null)
        {
            objectToReset = transform;
        }

        if (burrowerAnimator == null)
        {
            Debug.LogError("BurrowerAnimatorAutoTester: Keine BurrowerAnimatorBridge gefunden. Bitte beide Scripts auf dasselbe GameObject legen.");
        }

        if (captureOriginOnStart)
        {
            CaptureCurrentTransformAsOrigin();
        }
    }

    private void Start()
    {
        if (autoRunOnStart && burrowerAnimator != null)
        {
            StartAutoTest();
        }
    }

    public void StartAutoTest()
    {
        if (testRoutine != null)
        {
            StopCoroutine(testRoutine);
        }

        testRoutine = StartCoroutine(AutoTestRoutine());
    }

    public void StopAutoTest()
    {
        if (testRoutine != null)
        {
            StopCoroutine(testRoutine);
            testRoutine = null;
        }
    }

    [ContextMenu("TEST / Capture Current Transform As Origin")]
    public void CaptureCurrentTransformAsOrigin()
    {
        if (objectToReset == null)
        {
            objectToReset = transform;
        }

        originPosition = objectToReset.position;
        originRotation = objectToReset.rotation;
        originScale = objectToReset.localScale;
        originCaptured = true;

        if (enableLogs) Debug.Log("Burrower Tester: Current transform captured as origin.");
    }

    [ContextMenu("TEST / Reset Transform To Origin")]
    public void ResetTransformToOrigin()
    {
        if (!originCaptured)
        {
            CaptureCurrentTransformAsOrigin();
        }

        objectToReset.position = originPosition;
        objectToReset.rotation = originRotation;
        objectToReset.localScale = originScale;

        if (enableLogs) Debug.Log("Burrower Tester: Transform reset to origin.");
    }

    [ContextMenu("TEST / Reset Animation To Hidden At Origin")]
    public void ResetAnimationToHiddenAtOrigin()
    {
        StopAutoTest();
        ResetTransformToOrigin();

        if (burrowerAnimator != null)
        {
            burrowerAnimator.ResetToHidden();
        }

        if (enableLogs) Debug.Log("Burrower Tester: Reset to Hidden at origin.");
    }

    [ContextMenu("TEST / Reset Animation To Walk At Origin")]
    public void ResetAnimationToWalkAtOrigin()
    {
        StopAutoTest();
        ResetTransformToOrigin();

        if (burrowerAnimator != null)
        {
            burrowerAnimator.ResetToWalkBase();
        }

        if (enableLogs) Debug.Log("Burrower Tester: Reset to Walk/Base at origin.");
    }

    [ContextMenu("TEST / Reset Animation To Hover At Origin")]
    public void ResetAnimationToHoverAtOrigin()
    {
        StopAutoTest();
        ResetTransformToOrigin();

        if (burrowerAnimator != null)
        {
            burrowerAnimator.ResetToHover();
        }

        if (enableLogs) Debug.Log("Burrower Tester: Reset to Hover at origin.");
    }

    private IEnumerator AutoTestRoutine()
    {
        do
        {
            if (enableLogs) Debug.Log("Burrower AUTO TEST START: " + scenario);

            if (resetTransformBeforeAutoTest)
            {
                ResetTransformToOrigin();
            }

            switch (scenario)
            {
                case TestScenario.WakeUpToWalk:
                    yield return TestWakeUpToWalk();
                    break;

                case TestScenario.SecondAttackToHover:
                    yield return TestSecondAttackToHover();
                    break;

                case TestScenario.GrabAttackFromHover:
                    yield return TestGrabAttackFromHover();
                    break;

                case TestScenario.LowHpEscapeFromWalk:
                    yield return TestLowHpEscapeFromWalk();
                    break;

                case TestScenario.LowHpEscapeFromHover:
                    yield return TestLowHpEscapeFromHover();
                    break;

                case TestScenario.FullCycleNoLowHp:
                    yield return TestFullCycleNoLowHp();
                    break;

                case TestScenario.DeathFlying:
                    yield return TestDeathFlying();
                    break;

                case TestScenario.DeathDigging:
                    yield return TestDeathDigging();
                    break;
            }

            if (enableLogs) Debug.Log("Burrower AUTO TEST FINISHED: " + scenario);

            if (loopTest)
            {
                yield return new WaitForSeconds(delayBetweenLoops);
            }

        } while (loopTest);

        testRoutine = null;
    }

    private IEnumerator TestWakeUpToWalk()
    {
        burrowerAnimator.ResetToHidden();
        yield return new WaitForSeconds(hiddenWait);

        burrowerAnimator.WakeUp();
        yield return new WaitForSeconds(wakeUpSequenceWait);

        yield return OptionalWalkSpeedTest();
    }

    private IEnumerator TestSecondAttackToHover()
    {
        burrowerAnimator.ResetToWalkBase();
        yield return new WaitForSeconds(0.3f);

        yield return OptionalWalkSpeedTest();

        burrowerAnimator.PlaySecondAttack();
        yield return new WaitForSeconds(secondAttackWait);
    }

    private IEnumerator TestGrabAttackFromHover()
    {
        burrowerAnimator.ResetToHover();
        yield return new WaitForSeconds(0.3f);

        burrowerAnimator.PlayGrabAttack();
        yield return new WaitForSeconds(grabAttackWait);
    }

    private IEnumerator TestLowHpEscapeFromWalk()
    {
        burrowerAnimator.ResetToWalkBase();
        yield return new WaitForSeconds(0.5f);

        burrowerAnimator.PlayFlyDown();
        yield return new WaitForSeconds(lowHpEscapeWait);
    }

    private IEnumerator TestLowHpEscapeFromHover()
    {
        burrowerAnimator.ResetToHover();
        yield return new WaitForSeconds(0.5f);

        burrowerAnimator.PlayFlyDown();
        yield return new WaitForSeconds(lowHpEscapeWait);
    }

    private IEnumerator TestFullCycleNoLowHp()
    {
        burrowerAnimator.ResetToHidden();
        yield return new WaitForSeconds(hiddenWait);

        burrowerAnimator.WakeUp();
        yield return new WaitForSeconds(wakeUpSequenceWait);

        yield return OptionalWalkSpeedTest();

        burrowerAnimator.PlaySecondAttack();
        yield return new WaitForSeconds(secondAttackWait);

        burrowerAnimator.PlayGrabAttack();
        yield return new WaitForSeconds(grabAttackWait);
    }

    private IEnumerator TestDeathFlying()
    {
        burrowerAnimator.ResetToHover();
        yield return new WaitForSeconds(0.5f);

        burrowerAnimator.PlayDeathFlying();
        yield return new WaitForSeconds(deathWait);
    }

    private IEnumerator TestDeathDigging()
    {
        burrowerAnimator.ResetToHidden();
        yield return new WaitForSeconds(0.5f);

        burrowerAnimator.PlayDeathDigging();
        yield return new WaitForSeconds(deathWait);
    }

    private IEnumerator OptionalWalkSpeedTest()
    {
        if (!setSpeedDuringWalk)
        {
            yield break;
        }

        burrowerAnimator.SetSpeed(walkSpeed);
        yield return new WaitForSeconds(walkSpeedWait);
        burrowerAnimator.SetSpeed(0f);
    }
}
