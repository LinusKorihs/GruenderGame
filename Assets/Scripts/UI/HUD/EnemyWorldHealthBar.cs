using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class EnemyWorldHealthBar : MonoBehaviour
{
    public enum VisualStyle
    {
        Player,
        Boss
    }

    [SerializeField] private VisualStyle visualStyle = VisualStyle.Player;
    [SerializeField, Min(0.1f)] private float visibleDuration = 2.25f;
    [SerializeField] private float heightPadding = -0.2f;
    [SerializeField] private Vector2 worldSize = new Vector2(1.8f, 0.18f);
    [SerializeField] private Color backgroundColor = new Color(0.08f, 0.03f, 0.03f, 0.9f);
    [SerializeField] private Color fillColor = new Color(0.85f, 0.12f, 0.08f, 1f);

    private CombatantStats stats;
    private RectTransform canvasRect;
    private Image fill;
    private RectTransform fillRect;
    private Camera activeCamera;
    private float hideAt;

    public static void AttachIfNormalEnemy(CombatantStats target)
    {
        if (target == null || target.GetComponent<EnemyWorldHealthBar>() != null)
            return;
        if (target.GetComponent<TutorialTrainingDummy>() != null)
            return;
        if (!target.CompareTag("Enemy") || target.GetComponent<BossEncounterController>() != null)
            return;

        target.gameObject.AddComponent<EnemyWorldHealthBar>();
    }

    private void Awake()
    {
        stats = GetComponent<CombatantStats>();
        BuildBar();
    }

    private void OnEnable()
    {
        if (stats == null)
            stats = GetComponent<CombatantStats>();
        if (stats == null)
            return;

        stats.HealthChanged += HandleHealthChanged;
        stats.DamageTaken += HandleDamageTaken;
        stats.Died += HandleDied;
        Refresh(stats.CurrentHealth, stats.GetStat(CombatStatType.MaxHealth));
    }

    private void OnDisable()
    {
        if (stats == null)
            return;
        stats.HealthChanged -= HandleHealthChanged;
        stats.DamageTaken -= HandleDamageTaken;
        stats.Died -= HandleDied;
    }

    private void LateUpdate()
    {
        if (canvasRect == null)
            return;

        if (activeCamera == null)
            activeCamera = Camera.main;
        if (activeCamera != null)
            canvasRect.rotation = activeCamera.transform.rotation;

        if (canvasRect.gameObject.activeSelf && Time.time >= hideAt)
            canvasRect.gameObject.SetActive(false);
    }

    private void HandleHealthChanged(float current, float max)
    {
        Refresh(current, max);
    }

    private void HandleDamageTaken(float _)
    {
        if (canvasRect == null)
            return;
        canvasRect.gameObject.SetActive(true);
        hideAt = Time.time + visibleDuration;
    }

    private void HandleDied()
    {
        if (canvasRect != null)
            canvasRect.gameObject.SetActive(false);
    }

    private void Refresh(float current, float max)
    {
        float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        if (fill != null)
            fill.fillAmount = ratio;
        if (fillRect != null && fill.sprite == null)
            fillRect.anchorMax = new Vector2(0.04f + 0.92f * ratio, 0.82f);
    }

    private void BuildBar()
    {
        GameObject canvasObject = new GameObject("Enemy Health Bar", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.localPosition = Vector3.up * ResolveHeight();
        canvasRect.sizeDelta = visualStyle == VisualStyle.Player ? new Vector2(0.7f, 0.7f) : worldSize;

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;

        Color activeBackground = visualStyle == VisualStyle.Player
            ? new Color(0.02f, 0.12f, 0.08f, 0.92f)
            : backgroundColor;
        Color activeFill = visualStyle == VisualStyle.Player
            ? new Color(0.1f, 0.85f, 0.48f, 1f)
            : fillColor;

        GameObject backgroundObject = CreateImage("Background", canvasRect, activeBackground);
        Image background = backgroundObject.GetComponent<Image>();
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        GameObject fillObject = CreateImage("Fill", backgroundRect, activeFill);
        fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0.04f, 0.18f);
        fillRect.anchorMax = new Vector2(0.96f, 0.82f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fill = fillObject.GetComponent<Image>();
        Sprite fillSprite = FindLoadedSprite(visualStyle == VisualStyle.Player ? "Health full" : "Boss Bar full");
        Sprite emptySprite = FindLoadedSprite(visualStyle == VisualStyle.Player ? "Health empty" : "Boss Bar empty");
        if (fillSprite != null && emptySprite != null)
        {
            background.sprite = emptySprite;
            background.color = Color.white;
            background.preserveAspect = true;
            fill.sprite = fillSprite;
            fill.color = Color.white;
            fill.preserveAspect = true;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
        }

        fill.type = Image.Type.Filled;
        fill.fillMethod = visualStyle == VisualStyle.Player ? Image.FillMethod.Radial360 : Image.FillMethod.Horizontal;
        fill.fillOrigin = visualStyle == VisualStyle.Player
            ? (int)Image.Origin360.Top
            : (int)Image.OriginHorizontal.Left;

        canvasObject.SetActive(false);
    }

    private float ResolveHeight()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        float highest = transform.position.y + 1.5f;
        for (int i = 0; i < renderers.Length; i++)
            highest = Mathf.Max(highest, renderers[i].bounds.max.y);
        return highest - transform.position.y + heightPadding;
    }

    private static GameObject CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        imageObject.GetComponent<Image>().color = color;
        return imageObject;
    }

    private static Sprite FindLoadedSprite(string spriteName)
    {
        Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        for (int i = 0; i < sprites.Length; i++)
        {
            if (string.Equals(sprites[i].name, spriteName, System.StringComparison.OrdinalIgnoreCase))
                return sprites[i];
        }
        return null;
    }
}
