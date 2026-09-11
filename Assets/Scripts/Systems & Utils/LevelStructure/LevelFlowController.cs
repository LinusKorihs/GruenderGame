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
    private const string LevelLoaderName = "LevelLoader";
    private const string LevelLoaderPrefabPath = "Assets/Prefabs/Systems/PF_LevelLoader.prefab";
    private const string Level1ProfilePath = "Assets/ScriptableObjects/PCG/Profiles/Level/SO_Level1.asset";
    private const string Level2ProfilePath = "Assets/ScriptableObjects/PCG/Profiles/Level/SO_Level2.asset";

    public static LevelFlowController Instance { get; private set; }

    [SerializeField] private LevelFlowConfig flowConfig;
    [SerializeField] private bool useEditorFallbackFlow = true;
    [SerializeField] private bool logFlow = true;

    [Header("Level Loader")]
    [SerializeField] private LevelProfileLoader levelLoaderPrefab;
    [SerializeField] private bool instantiateLevelLoaderWhenMissing = true;

    private readonly List<LevelFlowStep> editorFallbackSteps = new List<LevelFlowStep>();
    private LevelProfileLoader levelProfileLoader;
    private StaticLevelLayoutBuilder staticLayoutBuilder;
    private int currentStepIndex = -1;
    private bool runActive;

    public LevelFlowStep CurrentStep
    {
        get
        {
            IReadOnlyList<LevelFlowStep> steps = Steps;
            return currentStepIndex >= 0 && currentStepIndex < steps.Count ? steps[currentStepIndex] : null;
        }
    }

    private IReadOnlyList<LevelFlowStep> Steps
    {
        get
        {
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
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;

        if (levelProfileLoader == null)
        {
            levelProfileLoader = GetComponent<LevelProfileLoader>();
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

        return BuildStaticLayout(step);
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

        loader.ConfigureTargets(assembler, contentSpawner, FindFirstDirectionalLightInScene());
        bool applied = loader.ApplyLevelProfile(step.levelProfile);
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
            return levelProfileLoader;

        levelProfileLoader = GetComponentInChildren<LevelProfileLoader>(true);
        if (levelProfileLoader != null)
            return levelProfileLoader;

        levelProfileLoader = FindFirstObjectByType<LevelProfileLoader>();
        if (levelProfileLoader != null)
            return levelProfileLoader;

        LevelProfileLoader prefab = ResolveLevelLoaderPrefab();
        if (instantiateLevelLoaderWhenMissing && prefab != null)
        {
            levelProfileLoader = Instantiate(prefab);
            levelProfileLoader.name = LevelLoaderName;
            DontDestroyOnLoad(levelProfileLoader.gameObject);
            levelProfileLoader.ConfigureTargets(assembler, contentSpawner, FindFirstDirectionalLightInScene());
            return levelProfileLoader;
        }

        GameObject host = GameObject.Find(LevelLoaderName);
        if (host == null)
            host = new GameObject(LevelLoaderName);

        levelProfileLoader = host.AddComponent<LevelProfileLoader>();
        levelProfileLoader.ConfigureTargets(assembler, contentSpawner, FindFirstDirectionalLightInScene());
        return levelProfileLoader;
    }

    public bool IsRuntimeLevelLoaderRoot(GameObject target)
    {
        return target != null && levelProfileLoader != null && target == levelProfileLoader.gameObject;
    }

    private bool BuildStaticLayout(LevelFlowStep step)
    {
        StaticLevelLayoutBuilder builder = EnsureStaticLayoutBuilder();
        if (builder == null)
        {
            Debug.LogWarning("[Level Flow] Cannot build static layout because no StaticLevelLayoutBuilder is available.", this);
            return false;
        }

        bool built = builder.Build(step.staticLayoutProfile);
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

        return staticLayoutBuilder;
    }

    private LevelProfileLoader ResolveLevelLoaderPrefab()
    {
        if (levelLoaderPrefab != null)
            return levelLoaderPrefab;

        ResolveFlowConfig();
        if (flowConfig != null && flowConfig.levelLoaderPrefab != null)
        {
            levelLoaderPrefab = flowConfig.levelLoaderPrefab;
            return levelLoaderPrefab;
        }

#if UNITY_EDITOR
        levelLoaderPrefab = AssetDatabase.LoadAssetAtPath<LevelProfileLoader>(LevelLoaderPrefabPath);
#endif
        return levelLoaderPrefab;
    }

    private bool LoadFlowScene(LevelFlowStep step)
    {
        string sceneName = string.IsNullOrWhiteSpace(step.sceneName) ? DefaultBossSceneName : step.sceneName;
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
        LevelFlowStep step = CurrentStep;
        if (step == null || step.stepType == LevelFlowStepType.PCG)
            return;

        if (!string.Equals(scene.name, step.sceneName, System.StringComparison.OrdinalIgnoreCase))
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
            sceneName = DefaultStartSceneName
        });
        editorFallbackSteps.Add(new LevelFlowStep
        {
            stepId = "level_01",
            displayName = "Level 1",
            stepType = LevelFlowStepType.PCG,
            sceneName = DefaultRunSceneName,
            levelProfile = level1
        });
        editorFallbackSteps.Add(new LevelFlowStep
        {
            stepId = "level_02",
            displayName = "Level 2",
            stepType = LevelFlowStepType.PCG,
            sceneName = DefaultRunSceneName,
            levelProfile = level2
        });
        editorFallbackSteps.Add(new LevelFlowStep
        {
            stepId = "boss",
            displayName = "Bossraum",
            stepType = LevelFlowStepType.Boss,
            sceneName = DefaultBossSceneName
        });
    }

    private void ResolveFlowConfig()
    {
        if (flowConfig != null)
            return;

        flowConfig = Resources.Load<LevelFlowConfig>(DefaultFlowConfigResourcesPath);
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
            if (step == null || string.IsNullOrWhiteSpace(step.sceneName))
                continue;

            if (string.Equals(step.sceneName, sceneName, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static Light FindFirstDirectionalLightInScene()
    {
        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }

    private static string DescribeStep(LevelFlowStep step)
    {
        if (step == null) return "missing step";

        string profile = step.levelProfile != null ? step.levelProfile.DisplayName : "no profile";
        if (step.staticLayoutProfile != null) profile = step.staticLayoutProfile.name;
        string scene = string.IsNullOrWhiteSpace(step.sceneName) ? "no scene" : step.sceneName;
        return $"{step.DisplayName} ({step.stepType}, Scene='{scene}', Profile='{profile}')";
    }

    private void Log(string message)
    {
        if (!logFlow) return;
        Debug.Log($"[Level Flow] {message}", this);
    }
}
