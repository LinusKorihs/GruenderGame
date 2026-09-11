using UnityEngine;

public class RunSetupData : MonoBehaviour
{
    public static RunSetupData Instance;

    public const int DefaultMaxTotal = 20;

    public int typeA;
    public int typeB;
    public int typeC;

    public int maxTotal = DefaultMaxTotal;
    public int levelIndex = 1;

    public int TotalMinions => Mathf.Max(0, typeA) + Mathf.Max(0, typeB) + Mathf.Max(0, typeC);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        maxTotal = Mathf.Max(1, maxTotal);
        levelIndex = Mathf.Max(1, levelIndex);
    }

    public static RunSetupData EnsureInstance()
    {
        if (Instance != null) return Instance;

        GameObject go = new GameObject(nameof(RunSetupData));
        return go.AddComponent<RunSetupData>();
    }

    public void SetMinionCounts(int melee, int ranged, int support, int max)
    {
        maxTotal = Mathf.Max(1, max);

        typeA = Mathf.Max(0, melee);
        typeB = Mathf.Max(0, ranged);
        typeC = Mathf.Max(0, support);

        int total = TotalMinions;
        if (total <= maxTotal) return;

        int overflow = total - maxTotal;
        TrimCount(ref typeC, ref overflow);
        TrimCount(ref typeB, ref overflow);
        TrimCount(ref typeA, ref overflow);
    }

    public void ResetRun(int melee = 1, int ranged = 1, int support = 1)
    {
        levelIndex = 1;
        SetMinionCounts(melee, ranged, support, DefaultMaxTotal);
    }

    private static void TrimCount(ref int value, ref int overflow)
    {
        if (overflow <= 0) return;

        int removed = Mathf.Min(value, overflow);
        value -= removed;
        overflow -= removed;
    }
}
