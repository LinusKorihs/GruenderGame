using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class PlayerKelpVisualInstaller : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private GameObject kelpPrefab;
    [SerializeField] private string spawnedVisualName = "PF_Kelp_Visual";
    [SerializeField] private Vector3 localPosition = Vector3.zero;
    [SerializeField] private Vector3 localEulerAngles = Vector3.zero;
    [SerializeField] private Vector3 localScale = Vector3.one;

    [Header("Placeholder")]
    [SerializeField] private bool disablePlaceholderRenderer = true;
    [SerializeField] private Renderer placeholderRenderer;

    public KelpAnimatorBridge Bridge { get; private set; }

    private CombatantStats stats;

    private void Awake()
    {
        InstallIfNeeded();
        SubscribeStats();
    }

    public void InstallIfNeeded()
    {
        if (visualRoot == null)
        {
            visualRoot = PlayerRootResolver.BodyTransform(gameObject);
            if (visualRoot == null) visualRoot = transform;
        }

        Bridge = FindExistingBridge();
        GameObject resolvedKelpPrefab = ResolveKelpPrefab();
        if (Bridge == null && resolvedKelpPrefab != null)
        {
            GameObject visual = Instantiate(resolvedKelpPrefab, visualRoot);
            visual.name = spawnedVisualName;
            visual.transform.localPosition = localPosition;
            visual.transform.localRotation = Quaternion.Euler(localEulerAngles);
            visual.transform.localScale = localScale;
            Bridge = visual.GetComponentInChildren<KelpAnimatorBridge>(true);
        }

        if (disablePlaceholderRenderer)
        {
            DisablePlaceholderRenderer();
        }

        RefreshDamageFlashRenderers();
    }

    private void SubscribeStats()
    {
        if (stats != null)
        {
            stats.Died -= HandleDied;
        }

        stats = GetComponentInChildren<CombatantStats>();
        if (stats != null)
        {
            stats.Died += HandleDied;
        }
    }

    private void OnDestroy()
    {
        if (stats != null)
        {
            stats.Died -= HandleDied;
        }
    }

    private void HandleDied()
    {
        Bridge?.SetDead(true);
    }

    private KelpAnimatorBridge FindExistingBridge()
    {
        if (visualRoot != null)
        {
            KelpAnimatorBridge bridge = visualRoot.GetComponentInChildren<KelpAnimatorBridge>(true);
            if (bridge != null) return bridge;
        }

        return GetComponentInChildren<KelpAnimatorBridge>(true);
    }

    private GameObject ResolveKelpPrefab()
    {
        if (kelpPrefab != null) return kelpPrefab;

        kelpPrefab = Resources.Load<GameObject>("PF_Kelp");
        if (kelpPrefab != null) return kelpPrefab;

        kelpPrefab = Resources.Load<GameObject>("Runtime/PF_Kelp");
        if (kelpPrefab != null) return kelpPrefab;

#if UNITY_EDITOR
        kelpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/PF_Kelp.prefab");
#endif
        return kelpPrefab;
    }

    private void DisablePlaceholderRenderer()
    {
        if (placeholderRenderer == null && visualRoot != null)
        {
            placeholderRenderer = visualRoot.GetComponent<Renderer>();
        }

        if (placeholderRenderer != null)
        {
            placeholderRenderer.enabled = false;
        }
    }

    private void RefreshDamageFlashRenderers()
    {
        DamageFlash[] flashes = GetComponentsInChildren<DamageFlash>(true);
        for (int i = 0; i < flashes.Length; i++)
        {
            if (flashes[i] != null)
            {
                flashes[i].RefreshRenderers();
            }
        }
    }
}
