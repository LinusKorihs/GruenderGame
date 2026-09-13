using UnityEngine;
using UnityEngine.UI;

public sealed class BossHealthHUD : MonoBehaviour
{
    [SerializeField] private CombatantStats bossStats;
    [SerializeField] private Image healthFill;
    [SerializeField] private bool hideWhenUnbound = true;

    private void Awake()
    {
        ResolveReferences();
        Bind(bossStats);
    }

    private void OnDisable()
    {
        if (bossStats != null)
        {
            bossStats.HealthChanged -= HandleHealthChanged;
        }
    }

    public void Bind(CombatantStats stats)
    {
        ResolveReferences();

        if (bossStats == stats)
        {
            Refresh();
            return;
        }

        if (bossStats != null)
        {
            bossStats.HealthChanged -= HandleHealthChanged;
        }

        bossStats = stats;

        if (bossStats != null)
        {
            bossStats.HealthChanged += HandleHealthChanged;
        }

        Refresh();
    }

    private void HandleHealthChanged(float current, float max)
    {
        SetHealth(current, max);
    }

    private void Refresh()
    {
        bool hasBoss = bossStats != null;
        if (hideWhenUnbound && gameObject.activeSelf != hasBoss)
        {
            gameObject.SetActive(hasBoss);
        }

        if (!hasBoss)
        {
            SetHealth(0f, 1f);
            return;
        }

        SetHealth(bossStats.CurrentHealth, bossStats.GetStat(CombatStatType.MaxHealth));
    }

    private void SetHealth(float current, float max)
    {
        if (healthFill == null) return;

        float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        healthFill.fillAmount = ratio;
    }

    private void ResolveReferences()
    {
        if (healthFill != null) return;

        Transform fill = transform.Find("BossBarFull");
        if (fill != null) healthFill = fill.GetComponent<Image>();
        if (healthFill == null) healthFill = GetComponentInChildren<Image>(true);
    }
}
