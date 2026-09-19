using UnityEngine;

public sealed class RunLevelExitTrigger : MonoBehaviour
{
    private LevelStartRunFlowController controller;
    private bool triggered;
    private TextMesh worldLabel;
    private Camera activeCamera;

    private void Awake()
    {
        Transform label = transform.Find("Label");
        if (label != null)
            worldLabel = label.GetComponent<TextMesh>();
    }

    private void LateUpdate()
    {
        UpdateWorldLabelVisibility();
    }

    private void UpdateWorldLabelVisibility()
    {
        if (activeCamera == null)
            activeCamera = Camera.main;
        if (activeCamera == null || worldLabel == null) return;

        Vector3 origin = activeCamera.transform.position;
        Vector3 direction = worldLabel.transform.position - origin;
        float distance = direction.magnitude;
        bool occluded = false;

        if (distance > 0.01f)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
            {
                Transform hit = hits[hitIndex].transform;
                if (hit == null || hit == worldLabel.transform || hit.IsChildOf(worldLabel.transform)) continue;
                if (EnemyTargetUtility.FindTaggedActor(hit, "Player") != null) continue;
                occluded = true;
                break;
            }
        }

        Renderer labelRenderer = worldLabel.GetComponent<Renderer>();
        if (labelRenderer != null)
            labelRenderer.forceRenderingOff = occluded;
    }

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
