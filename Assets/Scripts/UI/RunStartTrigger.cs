using UnityEngine;

public class RunStartTrigger : MonoBehaviour
{
    private bool triggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (triggered) return;

        if (other.CompareTag("Player"))
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
    }
}
