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

    private void Awake()
    {
        ResolveReferences();
        Refresh(force: true);
    }

    private void Update()
    {
        if (spawner == null)
            ResolveReferences();
        Refresh(force: false);
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
