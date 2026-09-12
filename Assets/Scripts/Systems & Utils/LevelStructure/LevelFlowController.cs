using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum LevelFlowAdvanceAction
{
    NotHandled,
    GeneratePCGLevel,
    LoadingScene,
    Complete,
    Blocked
}

public sealed class LevelFlowController : MonoBehaviour
{
    private const string DefaultStartSceneName = "1. Linus Tutorial";
    private const string DefaultRunSceneName = "2. Linus Run";
    private const string DefaultBossSceneName = "3. Linus Boss";
    private const string DefaultFlowConfigResourcesPath = "LevelFlow/SO_LevelFlow_Linus";
    private const string LevelFlowResourcesFolder = "LevelFlow";
    private const string RuntimeSystemsRootName = "Runtime_Systems";
    private const string LevelRuntimeRootName = "Level_Runtime";
    private const string Level1ProfilePath = "Assets/ScriptableObjects/PCG/Profiles/Level/SO_Level1.asset";
    private const string Level2ProfilePath = "Assets/ScriptableObjects/PCG/Profiles/Level/SO_Level2.asset";

    public static LevelFlowController Instance { get; private set; }

    [Header("Flow Source")]
    [SerializeField] private LevelFlowConfig flowConfig;
    [SerializeField] private bool useInlineFlowSteps;
    [SerializeField] private List<LevelFlowStep> inlineFlowSteps = new List<LevelFlowStep>();

    [Header("Startup")]
    [SerializeField] private bool loadFirstStepOnStart;
    [SerializeField] private bool infiniteLevelFlow;

    [Header("Debug / Fallback")]
    [SerializeField] private bool useEditorFallbackFlow = true;
    [SerializeField] private bool logFlow = true;

    private readonly List<LevelFlowStep> editorFallbackSteps = new List<LevelFlowStep>();
    private LevelProfileLoader levelProfileLoader;
    private LevelAtmosphereController atmosphereController;
    private StaticLevelLayoutBuilder staticLayoutBuilder;
    private int currentStepIndex = -1;
    private bool runActive;
    private bool firstStepLoadRequested;
    private int lastBuiltStaticStepIndex = -1;
    private string lastBuiltStaticSceneName;
    private Scene activeRuntimeFlowScene;
    private Scene pendingRuntimeSceneToUnload;
    private Transform activeLevelSceneRoot;

    public LevelFlowStep CurrentStep
    {
        get
        {
            IReadOnlyList<LevelFlowStep> steps = Steps;
            return currentStepIndex >= 0 && currentStepIndex < steps.Count ? steps[currentStepIndex] : null;
        }
    }

    public LevelFlowConfig FlowConfig => flowConfig;
    public bool InfiniteLevelFlow => infiniteLevelFlow;
    public GameObject RuntimeRoot => GetRuntimeRootGameObject();

    private IReadOnlyList<LevelFlowStep> Steps
    {
        get
        {
            if (useInlineFlowSteps && inlineFlowSteps != null && inlineFlowSteps.Count > 0)
                return inlineFlowSteps;

            ResolveFlowConfig();
            if (flowConfig != null && flowConfig.steps != null && flowConfig.steps.Count > 0)
                return flowConfig.steps;

            EnsureEditorFallbackFlow();
            return editorFallbackSteps;
        }
    }

    public static LevelFlowController EnsureInstance()
    {
        if (Instance != null) return Instance;

        LevelFlowController existing = FindFirstObjectByType<LevelFlowController>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        GameObject host = new GameObject(nameof(LevelFlowController));
        return host.AddComponent<LevelFlowController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(GetDuplicateDestroyTarget());
            return;
        }

        Instance = this;
        DontDestroyOnLoad(GetRuntimeRootGameObject());
        SceneManager.sceneLoaded += HandleSceneLoaded;

        if (levelProfileLoader == null)
        {
            levelProfileLoader = ResolveLevelLoaderInRuntimeRoot();
        }

