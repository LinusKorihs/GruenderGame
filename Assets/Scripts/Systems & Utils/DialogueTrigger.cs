using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DialogueTrigger : MonoBehaviour
{
    [Header("UI Referenzen")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private bool preferSharedPrefabDialogPanel = true;

    [Header("Dialog-Inhalt")]
    [TextArea(2, 5)]
    [SerializeField] private string[] dialogueLines;

    [Header("Steuerung")]
    [SerializeField] private KeyCode keyboardKey = KeyCode.E;
    [SerializeField] private KeyCode controllerKey = KeyCode.JoystickButton0;

    [Header("Sound (optional)")]
    [SerializeField] private SoundCue interactSound = new SoundCue();
    [SerializeField] private SoundCue elderKoiTalkSound = new SoundCue("NPC.ElderKoi.Talk");
    [SerializeField] private SoundCue pinchTalkSound = new SoundCue("NPC.Pinch.Talk");

    [Header("NPC Animation")]
    [SerializeField] private bool playDialogueAnimation = true;
    [SerializeField] private bool playAnimationOnEachLine = true;
    [SerializeField] private bool restartAnimationOnEachLine = true;
    [SerializeField] private bool returnToIdleWhenDialogueEnds = true;
    [SerializeField] private ElderKoiAnimatorBridge elderKoiAnimator;
    [SerializeField] private PinchAnimatorBridge pinchAnimator;

    private static DialogueTrigger activeDialogue;
    private static DialogPanelView sharedDialogPanel;

    private bool playerInRange;
    private int currentLineIndex;
    private bool isDialogueActive;
    private bool dialogueAnimationPlayed;
    private bool controlLockActive;
    private DialogPanelView activePanelView;

    private void Awake()
    {
        ResolveAnimationBridge();
    }

    private void Start()
    {
        EnsureDialogueUI();

        SetDialoguePanelVisible(false);
    }

    private void Update()
    {
        bool interactPressed = Input.GetKeyDown(keyboardKey) || Input.GetKeyDown(controllerKey);
        if (!playerInRange || !interactPressed)
            return;

        if (activeDialogue != null && activeDialogue != this)
            return;

        interactSound.Play(transform);

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
        EnsureDialogueUI();

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
        SetDialoguePanelVisible(true);
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

        SetDialoguePanelVisible(false);

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
        if (IsPlayer(other))
        {
            playerInRange = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other))
        {
            playerInRange = false;
            EndDialogue();
        }
    }

    private void ShowCurrentLine()
    {
        EnsureDialogueUI();

        if (elderKoiAnimator != null)
            elderKoiTalkSound.Play(transform);
        else if (pinchAnimator != null)
            pinchTalkSound.Play(transform);

        if (dialogueText != null && dialogueLines != null && currentLineIndex >= 0 && currentLineIndex < dialogueLines.Length)
        {
            dialogueText.text = dialogueLines[currentLineIndex];
        }

        bool shouldPlayAnimation = playDialogueAnimation && (playAnimationOnEachLine || !dialogueAnimationPlayed);
        if (shouldPlayAnimation)
        {
            PlayNpcDialogueAnimation(playAnimationOnEachLine && restartAnimationOnEachLine);
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

    private void EnsureDialogueUI()
    {
        if (!preferSharedPrefabDialogPanel && dialoguePanel != null && dialogueText != null)
            return;

        DialogPanelView panelView = ResolveSharedDialogPanel();
        if (panelView == null)
            return;

        panelView.ResolveReferences();
        if (!panelView.IsValid)
            return;

        if (dialoguePanel != null && dialoguePanel != panelView.PanelRoot)
            dialoguePanel.SetActive(false);

        activePanelView = panelView;
        dialoguePanel = panelView.PanelRoot;
        dialogueText = panelView.DialogueText;
    }

    private void SetDialoguePanelVisible(bool visible)
    {
        if (activePanelView != null)
        {
            activePanelView.SetVisible(visible);
            return;
        }

        if (dialoguePanel != null)
            dialoguePanel.SetActive(visible);
    }

    private DialogPanelView ResolveSharedDialogPanel()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (sharedDialogPanel != null && sharedDialogPanel.gameObject.scene == activeScene)
            return sharedDialogPanel;

        DialogPanelView[] panels = FindObjectsByType<DialogPanelView>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        DialogPanelView fallback = null;
        for (int i = 0; i < panels.Length; i++)
        {
            DialogPanelView panel = panels[i];
            if (panel == null)
                continue;

            if (fallback == null)
                fallback = panel;

            if (panel.gameObject.scene == activeScene)
            {
                sharedDialogPanel = panel;
                return sharedDialogPanel;
            }
        }

        sharedDialogPanel = fallback;
        return sharedDialogPanel;
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

    private void PlayNpcDialogueAnimation(bool restartIfAlreadyPlaying)
    {
        ResolveAnimationBridge();

        if (elderKoiAnimator != null)
        {
            elderKoiAnimator.PlayRandomDialog(restartIfAlreadyPlaying);
            return;
        }

        if (pinchAnimator != null)
        {
            pinchAnimator.PlayDialog(restartIfAlreadyPlaying);
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

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<PlayerMinionCommander>() != null) return true;
        return other.CompareTag("Player") || other.transform.root.CompareTag("Player");
    }
}
