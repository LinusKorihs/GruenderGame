using UnityEngine;

public sealed class LevelSystemController : MonoBehaviour
{
    public static LevelSystemController Instance { get; private set; }

    [Header("Optional Prefab Sources")]
    [SerializeField] private LevelFlowController levelFlowPrefab;
    [SerializeField] private LevelProfileLoader levelLoaderPrefab;

    [Header("Runtime Children")]
    [SerializeField] private LevelFlowController levelFlow;
    [SerializeField] private LevelProfileLoader levelLoader;
    [SerializeField] private bool instantiateMissingChildren = true;
    [SerializeField] private bool dontDestroyOnLoad = true;

    public LevelFlowController LevelFlow => levelFlow;
    public LevelProfileLoader LevelLoader => levelLoader;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);
        }

        ResolveChildren();
        ConfigureChildren();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [ContextMenu("Resolve Level System Children")]
    public void ResolveAndConfigure()
    {
        ResolveChildren();
        ConfigureChildren();
    }

    private void ResolveChildren()
    {
        if (levelFlow == null)
            levelFlow = GetComponentInChildren<LevelFlowController>(true);

        if (levelLoader == null)
            levelLoader = GetComponentInChildren<LevelProfileLoader>(true);

        if (!instantiateMissingChildren)
            return;

        if (levelLoader == null && levelLoaderPrefab != null)
        {
            levelLoader = Instantiate(levelLoaderPrefab, transform);
            levelLoader.name = "PF_LevelLoader";
        }

        if (levelFlow == null && levelFlowPrefab != null)
        {
            levelFlow = Instantiate(levelFlowPrefab, transform);
            levelFlow.name = "PF_LevelFlow";
        }
    }

    private void ConfigureChildren()
    {
        if (levelFlow != null && levelLoader != null)
        {
            levelFlow.ConfigureLevelLoader(levelLoader);
        }
    }
}
