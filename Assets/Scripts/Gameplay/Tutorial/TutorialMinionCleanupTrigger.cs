using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class TutorialMinionCleanupTrigger : MonoBehaviour
{
    private void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
            trigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // The player mesh owns the CharacterController. Camera and cursor colliders
        // can share its tagged root, but must not clear the tutorial party.
        if (other is not CharacterController || !other.CompareTag("Player") ||
            other.GetComponentInChildren<PlayerMovementCC>(true) == null)
            return;

        TutorialMinionSpawner.ClearAllTutorialMinions();
    }
}
