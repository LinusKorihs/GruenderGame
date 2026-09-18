using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class RunLifecycleController : MonoBehaviour
{
    public static RunLifecycleController Instance { get; private set; }

    [Header("Transition")]
    [SerializeField, Min(0.05f)] private float fadeDuration = 0.45f;
    [SerializeField, Min(0f)] private float minimumBlackDuration = 0.35f;
    [SerializeField] private Sprite meleeSprite;
    [SerializeField] private Sprite rangedSprite;
    [SerializeField] private Sprite supportSprite;

    [Header("Player Death")]
    [SerializeField, Min(0.1f)] private float deathAnimationFallbackDelay = 2.75f;
    [SerializeField] private string gameOverSoundId = "UI.GameOver";
    [SerializeField] private string restartSceneName = "0. Runtime";
    [SerializeField] private bool enableLogs = true;

    private Canvas canvas;
    private CanvasGroup transitionGroup;
    private TMP_Text transitionTitle;
    private TMP_Text[] minionCounts;
    private CanvasGroup gameOverGroup;
    private Button restartButton;
    private CombatantStats boundPlayerStats;
    private GameObject deadPlayer;
    private Coroutine deathRoutine;
    private bool deathAnimationFinished;
    private bool transitionVisible;
    private float transitionBecameBlackAt;
    private float nextBindingRefresh;

    public bool IsTransitionVisible => transitionVisible;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        BuildUI();
        SetGroupImmediate(transitionGroup, false);
        SetGroupImmediate(gameOverGroup, false);
    }

    private void OnEnable()
    {
        KelpAnimationEvents.PlayerDeathAnimationFinished += HandleDeathAnimationFinished;
    }

    private void OnDisable()
    {
        KelpAnimationEvents.PlayerDeathAnimationFinished -= HandleDeathAnimationFinished;
        UnbindPlayer();
        PlayerControlLock.PopLock(this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextBindingRefresh) return;
        nextBindingRefresh = Time.unscaledTime + 0.25f;
        RefreshPlayerBinding();
    }

    public IEnumerator ShowLevelTransition(string title, int melee, int ranged, int support)
    {
        if (gameOverGroup != null && gameOverGroup.alpha > 0f) yield break;

        transitionTitle.text = NormalizeLevelTitle(title);
        minionCounts[0].text = Mathf.Max(0, melee).ToString();
        minionCounts[1].text = Mathf.Max(0, ranged).ToString();
        minionCounts[2].text = Mathf.Max(0, support).ToString();
        transitionGroup.gameObject.SetActive(true);
        transitionGroup.blocksRaycasts = true;
        transitionVisible = true;
        PlayerControlLock.PushLock(this);
        yield return Fade(transitionGroup, transitionGroup.alpha, 1f, fadeDuration);
        transitionBecameBlackAt = Time.unscaledTime;
        Log($"Transition fully black: {transitionTitle.text} ({melee}/{ranged}/{support}).");
    }

    public IEnumerator HideLevelTransitionWhenReady()
    {
        if (!transitionVisible) yield break;

        float remainingBlackTime = minimumBlackDuration - (Time.unscaledTime - transitionBecameBlackAt);
        if (remainingBlackTime > 0f)
            yield return new WaitForSecondsRealtime(remainingBlackTime);

        yield return null;
        yield return new WaitForEndOfFrame();
        yield return Fade(transitionGroup, transitionGroup.alpha, 0f, fadeDuration);
        transitionVisible = false;
        transitionGroup.blocksRaycasts = false;
        transitionGroup.gameObject.SetActive(false);
        PlayerControlLock.PopLock(this);
        Log("Transition finished; gameplay unlocked.");
    }

    public void CompleteTransitionAfterLoadedScene()
    {
        if (transitionVisible)
            StartCoroutine(HideLevelTransitionWhenReady());
    }

    private void RefreshPlayerBinding()
    {
        GameObject player = PlayerRootResolver.FindAny();
        CombatantStats stats = player != null ? player.GetComponentInChildren<CombatantStats>(true) : null;
        if (stats == boundPlayerStats) return;

        UnbindPlayer();
        boundPlayerStats = stats;
        if (boundPlayerStats != null)
        {
            boundPlayerStats.Died += HandlePlayerDied;
            Log($"Bound player death lifecycle in scene '{boundPlayerStats.gameObject.scene.name}'.");
        }
    }

    private void UnbindPlayer()
    {
        if (boundPlayerStats != null)
            boundPlayerStats.Died -= HandlePlayerDied;
        boundPlayerStats = null;
    }

    private void HandlePlayerDied()
    {
        if (deathRoutine != null) return;
        deadPlayer = boundPlayerStats != null ? PlayerRootResolver.FromGameObject(boundPlayerStats.gameObject) : null;
        deathRoutine = StartCoroutine(PlayerDeathRoutine());
    }

    private IEnumerator PlayerDeathRoutine()
    {
        Log($"Player death detected in scene '{SceneManager.GetActiveScene().name}'.");
        PlayerControlLock.PushLock(this);
        deathAnimationFinished = false;

        if (deadPlayer != null)
        {
            PlayerBrain brain = deadPlayer.GetComponentInChildren<PlayerBrain>(true);
            if (brain != null) brain.enabled = false;

            PlayerMinionCommander commander = deadPlayer.GetComponentInChildren<PlayerMinionCommander>(true);
            if (commander != null) commander.enabled = false;
        }

        Log("Death animation requested; waiting for animation event.");
        KelpAnimatorBridge animationBridge = deadPlayer != null
            ? deadPlayer.GetComponentInChildren<KelpAnimatorBridge>(true)
            : null;
        float deadline = Time.unscaledTime + deathAnimationFallbackDelay;
        while (!deathAnimationFinished && Time.unscaledTime < deadline)
        {
            if (animationBridge != null && animationBridge.IsDeathAnimationComplete())
            {
                deathAnimationFinished = true;
                Log("Death animation completion detected from Animator state.");
                break;
            }
            yield return null;
        }

        if (!deathAnimationFinished)
            Log("Death animation fallback used.");

        ShowGameOver();
        deathRoutine = null;
    }

    private void HandleDeathAnimationFinished(GameObject playerRoot)
    {
        if (deadPlayer == null || playerRoot == null || PlayerRootResolver.FromGameObject(playerRoot) != deadPlayer)
            return;

        deathAnimationFinished = true;
        Log("Death animation event received.");
    }

    private void ShowGameOver()
    {
        if (transitionGroup != null)
            SetGroupImmediate(transitionGroup, false);

        transitionVisible = false;
        gameOverGroup.gameObject.SetActive(true);
        gameOverGroup.alpha = 1f;
        gameOverGroup.interactable = true;
        gameOverGroup.blocksRaycasts = true;
        SoundManager.StopMusic();
        SoundManager.TryPlayConfiguredId(gameOverSoundId, null, 0f);
        EnsureEventSystem();
        EventSystem.current?.SetSelectedGameObject(restartButton.gameObject);
        Log("Game-over screen opened.");
    }

    public void RestartRun()
    {
        if (restartButton != null) restartButton.interactable = false;
        Log("Hard restart requested.");
        HardRestartRunner.Begin(restartSceneName);
    }

    [ContextMenu("Debug - Kill Current Player")]
    private void DebugKillCurrentPlayer()
    {
        RefreshPlayerBinding();
        if (boundPlayerStats == null)
        {
            Debug.LogWarning("[Run Lifecycle] Debug death failed: no player CombatantStats found.", this);
            return;
        }

        boundPlayerStats.SetHealth(0f);
    }

    [ContextMenu("Debug - Show Level Transition")]
    private void DebugShowLevelTransition()
    {
        RunSetupData data = RunSetupData.Instance;
        int melee = data != null ? data.typeA : 1;
        int ranged = data != null ? data.typeB : 1;
        int support = data != null ? data.typeC : 1;
        StartCoroutine(DebugTransitionRoutine(melee, ranged, support));
    }

    private IEnumerator DebugTransitionRoutine(int melee, int ranged, int support)
    {
        yield return ShowLevelTransition("Layer 1", melee, ranged, support);
        yield return new WaitForSecondsRealtime(1f);
        yield return HideLevelTransitionWhenReady();
    }

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject("Run Lifecycle UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        transitionGroup = CreateFullscreenPanel(canvasObject.transform, "Level Transition", new Color(0f, 0f, 0f, 1f));
        transitionTitle = CreateText(transitionGroup.transform, "Level Title", "Layer 1", 74f, new Vector2(0f, 155f), new Vector2(900f, 110f));
        minionCounts = new TMP_Text[3];
        CreateMinionEntry(transitionGroup.transform, "Melee", meleeSprite, -250f, 0, out minionCounts[0]);
        CreateMinionEntry(transitionGroup.transform, "Ranged", rangedSprite, 0f, 1, out minionCounts[1]);
        CreateMinionEntry(transitionGroup.transform, "Support", supportSprite, 250f, 2, out minionCounts[2]);

        gameOverGroup = CreateFullscreenPanel(canvasObject.transform, "Game Over", new Color(0f, 0f, 0f, 0.94f));
        CreateText(gameOverGroup.transform, "Title", "Game Over", 86f, new Vector2(0f, 90f), new Vector2(900f, 130f));
        restartButton = CreateButton(gameOverGroup.transform, "Neustart", new Vector2(0f, -85f));
        restartButton.onClick.AddListener(RestartRun);
    }

    private static CanvasGroup CreateFullscreenPanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        panel.transform.SetParent(parent, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = color;
        return panel.GetComponent<CanvasGroup>();
    }

    private static TMP_Text CreateText(Transform parent, string name, string value, float size, Vector2 position, Vector2 dimensions)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void CreateMinionEntry(Transform parent, string name, Sprite sprite, float x, int index, out TMP_Text count)
    {
        GameObject entry = new GameObject(name, typeof(RectTransform));
        entry.transform.SetParent(parent, false);
        RectTransform entryRect = entry.GetComponent<RectTransform>();
        entryRect.anchorMin = entryRect.anchorMax = new Vector2(0.5f, 0.5f);
        entryRect.anchoredPosition = new Vector2(x, -75f);
        entryRect.sizeDelta = new Vector2(190f, 220f);

        GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(entry.transform, false);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(0f, 30f);
        iconRect.sizeDelta = new Vector2(145f, 145f);
        Image image = iconObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;

        count = CreateText(entry.transform, $"Count {index}", "0", 42f, new Vector2(0f, -75f), new Vector2(180f, 60f));
    }

    private static Button CreateButton(Transform parent, string label, Vector2 position)
    {
        GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(360f, 90f);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.34f, 0.06f, 0.05f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.75f, 0.65f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.75f, 0.45f, 0.4f, 1f);
        button.colors = colors;
        CreateText(buttonObject.transform, "Text", label, 36f, Vector2.zero, rect.sizeDelta);
        return button;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
    }

    private static void SetGroupImmediate(CanvasGroup group, bool visible)
    {
        if (group == null) return;
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
        group.gameObject.SetActive(visible);
    }

    private static IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }
        group.alpha = to;
    }

    private static string NormalizeLevelTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Next Layer";
        if (title.IndexOf("Boss", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Boss";
        if (title.IndexOf("2", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Layer 2";
        if (title.IndexOf("1", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Layer 1";
        return title;
    }

    private void Log(string message)
    {
        if (enableLogs) Debug.Log($"[Run Lifecycle] {message}", this);
    }

    private sealed class HardRestartRunner : MonoBehaviour
    {
        private string targetScene;

        public static void Begin(string sceneName)
        {
            SoundManager.StopMusic();
            GameObject runnerObject = new GameObject("Hard Restart Runner");
            DontDestroyOnLoad(runnerObject);
            HardRestartRunner runner = runnerObject.AddComponent<HardRestartRunner>();
            runner.targetScene = sceneName;
            runner.StartCoroutine(runner.Restart());
        }

        private IEnumerator Restart()
        {
            Time.timeScale = 1f;
            PlayerControlLock.ClearAll();

            DestroyRoots(FindObjectsByType<LevelSystemController>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            DestroyRoots(FindObjectsByType<LevelStartRunFlowController>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            DestroyRoots(FindObjectsByType<RunSetupData>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            DestroyRoots(FindObjectsByType<GameplayHUDController>(FindObjectsInactive.Include, FindObjectsSortMode.None));

            GameObject player = PlayerRootResolver.FindAny();
            if (player != null) Destroy(player.transform.root.gameObject);

            yield return null;
            AsyncOperation load = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
            if (load != null)
                while (!load.isDone) yield return null;

            yield return null;
            SoundManager.EnsureStartupMusicPlaying();

            Destroy(gameObject);
        }

        private static void DestroyRoots<T>(T[] components) where T : Component
        {
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                    Destroy(components[i].transform.root.gameObject);
            }
        }
    }
}
