using UnityEngine;
using TMPro;

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
    // JoystickButton0 entspricht 'A' auf Xbox bzw. 'Kreuz' auf PlayStation
    [SerializeField] private KeyCode controllerKey = KeyCode.JoystickButton0;

    private bool playerInRange = false;
    private int currentLineIndex = 0;
    private bool isDialogueActive = false;

    void Start()
    {
        // Garantiert, dass das Fenster beim Spielstart geschlossen ist
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }
    }

    void Update()
    {
        // Prüft Tastatur ODER Controller
        bool interactPressed = Input.GetKeyDown(keyboardKey) || Input.GetKeyDown(controllerKey);

        if (playerInRange && interactPressed)
        {
            if (!isDialogueActive)
            {
                StartDialogue();
            }
            else
            {
                NextLine();
            }
        }
    }

    private void StartDialogue()
    {
        if (dialogueLines == null || dialogueLines.Length == 0)
        {
            Debug.LogWarning("Keine Dialogzeilen im Inspector eingetragen!");
            return;
        }

        isDialogueActive = true;
        currentLineIndex = 0;
        dialoguePanel.SetActive(true);
        dialogueText.text = dialogueLines[currentLineIndex];
    }

    private void NextLine()
    {
        currentLineIndex++;

        if (currentLineIndex < dialogueLines.Length)
        {
            dialogueText.text = dialogueLines[currentLineIndex];
        }
        else
        {
            EndDialogue();
        }
    }

    private void EndDialogue()
    {
        isDialogueActive = false;
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
    }

    // --- TRIGGER ERKENNUNG (3D) ---
    // (Hinweis: Falls 2D, OnTriggerEnter2D und Collider2D nutzen)
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            Debug.Log("Spieler in Reichweite! Drücke E oder Controller-A.");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            EndDialogue();
            Debug.Log("Spieler hat den Bereich verlassen.");
        }
    }
}