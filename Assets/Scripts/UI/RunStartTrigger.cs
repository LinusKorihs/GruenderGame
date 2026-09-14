using UnityEngine;

public class RunStartTrigger : MonoBehaviour
{
    [SerializeField] private KeyCode keyboardKey = KeyCode.E;
    [SerializeField] private KeyCode controllerKey = KeyCode.JoystickButton0;

    private bool playerInRange;
    private bool triggered;

    private void Update()
    {
        if (!playerInRange || triggered)
            return;

        if (Input.GetKeyDown(keyboardKey) || Input.GetKeyDown(controllerKey))
        {
            StartRunSelection();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other))
            return;

        playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other))
            playerInRange = false;
    }

    private void StartRunSelection()
    {
        triggered = true;

        if (LevelStartRunFlowController.Instance != null)
        {
            LevelStartRunFlowController.Instance.OpenSelectionUI();
        }
        else if (RunStartUI.Instance != null)
        {
            RunStartUI.Instance.Open();
        }
    }

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<PlayerMinionCommander>() != null) return true;
        return other.CompareTag("Player") || other.transform.root.CompareTag("Player");
    }
}
