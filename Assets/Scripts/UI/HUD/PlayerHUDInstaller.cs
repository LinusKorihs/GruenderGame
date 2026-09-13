using UnityEngine;

public sealed class PlayerHUDInstaller : MonoBehaviour
{
    [SerializeField] private GameObject gameplayHUDPrefab;
    [SerializeField] private GameplayHUDController existingHUD;
    [SerializeField] private bool instantiateIfMissing = true;
    [SerializeField] private bool parentHudToPlayer = true;

    private GameplayHUDController hud;

    private void Awake()
    {
        InstallIfNeeded();
    }

    private void Start()
    {
        InstallIfNeeded();
    }

    private void OnEnable()
    {
        if (hud != null)
        {
            hud.Bind(PlayerRootResolver.FromGameObject(gameObject), GetComponentInChildren<PlayerMinionCommander>(true));
        }
    }

    private void InstallIfNeeded()
    {
        if (hud != null)
            return;

        hud = existingHUD;

        if (hud == null)
        {
            hud = GetComponentInChildren<GameplayHUDController>(true);
        }

        if (hud == null)
        {
            hud = FindFirstObjectByType<GameplayHUDController>(FindObjectsInactive.Include);
        }

        if (hud == null && instantiateIfMissing && gameplayHUDPrefab != null)
        {
            Transform parent = parentHudToPlayer ? transform : null;
            GameObject hudObject = Instantiate(gameplayHUDPrefab, parent);
            hudObject.name = gameplayHUDPrefab.name;
            hud = hudObject.GetComponentInChildren<GameplayHUDController>(true);
        }

        if (hud == null)
            return;

        GameObject playerRoot = PlayerRootResolver.FromGameObject(gameObject);
        PlayerMinionCommander commander = playerRoot != null
            ? playerRoot.GetComponentInChildren<PlayerMinionCommander>(true)
            : GetComponentInChildren<PlayerMinionCommander>(true);

        hud.Bind(playerRoot, commander);
    }
}