        ResolveAtmosphereController();
    }

    private void Start()
    {
        if (loadFirstStepOnStart)
        {
            LoadFirstStep();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    public bool BeginRun()
    {
        IReadOnlyList<LevelFlowStep> steps = Steps;
        int firstStep = FirstPlayableStepIndex(steps);
        if (firstStep < 0)
        {
            Debug.LogWarning("[Level Flow] No playable PCG or Boss step found. Falling back to the existing run flow.", this);
            runActive = false;
            currentStepIndex = -1;
            return false;
        }

        runActive = true;
        currentStepIndex = firstStep;
        ApplyRunSetupLevelIndex(CurrentStep);
        Log($"Run started at step {currentStepIndex}: {DescribeStep(CurrentStep)}.");
        return true;
    }

    public void ConfigureLevelLoader(LevelProfileLoader loader)
    {
        if (loader == null)
            return;

        levelProfileLoader = loader;
        staticLayoutBuilder = null;
        ApplyRuntimeSceneRootToLoader(levelProfileLoader);
        OrganizeKnownRuntimeObjects();
    }

    public void ConfigureAtmosphere(LevelAtmosphereController controller)
    {
        if (controller == null)
            return;

        atmosphereController = controller;
    }

    [ContextMenu("Load First Flow Step")]
    public bool LoadFirstStep()
    {
        if (firstStepLoadRequested)
            return false;

        IReadOnlyList<LevelFlowStep> steps = Steps;
        int firstStep = FirstStepIndex(steps);
        if (firstStep < 0)
        {
            Debug.LogWarning("[Level Flow] Cannot load first step because the flow has no steps.", this);
            return false;
        }

        firstStepLoadRequested = true;
        runActive = false;
        currentStepIndex = firstStep;

        LevelFlowStep step = CurrentStep;
        ApplyRunSetupLevelIndex(step);
        Log($"Loading first flow step {currentStepIndex}: {DescribeStep(step)}.");

        string targetSceneName = ResolveTargetSceneName(step);
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrWhiteSpace(targetSceneName) &&
            !string.Equals(activeScene.name, targetSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
            bool loading = LoadFlowScene(step);
            if (!loading)
            {
                firstStepLoadRequested = false;
            }

            return loading;
        }

        if (step.staticLayoutProfile != null)
        {
            bool built = BuildStaticLayout(step);
            firstStepLoadRequested = false;
            return built;
        }

        firstStepLoadRequested = false;
        return true;
    }

    public bool PrepareStaticStepForScene(Scene scene)
    {
        IReadOnlyList<LevelFlowStep> steps = Steps;
        int stepIndex = FindStepIndexForScene(steps, scene.name);
        if (stepIndex < 0)
            return false;

        LevelFlowStep step = steps[stepIndex];
        if (step == null || step.staticLayoutProfile == null)
            return false;

        currentStepIndex = stepIndex;
        if (step.stepType == LevelFlowStepType.Tutorial)
        {
            runActive = false;
        }

        if (WasStaticStepBuiltForScene(stepIndex, scene.name))
        {
            Log($"Static step {stepIndex} already prepared for scene '{scene.name}'.");
            return true;
        }

        return BuildStaticLayout(step);
    }

    public bool PrepareCurrentStepSceneForGeneration(out string targetSceneName, out bool sceneLoadPending)
    {
        targetSceneName = null;
        sceneLoadPending = false;

        LevelFlowStep step = CurrentStep;
        if (step == null)
            return false;

        targetSceneName = ResolveTargetSceneName(step);
        if (string.IsNullOrWhiteSpace(targetSceneName))
            return false;

        Scene activeScene = SceneManager.GetActiveScene();
        if (string.Equals(activeScene.name, targetSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
            if (UsesRuntimeScene(step))
            {
                activeRuntimeFlowScene = activeScene;
                activeLevelSceneRoot = GetOrCreateRuntimeSceneRoot(activeScene);
                ApplyAtmosphere(step, activeLevelSceneRoot);
                ApplyRuntimeSceneRootToLoader(levelProfileLoader);
            }

            return true;
        }

        if (UsesRuntimeScene(step))
        {
            Scene runtimeScene = CreateOrActivateRuntimeScene(step);
            return runtimeScene.IsValid() && runtimeScene.isLoaded;
        }

        if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            Debug.LogWarning(
                $"[Level Flow] Cannot load scene '{targetSceneName}'. Add it to Build Settings or switch the Flow step to Runtime Scene.",
                this);
            return false;
        }

        Log($"Loading scene '{targetSceneName}' for {DescribeStep(step)}.");
        sceneLoadPending = true;
        SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
        return true;
    }

    public bool PrepareCurrentPCGLevel(RoomAssemblerGenerator assembler, LevelContentSpawner contentSpawner)
    {
        if (!runActive)
            return false;

        LevelFlowStep step = CurrentStep;
        if (step == null || step.stepType != LevelFlowStepType.PCG)
            return false;

        if (step.levelProfile == null)
        {
            Debug.LogWarning($"[Level Flow] {step.DisplayName}: PCG step has no LevelConfigProfile. Existing run flow will try its fallback config.", this);
            return false;
        }

        ApplyRunSetupLevelIndex(step);
        LevelProfileLoader loader = EnsureLevelProfileLoader(assembler, contentSpawner);
        if (loader == null)
        {
            Debug.LogWarning("[Level Flow] Cannot apply PCG profile because no LevelProfileLoader could be created.", this);
            return false;
        }

        ApplyRuntimeSceneRootToLoader(loader);
        loader.ConfigureTargets(assembler, contentSpawner);
        loader.ConfigureAtmosphere(atmosphereController);
        loader.SetLevelAtmosphereProfile(step.atmosphereProfile, false);
        bool applied = loader.ApplyLevelProfile(step.levelProfile);
        loader.ApplyLevelAtmosphereProfile(step.atmosphereProfile, activeLevelSceneRoot);
        Log($"Prepared PCG step {currentStepIndex}: {DescribeStep(step)}. Applied={applied}.");
        return applied;
    }

    public bool TryGetPCGTargets(out RoomAssemblerGenerator assembler, out LevelContentSpawner contentSpawner)
    {
        assembler = null;
        contentSpawner = null;

        LevelProfileLoader loader = EnsureLevelProfileLoader(null, null);
        if (loader == null)
            return false;

        loader.ResolveSceneTargets();
        assembler = loader.RoomAssemblerGenerator;
        contentSpawner = loader.LevelContentSpawner;
        return assembler != null;
    }

    public LevelFlowAdvanceAction AdvanceAfterCurrentLevelExit()
    {
        if (!runActive)
            return LevelFlowAdvanceAction.NotHandled;

        IReadOnlyList<LevelFlowStep> steps = Steps;
        int nextIndex = NextStepIndex(steps, currentStepIndex);
        if (nextIndex < 0)
        {
            if (infiniteLevelFlow)
            {
                nextIndex = FirstPlayableStepIndex(steps);
                if (nextIndex >= 0)
                {
                    Log($"Infinite flow loop: returning to playable step {nextIndex}.");
                }
            }
        }

        if (nextIndex < 0)
        {
            Log("Flow completed because there is no next step.");
            runActive = false;
            return LevelFlowAdvanceAction.Complete;
        }

        currentStepIndex = nextIndex;
        LevelFlowStep nextStep = CurrentStep;
        ApplyRunSetupLevelIndex(nextStep);
        Log($"Advancing to step {currentStepIndex}: {DescribeStep(nextStep)}.");

        if (nextStep.stepType == LevelFlowStepType.PCG)
            return LevelFlowAdvanceAction.GeneratePCGLevel;

        if (nextStep.stepType == LevelFlowStepType.Boss)
            return LoadFlowScene(nextStep) ? LevelFlowAdvanceAction.LoadingScene : LevelFlowAdvanceAction.Blocked;

        if (nextStep.stepType == LevelFlowStepType.End)
        {
            runActive = false;
            return LevelFlowAdvanceAction.Complete;
        }

        return LevelFlowAdvanceAction.Blocked;
    }

    private LevelProfileLoader EnsureLevelProfileLoader(RoomAssemblerGenerator assembler, LevelContentSpawner contentSpawner)
    {
        if (levelProfileLoader != null)
        {
            ApplyRuntimeSceneRootToLoader(levelProfileLoader);
            OrganizeKnownRuntimeObjects();
            return levelProfileLoader;
        }

        levelProfileLoader = ResolveLevelLoaderInRuntimeRoot();
        if (levelProfileLoader != null)
        {
            ApplyRuntimeSceneRootToLoader(levelProfileLoader);
            OrganizeKnownRuntimeObjects();
            return levelProfileLoader;
        }

        levelProfileLoader = FindFirstObjectByType<LevelProfileLoader>(FindObjectsInactive.Include);
        if (levelProfileLoader != null)
        {
            ApplyRuntimeSceneRootToLoader(levelProfileLoader);
            OrganizeKnownRuntimeObjects();
            return levelProfileLoader;
        }

        Debug.LogWarning(
            "[Level Flow] No LevelProfileLoader found. Put PF_LevelLoader under PF_LevelSystem or add one to the active scene.",
            this);
        return null;
    }

    public bool IsRuntimeLevelLoaderRoot(GameObject target)
    {
        return target != null && levelProfileLoader != null && target == levelProfileLoader.gameObject;
    }

    public void RegisterRuntimeObject(GameObject target)
    {
        if (target == null || levelProfileLoader == null || target == levelProfileLoader.gameObject)
            return;

        AttachRuntimeObject(target);
    }

    private void AttachRuntimeObject(GameObject target)
    {
        if (target == null)
            return;

        Transform targetTransform = target.transform;
        Transform runtimeRoot = GetRuntimeRootTransform();
        if (runtimeRoot == null)
            return;

        if (targetTransform == runtimeRoot || targetTransform.IsChildOf(runtimeRoot) || runtimeRoot.IsChildOf(targetTransform))
            return;

        Transform runtimeSystemsRoot = GetOrCreateChild(runtimeRoot, RuntimeSystemsRootName);
        targetTransform.SetParent(runtimeSystemsRoot, true);
    }

    private bool BuildStaticLayout(LevelFlowStep step)
    {
        StaticLevelLayoutBuilder builder = EnsureStaticLayoutBuilder();
        if (builder == null)
        {
            Debug.LogWarning("[Level Flow] Cannot build static layout because no StaticLevelLayoutBuilder is available.", this);
            return false;
        }

        if (activeLevelSceneRoot != null)
        {
            builder.SetRuntimeLevelRoot(activeLevelSceneRoot);
        }

        bool built = builder.Build(step.staticLayoutProfile);
        if (built && step.stepType == LevelFlowStepType.Boss)
        {
            LevelStartRunFlowController.Instance?.EnsureSelectedMinionsForCurrentScene();
        }

        OrganizeKnownRuntimeObjects();
        RememberStaticBuild(step, built);
        Log($"Prepared static step {currentStepIndex}: {DescribeStep(step)}. Built={built}.");
        return built;
    }

    private StaticLevelLayoutBuilder EnsureStaticLayoutBuilder()
    {
        if (staticLayoutBuilder != null)
            return staticLayoutBuilder;

        LevelProfileLoader loader = EnsureLevelProfileLoader(null, null);
        if (loader == null)
            return null;

        staticLayoutBuilder = loader.GetComponent<StaticLevelLayoutBuilder>();
        if (staticLayoutBuilder == null)
        {
            staticLayoutBuilder = loader.gameObject.AddComponent<StaticLevelLayoutBuilder>();
        }

        if (activeLevelSceneRoot != null)
        {
            staticLayoutBuilder.SetRuntimeLevelRoot(activeLevelSceneRoot);
        }

        OrganizeKnownRuntimeObjects();
        return staticLayoutBuilder;
    }

    private void OrganizeKnownRuntimeObjects()
    {
        if (levelProfileLoader == null)
            return;

        RegisterRuntimeObject(gameObject);
        RegisterRuntimeObject(FindNamedRoot(nameof(LevelStartRunFlowController)));
        RegisterRuntimeObject(FindNamedRoot(nameof(RunSetupData)));
        RegisterRuntimeObject(FindNamedRoot("Debug Updater"));
    }

    private bool LoadFlowScene(LevelFlowStep step)
    {
        string sceneName = ResolveTargetSceneName(step);
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[Level Flow] Cannot load scene because the step has no scene name.", this);
            return false;
        }

        if (UsesRuntimeScene(step))
        {
            Scene runtimeScene = CreateOrActivateRuntimeScene(step);
            if (!runtimeScene.IsValid() || !runtimeScene.isLoaded)
                return false;

            if (step.staticLayoutProfile != null)
            {
                BuildStaticLayout(step);
            }

            if (step.stepType == LevelFlowStepType.Tutorial)
            {
                LevelStartRunFlowController.EnsureForScene(runtimeScene);
            }

            UnloadPreviousRuntimeSceneIfReady();
            StartCoroutine(CleanupStaticSceneAfterLoad(runtimeScene));
            return true;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning(
                $"[Level Flow] Cannot load scene '{sceneName}'. Add it to Build Settings or fix the Flow step scene name.",
                this);
            return false;
        }

        Log($"Loading scene '{sceneName}' for {DescribeStep(step)}.");
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        return true;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (firstStepLoadRequested)
        {
            firstStepLoadRequested = false;
        }

        LevelFlowStep step = CurrentStep;
        if (step == null || step.stepType == LevelFlowStepType.PCG)
            return;

        if (!string.Equals(scene.name, ResolveTargetSceneName(step), System.StringComparison.OrdinalIgnoreCase))
            return;

        if (step.staticLayoutProfile != null)
        {
            BuildStaticLayout(step);
        }

        StartCoroutine(CleanupStaticSceneAfterLoad(scene));
    }

    private IEnumerator CleanupStaticSceneAfterLoad(Scene scene)
    {
        yield return null;
        DisableDuplicateAudioListeners(scene);
    }

    private Scene CreateOrActivateRuntimeScene(LevelFlowStep step)
    {
        string sceneName = ResolveTargetSceneName(step);
        if (string.IsNullOrWhiteSpace(sceneName))
            return default;

        Scene previousRuntimeScene = activeRuntimeFlowScene;
        Scene runtimeScene = SceneManager.GetSceneByName(sceneName);
        if (!runtimeScene.IsValid() || !runtimeScene.isLoaded)
        {
            runtimeScene = SceneManager.CreateScene(sceneName);
            Log($"Created runtime scene '{sceneName}' for {DescribeStep(step)}.");
        }

        if (runtimeScene.IsValid() && runtimeScene.isLoaded)
        {
            SceneManager.SetActiveScene(runtimeScene);
            activeRuntimeFlowScene = runtimeScene;
            activeLevelSceneRoot = GetOrCreateRuntimeSceneRoot(runtimeScene);
            ApplyAtmosphere(step, activeLevelSceneRoot);
            ApplyRuntimeSceneRootToLoader(levelProfileLoader);
        }

        if (step.unloadPreviousRuntimeScene &&
            previousRuntimeScene.IsValid() &&
            previousRuntimeScene.isLoaded &&
            previousRuntimeScene != runtimeScene)
        {
            pendingRuntimeSceneToUnload = previousRuntimeScene;
        }

        return runtimeScene;
    }

    public void UnloadPreviousRuntimeSceneIfReady()
    {
        if (!pendingRuntimeSceneToUnload.IsValid() || !pendingRuntimeSceneToUnload.isLoaded)
            return;

        if (activeRuntimeFlowScene.IsValid() && pendingRuntimeSceneToUnload == activeRuntimeFlowScene)
            return;

        Log($"Unloading previous runtime scene '{pendingRuntimeSceneToUnload.name}'.");
        SceneManager.UnloadSceneAsync(pendingRuntimeSceneToUnload);
        pendingRuntimeSceneToUnload = default;
    }

    public IEnumerator UnloadPreviousRuntimeSceneBeforeGeneration()
    {
        if (!pendingRuntimeSceneToUnload.IsValid() || !pendingRuntimeSceneToUnload.isLoaded)
            yield break;

        if (activeRuntimeFlowScene.IsValid() && pendingRuntimeSceneToUnload == activeRuntimeFlowScene)
            yield break;

        Scene sceneToUnload = pendingRuntimeSceneToUnload;
        pendingRuntimeSceneToUnload = default;

        Log($"Unloading previous runtime scene '{sceneToUnload.name}' before generation.");
        AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(sceneToUnload);
        if (unloadOperation == null)
            yield break;

        while (!unloadOperation.isDone)
        {
            yield return null;
        }
    }

    private Transform GetOrCreateRuntimeSceneRoot(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root != null && root.name == LevelRuntimeRootName)
                return root.transform;
        }

        GameObject rootObject = new GameObject(LevelRuntimeRootName);
        SceneManager.MoveGameObjectToScene(rootObject, scene);
        return rootObject.transform;
    }

    private void ApplyRuntimeSceneRootToLoader(LevelProfileLoader loader)
    {
        if (loader == null || activeLevelSceneRoot == null)
            return;

        loader.SetRuntimeLevelRoot(activeLevelSceneRoot);

        StaticLevelLayoutBuilder builder = loader.GetComponent<StaticLevelLayoutBuilder>();
        if (builder != null)
        {
            builder.SetRuntimeLevelRoot(activeLevelSceneRoot);
        }
    }

    private void DisableDuplicateAudioListeners(Scene preferredScene)
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (listeners.Length <= 1)
            return;

        AudioListener keep = null;
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener != null && listener.enabled && listener.gameObject.scene == preferredScene)
            {
                keep = listener;
                break;
            }
        }

        if (keep == null)
        {
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].enabled)
                {
                    keep = listeners[i];
                    break;
                }
            }
        }

        int disabled = 0;
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null || listener == keep || !listener.enabled)
                continue;

            listener.enabled = false;
            disabled++;
        }

        if (disabled > 0)
        {
            Log($"Disabled {disabled} duplicate AudioListener(s) after loading '{preferredScene.name}'.");
        }
    }

    private static void ApplyRunSetupLevelIndex(LevelFlowStep step)
    {
        if (step == null || step.levelProfile == null)
            return;

        RunSetupData data = RunSetupData.EnsureInstance();
        data.levelIndex = Mathf.Max(1, step.levelProfile.levelIndex);
    }

    private void EnsureEditorFallbackFlow()
    {
        if (!useEditorFallbackFlow || editorFallbackSteps.Count > 0)
            return;

        LevelConfigProfile level1 = LoadLevelProfile(Level1ProfilePath);
        LevelConfigProfile level2 = LoadLevelProfile(Level2ProfilePath);
        editorFallbackSteps.Add(new LevelFlowStep
        {
            stepId = "tutorial",
            displayName = "Tutorial / LevelStart",
            stepType = LevelFlowStepType.Tutorial,
            sceneMode = LevelFlowSceneMode.RuntimeScene,
            sceneName = DefaultStartSceneName,
            runtimeSceneName = DefaultStartSceneName
        });
        editorFallbackSteps.Add(new LevelFlowStep
        {
            stepId = "level_01",
            displayName = "Level 1",
            stepType = LevelFlowStepType.PCG,
            sceneMode = LevelFlowSceneMode.RuntimeScene,
            sceneName = DefaultRunSceneName,
            runtimeSceneName = DefaultRunSceneName,
            levelProfile = level1
        });
        editorFallbackSteps.Add(new LevelFlowStep
        {
            stepId = "level_02",
            displayName = "Level 2",
            stepType = LevelFlowStepType.PCG,
            sceneMode = LevelFlowSceneMode.RuntimeScene,
            sceneName = DefaultRunSceneName,
            runtimeSceneName = DefaultRunSceneName,
            levelProfile = level2
        });
        editorFallbackSteps.Add(new LevelFlowStep
        {
            stepId = "boss",
            displayName = "Bossraum",
            stepType = LevelFlowStepType.Boss,
            sceneMode = LevelFlowSceneMode.RuntimeScene,
            sceneName = DefaultBossSceneName,
            runtimeSceneName = DefaultBossSceneName
        });
    }

    private void ResolveFlowConfig()
    {
        if (useInlineFlowSteps && inlineFlowSteps != null && inlineFlowSteps.Count > 0)
            return;

        if (flowConfig != null)
            return;

        flowConfig = ResolveFlowConfigForActiveScene();
        if (flowConfig != null)
            return;

        flowConfig = Resources.Load<LevelFlowConfig>(DefaultFlowConfigResourcesPath);
    }

    private void ApplyAtmosphere(LevelFlowStep step, Transform sceneRoot)
    {
        ResolveAtmosphereController();
        if (atmosphereController == null)
            return;

        atmosphereController.Apply(step != null ? step.atmosphereProfile : null, sceneRoot);
    }

    private void ResolveAtmosphereController()
    {
        if (atmosphereController != null)
            return;

        atmosphereController = GetComponent<LevelAtmosphereController>();
        if (atmosphereController != null)
            return;

        if (LevelSystemController.Instance != null && LevelSystemController.Instance.LevelAtmosphere != null)
        {
            atmosphereController = LevelSystemController.Instance.LevelAtmosphere;
            return;
        }

        Transform runtimeRoot = GetRuntimeRootTransform();
        if (runtimeRoot != null)
        {
            atmosphereController = runtimeRoot.GetComponentInChildren<LevelAtmosphereController>(true);
            if (atmosphereController != null)
                return;
        }

        atmosphereController = FindFirstObjectByType<LevelAtmosphereController>(FindObjectsInactive.Include);
    }

    private LevelProfileLoader ResolveLevelLoaderInRuntimeRoot()
    {
        Transform runtimeRoot = GetRuntimeRootTransform();
        if (runtimeRoot != null)
        {
            LevelProfileLoader loaderInRoot = runtimeRoot.GetComponentInChildren<LevelProfileLoader>(true);
            if (loaderInRoot != null)
                return loaderInRoot;
        }

        if (LevelSystemController.Instance != null && LevelSystemController.Instance.LevelLoader != null)
            return LevelSystemController.Instance.LevelLoader;

        LevelProfileLoader loaderOnSelf = GetComponent<LevelProfileLoader>();
        if (loaderOnSelf != null)
            return loaderOnSelf;

        return null;
    }

    private GameObject GetDuplicateDestroyTarget()
    {
        GameObject runtimeRoot = GetRuntimeRootGameObject();
        return runtimeRoot != null && runtimeRoot != gameObject ? runtimeRoot : gameObject;
    }

    private GameObject GetRuntimeRootGameObject()
    {
        Transform root = GetRuntimeRootTransform();
        return root != null ? root.gameObject : gameObject;
    }

    private Transform GetRuntimeRootTransform()
    {
        return transform.root != null ? transform.root : transform;
    }

    private bool WasStaticStepBuiltForScene(int stepIndex, string sceneName)
    {
        return lastBuiltStaticStepIndex == stepIndex
            && !string.IsNullOrWhiteSpace(sceneName)
            && string.Equals(lastBuiltStaticSceneName, sceneName, System.StringComparison.OrdinalIgnoreCase);
    }

    private void RememberStaticBuild(LevelFlowStep step, bool built)
    {
        if (!built || step == null || step.staticLayoutProfile == null)
            return;

        lastBuiltStaticStepIndex = currentStepIndex;
        lastBuiltStaticSceneName = SceneManager.GetActiveScene().name;
    }

    private LevelFlowConfig ResolveFlowConfigForActiveScene()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        if (string.IsNullOrWhiteSpace(activeSceneName))
            return null;

        LevelFlowConfig[] configs = Resources.LoadAll<LevelFlowConfig>(LevelFlowResourcesFolder);
        for (int i = 0; i < configs.Length; i++)
        {
            LevelFlowConfig candidate = configs[i];
            if (candidate == null || !ContainsScene(candidate, activeSceneName))
                continue;

            Log($"Using flow config '{candidate.name}' for active scene '{activeSceneName}'.");
            return candidate;
        }

        return null;
    }

    private static int FirstStepIndex(IReadOnlyList<LevelFlowStep> steps)
    {
        if (steps == null)
            return -1;

        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i] != null)
                return i;
        }

        return -1;
    }

    private static bool ContainsScene(LevelFlowConfig config, string sceneName)
    {
        if (config == null || config.steps == null || string.IsNullOrWhiteSpace(sceneName))
            return false;

        for (int i = 0; i < config.steps.Count; i++)
        {
            LevelFlowStep step = config.steps[i];
            if (step == null || string.IsNullOrWhiteSpace(ResolveTargetSceneName(step)))
                continue;

            if (string.Equals(ResolveTargetSceneName(step), sceneName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static LevelConfigProfile LoadLevelProfile(string path)
    {
#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<LevelConfigProfile>(path);
#else
        return null;
#endif
    }

    private static int FirstPlayableStepIndex(IReadOnlyList<LevelFlowStep> steps)
    {
        if (steps == null)
            return -1;

        for (int i = 0; i < steps.Count; i++)
        {
            LevelFlowStep step = steps[i];
            if (step == null) continue;
            if (step.stepType == LevelFlowStepType.PCG || step.stepType == LevelFlowStepType.Boss)
                return i;
        }

        return -1;
    }

    private static int NextStepIndex(IReadOnlyList<LevelFlowStep> steps, int currentIndex)
    {
        if (steps == null)
            return -1;

        for (int i = Mathf.Max(0, currentIndex + 1); i < steps.Count; i++)
        {
            if (steps[i] != null)
                return i;
        }

        return -1;
    }

    private static int FindStepIndexForScene(IReadOnlyList<LevelFlowStep> steps, string sceneName)
    {
        if (steps == null || string.IsNullOrWhiteSpace(sceneName))
            return -1;

        for (int i = 0; i < steps.Count; i++)
        {
            LevelFlowStep step = steps[i];
            if (step == null || string.IsNullOrWhiteSpace(ResolveTargetSceneName(step)))
                continue;

            if (string.Equals(ResolveTargetSceneName(step), sceneName, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        string safeName = string.IsNullOrWhiteSpace(childName) ? "Runtime" : childName;
        Transform child = parent.Find(safeName);
        if (child != null)
            return child;

        GameObject childObject = new GameObject(safeName);
        child = childObject.transform;
        child.SetParent(parent, false);
        return child;
    }

    private static GameObject FindNamedRoot(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        GameObject[] objects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject candidate = objects[i];
            if (candidate != null && candidate.transform.parent == null && candidate.name == objectName)
                return candidate;
        }

        return null;
    }

    private static bool UsesRuntimeScene(LevelFlowStep step)
    {
        return step != null && step.sceneMode == LevelFlowSceneMode.RuntimeScene;
    }

    private static string ResolveTargetSceneName(LevelFlowStep step)
    {
        if (step == null)
            return null;

        if (UsesRuntimeScene(step) && !string.IsNullOrWhiteSpace(step.runtimeSceneName))
            return step.runtimeSceneName;

        if (!string.IsNullOrWhiteSpace(step.sceneName))
            return step.sceneName;

        switch (step.stepType)
        {
            case LevelFlowStepType.Tutorial:
                return DefaultStartSceneName;
            case LevelFlowStepType.PCG:
                return DefaultRunSceneName;
            case LevelFlowStepType.Boss:
                return DefaultBossSceneName;
            default:
                return null;
        }
    }

    private static string DescribeStep(LevelFlowStep step)
    {
        if (step == null) return "missing step";

        string profile = step.levelProfile != null ? step.levelProfile.DisplayName : "no profile";
        if (step.staticLayoutProfile != null) profile = step.staticLayoutProfile.name;
        string scene = ResolveTargetSceneName(step) ?? "no scene";
        string sceneMode = step.sceneMode.ToString();
        return $"{step.DisplayName} ({step.stepType}, {sceneMode}, Scene='{scene}', Profile='{profile}')";
    }

    private void Log(string message)
    {
        if (!logFlow) return;
        Debug.Log($"[Level Flow] {message}", this);
    }
}
