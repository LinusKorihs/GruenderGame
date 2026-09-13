using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CombatantStats playerStats;
    [SerializeField] private Image healthFill;

    [Header("Colors")]
    [SerializeField] private bool tintByHealth = false;
    [SerializeField] private Color healthyColor = Color.white;
    [SerializeField] private Color woundedColor = new Color(1f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color criticalColor = new Color(1f, 0.25f, 0.2f, 1f);
    [SerializeField, Range(0f, 1f)] private float woundedThreshold = 0.5f;
    [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.25f;

    private void Awake()
    {
        if (healthFill == null)
        {
            Transform fill = transform.Find("HealthFull");
            if (fill != null) healthFill = fill.GetComponent<Image>();
        }
    }

    private void OnEnable()
    {
        Bind(playerStats != null ? playerStats : FindPlayerStats());
    }

    private void OnDisable()
    {
        if (playerStats != null)
        {
            playerStats.HealthChanged -= OnHealthChanged;
        }
    }

    public void Bind(CombatantStats stats)
    {
        if (playerStats == stats)
        {
            Refresh();
            return;
        }

        if (playerStats != null)
        {
            playerStats.HealthChanged -= OnHealthChanged;
        }

        playerStats = stats;

        if (playerStats != null)
        {
            playerStats.HealthChanged += OnHealthChanged;
        }

        Refresh();
    }

    private void OnHealthChanged(float current, float max)
    {
        SetHealth(current, max);
    }

    private void Refresh()
    {
        if (playerStats == null)
        {
            SetHealth(0f, 1f);
            return;
        }

        SetHealth(playerStats.CurrentHealth, playerStats.GetStat(CombatStatType.MaxHealth));
    }

    private void SetHealth(float current, float max)
    {
        if (healthFill == null) return;

        float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        healthFill.fillAmount = ratio;

        if (!tintByHealth)
        {
            healthFill.color = healthyColor;
            return;
        }

        if (ratio <= criticalThreshold)
        {
            healthFill.color = criticalColor;
        }
        else if (ratio <= woundedThreshold)
        {
            healthFill.color = woundedColor;
        }
        else
        {
            healthFill.color = healthyColor;
        }
    }

    private static CombatantStats FindPlayerStats()
    {
        GameObject player = PlayerRootResolver.FindAny();
        return player != null ? player.GetComponentInChildren<CombatantStats>(true) : null;
    }
}
