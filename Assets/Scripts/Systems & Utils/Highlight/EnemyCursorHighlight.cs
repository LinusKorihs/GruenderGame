using UnityEngine;

public class EnemyCursorHighlight : MonoBehaviour, ICursorHighlight
{
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private bool autoRefreshRenderers = true;

    [Header("Highlight Look")]
    [SerializeField] private Color highlightColor = new Color(1f, 1f, 1f, 1f); // can be overridden per enemy prefab
    [SerializeField, Range(0f, 8f)] private float intensity = 0.35f;
    [SerializeField] private bool addToExistingEmission = true;

    [Header("Damage Feedback")]
    [SerializeField] private Color damageColor = new Color(1f, 0.05f, 0.05f, 1f);
    [SerializeField, Range(0f, 8f)] private float damageIntensity = 0.8f;
    [SerializeField, Min(0.01f)] private float damageDuration = 0.14f;

    private MaterialPropertyBlock mpb;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    // Cached ORIGINAL emission per renderer per sub-material
    private Color[][] originalEmission;

    private bool isHighlighted;
    private float damageTimer;
    private CombatantStats stats;
    public bool IsHighlighted => isHighlighted;

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (autoRefreshRenderers || renderers == null || renderers.Length == 0)
            RefreshRenderers(reapplyHighlight: false);
        else
            CacheOriginalEmission();

        stats = GetComponentInParent<CombatantStats>() ?? GetComponentInChildren<CombatantStats>();
    }

    private void OnEnable()
    {
        if (stats != null)
            stats.DamageTaken += HandleDamageTaken;
    }

    private void OnDisable()
    {
        if (stats != null)
            stats.DamageTaken -= HandleDamageTaken;
    }

    private void Update()
    {
        if (damageTimer <= 0f)
            return;

        damageTimer = Mathf.Max(0f, damageTimer - Time.deltaTime);
        ApplyHighlight();
    }

    private void HandleDamageTaken(float _)
    {
        damageTimer = damageDuration;
        ApplyHighlight();
    }

    public void RefreshRenderers()
    {
        RefreshRenderers(reapplyHighlight: true);
    }

    private void RefreshRenderers(bool reapplyHighlight)
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        CacheOriginalEmission();

        if (reapplyHighlight && isHighlighted)
            ApplyHighlight();
    }

    private void CacheOriginalEmission()
    {
        if (mpb == null)
            mpb = new MaterialPropertyBlock();

        if (renderers == null)
            renderers = GetComponentsInChildren<Renderer>(true);

        originalEmission = new Color[renderers.Length][];

        for (int r = 0; r < renderers.Length; r++)
        {
            var ren = renderers[r];
            if (!ren) continue;

            var mats = ren.materials; // instances ok for enemies
            originalEmission[r] = new Color[mats.Length];

            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (!mat) continue;

                // Make sure emission is enabled for URP Lit etc.
                mat.EnableKeyword("_EMISSION");

                // Cache material default emission (NOT from property block)
                if (mat.HasProperty(EmissionColor)) originalEmission[r][m] = mat.GetColor(EmissionColor);
                else originalEmission[r][m] = Color.black;
            }
        }
    }

    public void SetHighlighted(bool on)
    {
        isHighlighted = on;

        if (renderers == null || renderers.Length == 0 || originalEmission == null || originalEmission.Length != renderers.Length)
            RefreshRenderers(reapplyHighlight: false);

        ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        for (int r = 0; r < renderers.Length; r++)
        {
            var ren = renderers[r];
            if (!ren) continue;

            int matCount = ren.sharedMaterials != null ? ren.sharedMaterials.Length : 1;

            for (int m = 0; m < matCount; m++)
            {
                // Use per-material blocks (same as DamageFlash) so both systems write to the
                // same layer and the last writer wins cleanly.
                ren.GetPropertyBlock(mpb, m);

                Color baseE = (originalEmission[r] != null && m < originalEmission[r].Length) ? originalEmission[r][m] : Color.black;
                Color cursorEmission = isHighlighted ? highlightColor * intensity : Color.black;
                float damage01 = damageDuration > 0f ? Mathf.Clamp01(damageTimer / damageDuration) : 0f;
                Color damageEmission = damageColor * (damageIntensity * damage01);
                Color effectEmission = cursorEmission + damageEmission;
                Color final = (isHighlighted || damage01 > 0f) && !addToExistingEmission
                    ? effectEmission
                    : baseE + effectEmission;

                mpb.SetColor(EmissionColor, final);
                ren.SetPropertyBlock(mpb, m);
            }
        }
    }
}
