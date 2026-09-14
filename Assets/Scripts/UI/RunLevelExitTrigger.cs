using UnityEngine;

public sealed class RunLevelExitTrigger : MonoBehaviour
{
    private LevelStartRunFlowController controller;
    private bool triggered;

    public void Initialize(LevelStartRunFlowController owner)
    {
        controller = owner;
        triggered = false;
    }

    public bool IsOwnedBy(LevelStartRunFlowController owner)
    {
        return controller == owner;
    }

    public bool IsUnowned => controller == null;

    private void OnTriggerEnter(Collider other)
    {
        if (triggered || !IsPlayer(other)) return;

        triggered = true;
        controller ??= LevelStartRunFlowController.Instance;
        controller?.AdvanceToNextLevel();
    }

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<PlayerMinionCommander>() != null) return true;
        return other.CompareTag("Player") || other.transform.root.CompareTag("Player");
    }
}
