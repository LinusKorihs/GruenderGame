using TMPro;
using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    [Header("UI Referenzen")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TextMeshProUGUI dialogueText;

    [Header("Dialog-Inhalt")]
    [TextArea(2, 5)]
    [SerializeField] private string[] dialogueLines;

    [Header("Steuerung")]
    [SerializeField] private KeyCode keyboardKey = KeyCode.E;
    [SerializeField] private KeyCode controllerKey = KeyCode.JoystickButton0;

    [Header("NPC Animation")]
    [SerializeField] private bool playDialogueAnimation = true;
    [SerializeField] private bool playAnimationOnEachLine = true;
    [SerializeField] private bool returnToIdleWhenDialogueEnds = true;
    [SerializeField] private ElderKoiAnimatorBridge elderKoiAnimator;
    [SerializeField] private PinchAnimatorBridge pinchAnimator;

    private static DialogueTrigger activeDialogue;

    private bool playerInRange;
    private int currentLineIndex;
    private bool isDialogueActive;
    private bool dialogueAnimationPlayed;
    private bool controlLockActive;

    private void Awake()
    {
        ResolveAnimationBridge();
    }

    private void Start()
    {
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }
    }

    private void Update()
    {
        bool interactPressed = Input.GetKeyDown(keyboardKey) || Input.GetKeyDown(controllerKey);
        if (!playerInRange || !interactPressed)
            return;

        if (activeDialogue != null && activeDialogue != this)
            return;

        if (!isDialogueActive)
        {
            StartDialogue();
        }
        else
        {
            NextLine();
        }
    }

    private void StartDialogue()
    {
        if (dialoguePanel == null || dialogueText == null)
        {
            Debug.LogWarning($"{name}: DialogueTrigger is missing panel or text reference.", this);
            return;
        }

        if (dialogueLines == null || dialogueLines.Length == 0)
        {
            Debug.LogWarning($"{name}: DialogueTrigger has no dialogue lines.", this);
            return;
        }

        isDialogueActive = true;
        activeDialogue = this;
        currentLineIndex = 0;
        dialogueAnimationPlayed = false;
        SetControlsLocked(true);
        dialoguePanel.SetActive(true);
        ShowCurrentLine();
    }

    private void NextLine()
    {
        currentLineIndex++;
        if (currentLineIndex < dialogueLines.Length)
        {
            ShowCurrentLine();
            return;
        }

        EndDialogue();
    }

    private void EndDialogue()
    {
        if (!isDialogueActive && activeDialogue != this)
            return;

        isDialogueActive = false;
        if (activeDialogue == this)
            activeDialogue = null;

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (returnToIdleWhenDialogueEnds)
            PlayNpcIdleAnimation();

        SetControlsLocked(false);
    }

    private void OnDestroy()
    {
        if (activeDialogue == this)
            activeDialogue = null;

        SetControlsLocked(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            EndDialogue();
        }
    }

    private void ShowCurrentLine()
    {
        if (dialogueText != null && dialogueLines != null && currentLineIndex >= 0 && currentLineIndex < dialogueLines.Length)
        {
            dialogueText.text = dialogueLines[currentLineIndex];
        }

        if (playDialogueAnimation && (playAnimationOnEachLine || !dialogueAnimationPlayed))
        {
            PlayNpcDialogueAnimation();
            dialogueAnimationPlayed = true;
        }
    }

    private void SetControlsLocked(bool locked)
    {
        if (locked == controlLockActive)
            return;

        controlLockActive = locked;

        if (locked)
            PlayerControlLock.PushLock(this);
        else
            PlayerControlLock.PopLock(this);
    }

    private void ResolveAnimationBridge()
    {
        if (elderKoiAnimator == null)
        {
            elderKoiAnimator = GetComponentInParent<ElderKoiAnimatorBridge>();
            if (elderKoiAnimator == null)
                elderKoiAnimator = GetComponentInChildren<ElderKoiAnimatorBridge>(true);
        }

        if (pinchAnimator == null)
        {
            pinchAnimator = GetComponentInParent<PinchAnimatorBridge>();
            if (pinchAnimator == null)
                pinchAnimator = GetComponentInChildren<PinchAnimatorBridge>(true);
        }
    }

    private void PlayNpcDialogueAnimation()
    {
        ResolveAnimationBridge();

        if (elderKoiAnimator != null)
        {
            elderKoiAnimator.PlayRandomDialog();
            return;
        }

        if (pinchAnimator != null)
        {
            pinchAnimator.PlayDialog();
        }
    }

    private void PlayNpcIdleAnimation()
    {
        ResolveAnimationBridge();

        if (elderKoiAnimator != null)
        {
            elderKoiAnimator.PlayIdle();
            return;
        }

        if (pinchAnimator != null)
        {
            pinchAnimator.PlayIdle();
        }
    }
}
