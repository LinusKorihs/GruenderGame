using UnityEngine;

[DisallowMultipleComponent]
public sealed class TutorialMinionCountDisplay : MonoBehaviour
{
    [SerializeField] private TutorialMinionSpawner spawner;
    [SerializeField] private TextMesh totalText;
    [SerializeField] private TextMesh typeCountsText;

    private int lastMelee = -1;
    private int lastRanged = -1;
    private int lastSupport = -1;
    private int lastLimit = -1;
    private Renderer[] displayRenderers;
    private Camera activeCamera;

    private void Awake()
    {
        ResolveReferences();
        displayRenderers = GetComponentsInChildren<Renderer>(true);
        Refresh(force: true);
    }

    private void Update()
    {
        if (spawner == null)
            ResolveReferences();
        Refresh(force: false);
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (activeCamera == null)
            activeCamera = Camera.main;
        if (activeCamera == null || displayRenderers == null) return;

        Vector3 origin = activeCamera.transform.position;
        Vector3 target = transform.position;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;
        bool occluded = false;

        if (distance > 0.01f)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hit = hits[i].transform;
                if (hit == null || hit == transform || hit.IsChildOf(transform)) continue;
                if (EnemyTargetUtility.FindTaggedActor(hit, "Player") != null) continue;
                occluded = true;
                break;
            }
        }

        for (int i = 0; i < displayRenderers.Length; i++)
        {
            if (displayRenderers[i] != null)
                displayRenderers[i].forceRenderingOff = occluded;
        }
    }

    private void ResolveReferences()
    {
        if (spawner == null) spawner = GetComponentInParent<TutorialMinionSpawner>();
        if (spawner == null) spawner = FindFirstObjectByType<TutorialMinionSpawner>();

        TextMesh[] labels = GetComponentsInChildren<TextMesh>(true);
        if (totalText == null && labels.Length > 0) totalText = labels[0];
        if (typeCountsText == null && labels.Length > 1) typeCountsText = labels[1];
    }

    private void Refresh(bool force)
    {
        if (spawner == null || totalText == null || typeCountsText == null) return;

        spawner.GetActiveCounts(out int melee, out int ranged, out int support);
        int limit = spawner.MaxTutorialMinions;
        if (!force && melee == lastMelee && ranged == lastRanged && support == lastSupport && limit == lastLimit)
            return;

        lastMelee = melee;
        lastRanged = ranged;
        lastSupport = support;
        lastLimit = limit;

        totalText.text = $"TOTAL  {melee + ranged + support} / {limit}";
        typeCountsText.text = $"MELEE  {melee}     RANGED  {ranged}     SUPPORT  {support}";
    }
}
