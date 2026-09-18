using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class TutorialMinionButton : MonoBehaviour
{
    public enum ButtonAction { SpawnMelee, SpawnRanged, SpawnSupport, Clear }

    [SerializeField] private TutorialMinionSpawner spawner;
    [SerializeField] private ButtonAction action;
    [SerializeField] private string label;
    [SerializeField] private Color buttonColor = new Color(0.12f, 0.65f, 0.9f, 1f);
    [SerializeField] private KeyCode keyboardKey = KeyCode.E;
    [SerializeField] private KeyCode controllerKey = KeyCode.JoystickButton0;
    [SerializeField, Min(0.05f)] private float rearmDelay = 0.2f;

    private bool playerInRange;
    private float nextUseTime;

    public void Initialize(TutorialMinionSpawner owner, ButtonAction buttonAction, string buttonLabel, Color color)
    {
        spawner = owner;
        action = buttonAction;
        label = buttonLabel;
        buttonColor = color;
    }

    private void Awake()
    {
        if (spawner == null) spawner = GetComponentInParent<TutorialMinionSpawner>();
        if (spawner == null) spawner = FindFirstObjectByType<TutorialMinionSpawner>();
        Collider trigger = GetComponent<Collider>();
        if (trigger != null) trigger.isTrigger = true;
        Renderer buttonRenderer = GetComponent<Renderer>();
        if (buttonRenderer != null) buttonRenderer.material.color = buttonColor;
        Light buttonLight = GetComponent<Light>();
        if (buttonLight != null) buttonLight.color = buttonColor;
    }

    private void Update()
    {
        if (!playerInRange || Time.time < nextUseTime) return;
        if (!Input.GetKeyDown(keyboardKey) && !Input.GetKeyDown(controllerKey)) return;
        nextUseTime = Time.time + rearmDelay;
        Activate();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other)) playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other)) playerInRange = false;
    }

    public void Activate()
    {
        if (spawner == null) return;
        switch (action)
        {
            case ButtonAction.SpawnMelee: spawner.SpawnMelee(); break;
            case ButtonAction.SpawnRanged: spawner.SpawnRanged(); break;
            case ButtonAction.SpawnSupport: spawner.SpawnSupport(); break;
            case ButtonAction.Clear: spawner.ClearAll(); break;
        }
    }

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<PlayerMinionCommander>() != null) return true;
        return other.CompareTag("Player") || other.transform.root.CompareTag("Player");
    }
}
