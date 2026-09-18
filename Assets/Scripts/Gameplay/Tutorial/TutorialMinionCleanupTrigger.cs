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
        Transform actor = EnemyTargetUtility.FindTaggedActor(other.transform, "Player");
        if (actor != null)
        {
            TutorialMinionSpawner.ClearAllTutorialMinions();
            ControllerInputHintsHUD.HideInputHints();
        }
    }
}
