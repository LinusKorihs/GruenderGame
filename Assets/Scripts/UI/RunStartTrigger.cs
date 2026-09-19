using UnityEngine;

public class RunStartTrigger : MonoBehaviour
{
    [SerializeField] private KeyCode keyboardKey = KeyCode.E;
    [SerializeField] private KeyCode controllerKey = KeyCode.JoystickButton0;
    [SerializeField] private SoundCue interactSound = new SoundCue();

    private bool playerInRange;
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

    private void Update()
    {
        if (!playerInRange || triggered)
            return;

        if (LegacyKeyBinding.WasPressedThisFrame(keyboardKey) || LegacyKeyBinding.WasPressedThisFrame(controllerKey))
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
        interactSound.Play(transform);

        if (LevelStartRunFlowController.Instance != null)
        {
            LevelStartRunFlowController.Instance.OpenSelectionUI();
        }
        else if (RunStartUI.Instance != null)
        {
            RunStartUI.Instance.Open();
        }
    }

    public void ResetInteraction()
    {
        triggered = false;
    }

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<PlayerMinionCommander>() != null) return true;
        return other.CompareTag("Player") || other.transform.root.CompareTag("Player");
    }
}
