using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class GameplayHUDController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private PlayerHealthHUD playerHealthHUD;
    [SerializeField] private MinionTypeSelectorHUD minionSelectorHUD;
    [SerializeField] private LayerHUDView layerHUD;
    [SerializeField] private BossHealthHUD bossHealthHUD;

    [Header("Visibility")]
    [SerializeField] private int gameplaySortingOrder = 100;
    [SerializeField] private bool hideInMenuScenes = true;
    [SerializeField] private string[] menuSceneNameHints = { "Menu", "MainMenu", "Title" };
    [SerializeField] private string[] gameplaySceneNameHints = { "Tutorial", "Run", "Boss", "Level", "Linus" };

    [Header("Event System")]
    [SerializeField] private bool disableLocalEventSystemWhenDuplicate = true;

    private GameObject playerRoot;
    private PlayerMinionCommander commander;
    private bool transitionHidden;
    private float nextRefreshTime;

    private void Awake()
    {
        ResolveReferences();
        ConfigureCanvas();
        DisableDuplicateLocalEventSystem();
        if (bossHealthHUD != null) bossHealthHUD.Bind(null);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        LevelStartRunFlowController.LevelTransitionStateChanged += HandleTransitionStateChanged;
        RefreshBindings();
        RefreshVisibility();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        LevelStartRunFlowController.LevelTransitionStateChanged -= HandleTransitionStateChanged;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + 0.25f;
        RefreshBindings();
        RefreshVisibility();
        layerHUD?.Refresh();
    }

    public void Bind(GameObject newPlayerRoot, PlayerMinionCommander newCommander = null)
    {
        playerRoot = newPlayerRoot != null ? PlayerRootResolver.FromGameObject(newPlayerRoot) : null;
        commander = newCommander != null
            ? newCommander
            : playerRoot != null
                ? playerRoot.GetComponentInChildren<PlayerMinionCommander>(true)
                : null;

        RefreshBindings();
        RefreshVisibility();
    }

    public void BindBoss(CombatantStats bossStats)
    {
        if (bossHealthHUD == null) return;
        bossHealthHUD.Bind(bossStats);
    }

    public void SetTransitionHidden(bool hidden)
    {
        transitionHidden = hidden;
        RefreshVisibility();
    }

    private void RefreshBindings()
    {
        ResolveReferences();

        if (playerRoot == null)
        {
            playerRoot = PlayerRootResolver.FindAny();
        }

        if (commander == null && playerRoot != null)
        {
            commander = playerRoot.GetComponentInChildren<PlayerMinionCommander>(true);
        }

        if (playerHealthHUD != null)
        {
            CombatantStats stats = playerRoot != null ? playerRoot.GetComponentInChildren<CombatantStats>(true) : null;
            playerHealthHUD.Bind(stats);
        }

        if (commander != null)
        {
            SeedCommanderKnownCounts(commander);
        }

        if (minionSelectorHUD != null)
        {
            minionSelectorHUD.Bind(commander);
        }
    }

    private void RefreshVisibility()
    {
        bool visible = ShouldBeVisible();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        else if (canvas != null)
        {
            canvas.enabled = visible;
        }
    }

    private bool ShouldBeVisible()
    {
        if (transitionHidden)
            return false;

        if (playerRoot == null)
            return false;

        Scene activeScene = SceneManager.GetActiveScene();
        if (hideInMenuScenes && IsMenuScene(activeScene.name))
            return false;

        if (LevelFlowController.Instance != null && LevelFlowController.Instance.CurrentStep != null)
            return true;

        if (RunSetupData.Instance != null)
            return true;

        return IsGameplaySceneName(activeScene.name);
    }

    private void ResolveReferences()
    {
        if (canvas == null) canvas = GetComponentInChildren<Canvas>(true);
        if (canvasGroup == null && canvas != null) canvasGroup = canvas.GetComponent<CanvasGroup>();
        if (canvasGroup == null && canvas != null) canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
        if (playerHealthHUD == null) playerHealthHUD = GetComponentInChildren<PlayerHealthHUD>(true);
        if (minionSelectorHUD == null) minionSelectorHUD = GetComponentInChildren<MinionTypeSelectorHUD>(true);
        if (layerHUD == null) layerHUD = GetComponentInChildren<LayerHUDView>(true);
        if (bossHealthHUD == null) bossHealthHUD = GetComponentInChildren<BossHealthHUD>(true);
    }

    private void ConfigureCanvas()
    {
        if (canvas == null) return;

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = gameplaySortingOrder;
    }

    private void DisableDuplicateLocalEventSystem()
    {
        if (!disableLocalEventSystemWhenDuplicate)
            return;

        EventSystem local = GetComponentInChildren<EventSystem>(true);
        if (local == null)
            return;

        EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < eventSystems.Length; i++)
        {
            EventSystem candidate = eventSystems[i];
            if (candidate == null || candidate == local)
                continue;

            local.gameObject.SetActive(false);
            return;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _ = scene;
        _ = mode;
        RefreshBindings();
        RefreshVisibility();
    }

    private void HandleTransitionStateChanged(bool isTransitioning)
    {
        SetTransitionHidden(isTransitioning);
    }

    private bool IsMenuScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || menuSceneNameHints == null)
            return false;

        for (int i = 0; i < menuSceneNameHints.Length; i++)
        {
            string hint = menuSceneNameHints[i];
            if (!string.IsNullOrWhiteSpace(hint) &&
                sceneName.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsGameplaySceneName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || gameplaySceneNameHints == null)
            return false;

        for (int i = 0; i < gameplaySceneNameHints.Length; i++)
        {
            string hint = gameplaySceneNameHints[i];
            if (!string.IsNullOrWhiteSpace(hint) &&
                sceneName.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void SeedCommanderKnownCounts(PlayerMinionCommander targetCommander)
    {
        RunSetupData data = RunSetupData.Instance;
        if (targetCommander == null || data == null)
            return;

        targetCommander.SetKnownMinionCounts(data.typeA, data.typeB, data.typeC);
    }
}
