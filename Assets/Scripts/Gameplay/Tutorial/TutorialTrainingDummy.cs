using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CombatantStats))]
public sealed class TutorialTrainingDummy : MonoBehaviour
{
    [Header("Training")]
    [SerializeField, Min(100f)] private float bonusHealth = 100000f;
    [SerializeField, Min(0.1f)] private float feedbackVisibleDuration = 3f;

    [Header("Feedback")]
    [SerializeField] private Vector3 feedbackLocalPosition = new Vector3(1.8f, 1.75f, 0f);
    [SerializeField] private Vector2 feedbackSize = new Vector2(4f, 1.8f);
    [SerializeField, Min(0.001f)] private float feedbackScale = 0.01f;

    private const string HealthModifierId = "TutorialTrainingDummy.Health";
    private CombatantStats stats;
    private SupportSlowTarget slowTarget;
    private RectTransform feedbackRoot;
    private TMP_Text feedbackText;
    private Camera activeCamera;
    private float recentDamage;
    private float lastDamage;
    private float hideAt;
    private bool feedbackRequested;

    private void Awake()
    {
        stats = GetComponent<CombatantStats>();
        stats.AddPersistentModifier(HealthModifierId, new CombatStatModifierData
        {
            Type = CombatStatType.MaxHealth,
            Operation = StatModifierOperation.Add,
            Value = Mathf.Max(100f, bonusHealth)
        });
        stats.SetToFullHealth();

        slowTarget = GetComponent<SupportSlowTarget>();
        if (slowTarget == null)
            slowTarget = gameObject.AddComponent<SupportSlowTarget>();

        BuildFeedback();
        HideFeedback();
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
        float slow = slowTarget != null ? slowTarget.CurrentSlowFraction : 0f;
        if (slow > 0.001f)
        {
            hideAt = Time.time + feedbackVisibleDuration;
            feedbackRequested = true;
        }

        if (feedbackRequested)
        {
            RefreshFeedback(slow);
            if (Time.time >= hideAt && slow <= 0.001f)
            {
                recentDamage = 0f;
                feedbackRequested = false;
                HideFeedback();
            }
        }
    }

    private void LateUpdate()
    {
        if (feedbackRoot == null || !feedbackRequested)
            return;

        if (activeCamera == null)
            activeCamera = Camera.main;
        if (activeCamera != null)
        {
            feedbackRoot.rotation = activeCamera.transform.rotation;
            feedbackRoot.gameObject.SetActive(!IsOccluded(activeCamera));
        }
    }

    private void HandleDamageTaken(float damage)
    {
        lastDamage = damage;
        recentDamage += damage;
        hideAt = Time.time + feedbackVisibleDuration;
        feedbackRequested = true;

        if (feedbackRoot != null)
            feedbackRoot.gameObject.SetActive(true);

        RefreshFeedback(slowTarget != null ? slowTarget.CurrentSlowFraction : 0f);
        stats.SetToFullHealth();
    }

    private void RefreshFeedback(float slowFraction)
    {
        if (feedbackText == null)
            return;

        feedbackText.text =
            $"<color=#545B61>LAST HIT</color>   <b>{lastDamage:0.#}</b>\n" +
            $"<color=#545B61>RECENT DAMAGE</color>   <b>{recentDamage:0.#}</b>\n" +
            $"<color=#545B61>SLOW</color>   <b>{slowFraction * 100f:0}%</b>";
    }

    private void BuildFeedback()
    {
        GameObject canvasObject = new GameObject("Training Feedback", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        feedbackRoot = canvasObject.GetComponent<RectTransform>();
        feedbackRoot.localPosition = feedbackLocalPosition;
        feedbackRoot.localScale = Vector3.one * feedbackScale;
        feedbackRoot.sizeDelta = feedbackSize * 100f;

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 30;

        GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image), typeof(Outline));
        backgroundObject.transform.SetParent(feedbackRoot, false);
        RectTransform background = backgroundObject.GetComponent<RectTransform>();
        background.anchorMin = Vector2.zero;
        background.anchorMax = Vector2.one;
        background.offsetMin = Vector2.zero;
        background.offsetMax = Vector2.zero;
        Image backgroundImage = backgroundObject.GetComponent<Image>();
        backgroundImage.color = new Color(0.82f, 0.835f, 0.85f, 0.86f);
        backgroundImage.raycastTarget = false;
        Outline backgroundOutline = backgroundObject.GetComponent<Outline>();
        backgroundOutline.effectColor = new Color(0.12f, 0.14f, 0.16f, 0.32f);
        backgroundOutline.effectDistance = new Vector2(2f, -2f);
        backgroundOutline.useGraphicAlpha = true;

        GameObject textObject = new GameObject("Values", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(background, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 18f);
        textRect.offsetMax = new Vector2(-24f, -18f);

        feedbackText = textObject.GetComponent<TextMeshProUGUI>();
        feedbackText.alignment = TextAlignmentOptions.MidlineLeft;
        feedbackText.fontSize = 30f;
        feedbackText.lineSpacing = 10f;
        feedbackText.textWrappingMode = TextWrappingModes.NoWrap;
        feedbackText.color = new Color(0.025f, 0.03f, 0.035f, 1f);
        feedbackText.raycastTarget = false;
    }

    private void HideFeedback()
    {
        if (feedbackRoot != null)
            feedbackRoot.gameObject.SetActive(false);
    }

    private bool IsOccluded(Camera cameraToCheck)
    {
        Vector3 targetPosition = feedbackRoot.position;
        Vector3 origin = cameraToCheck.transform.position;
        Vector3 direction = targetPosition - origin;
        float distance = direction.magnitude;
        if (distance <= 0.01f) return false;

        RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hit = hits[i].transform;
            if (hit == null || hit == transform || hit.IsChildOf(transform)) continue;
            if (EnemyTargetUtility.FindTaggedActor(hit, "Player") != null) continue;
            return true;
        }
        return false;
    }
}
