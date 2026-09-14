using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MinionTypeSelectorHUD : MonoBehaviour
{
    [System.Serializable]
    private class Slot
    {
        public Image icon;
        public Image tint;
    }

    [Header("References")]
    [SerializeField] private PlayerMinionCommander commander;
    [SerializeField] private Slot leftSlot = new Slot();
    [SerializeField] private Slot centerSlot = new Slot();
    [SerializeField] private Slot rightSlot = new Slot();
    [SerializeField] private TMP_Text countText;

    [Header("Role Sprites")]
    [SerializeField] private Sprite meleeSprite;
    [SerializeField] private Sprite rangedSprite;
    [SerializeField] private Sprite supportSprite;

    [Header("Tint")]
    [SerializeField] private Color sideTintColor = Color.black;
    [SerializeField] private Color unavailableTintColor = new Color(1f, 0.16f, 0.08f, 1f);
    [SerializeField, Range(0f, 1f)] private float sideTintAlpha = 0.18f;
    [SerializeField, Range(0f, 1f)] private float unavailableTintAlpha = 0.9f;
    [SerializeField, Range(0f, 1f)] private float emptyCenterTintAlpha = 0.78f;
    [SerializeField, Range(0f, 1f)] private float flashTintAlpha = 1f;
    [SerializeField] private float emptyFlashDuration = 0.28f;

    private float flashUntil;
    private MinionRoleType flashRole;
    private bool hasFlashRole;

    private void Awake()
    {
        ResolveSlotReferences();
        CaptureDefaultSprites();
    }

    private void OnEnable()
    {
        Bind(commander != null ? commander : FindFirstObjectByType<PlayerMinionCommander>());
    }

    private void OnDisable()
    {
        if (commander == null) return;

        commander.MinionSelectionChanged -= Refresh;
        commander.EmptyMinionSelectionRequested -= OnEmptySelectionRequested;
    }

    private void Update()
    {
        if (flashUntil <= 0f) return;

        if (Time.unscaledTime >= flashUntil)
        {
            flashUntil = 0f;
            hasFlashRole = false;
        }

        Refresh();
    }

    public void Bind(PlayerMinionCommander newCommander)
    {
        if (commander == newCommander)
        {
            Refresh();
            return;
        }

        if (commander != null)
        {
            commander.MinionSelectionChanged -= Refresh;
            commander.EmptyMinionSelectionRequested -= OnEmptySelectionRequested;
        }

        commander = newCommander;

        if (commander != null)
        {
            commander.MinionSelectionChanged += Refresh;
            commander.EmptyMinionSelectionRequested += OnEmptySelectionRequested;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (commander == null)
        {
            SetSlot(leftSlot, MinionRoleType.Support, false, true);
            SetSlot(centerSlot, MinionRoleType.Melee, true, true);
            SetSlot(rightSlot, MinionRoleType.Ranged, false, true);
            if (countText != null) countText.text = "0 / 0";
            return;
        }

        MinionRoleType center = commander.SelectedRole;
        MinionRoleType left = commander.GetAdjacentRole(-1);
        MinionRoleType right = commander.GetAdjacentRole(1);

        SetSlot(leftSlot, left, false, commander.GetLiveCount(left) <= 0);
        SetSlot(centerSlot, center, true, commander.GetLiveCount(center) <= 0);
        SetSlot(rightSlot, right, false, commander.GetLiveCount(right) <= 0);

        int live = commander.GetLiveCount(center);
        int total = Mathf.Max(live, commander.GetKnownTotalCount(center));
        if (countText != null) countText.text = $"{live} / {total}";
        if (countText != null) countText.transform.SetAsLastSibling();
    }

    private void SetSlot(Slot slot, MinionRoleType role, bool isCenter, bool unavailable)
    {
        if (slot == null) return;

        if (slot.icon != null)
        {
            slot.icon.sprite = GetSprite(role);
            slot.icon.enabled = slot.icon.sprite != null;
        }

        if (slot.tint == null) return;

        PrepareTint(slot.tint);
        slot.tint.gameObject.SetActive(true);
        slot.tint.transform.SetAsLastSibling();

        bool flashing = hasFlashRole && role == flashRole && flashUntil > Time.unscaledTime;
        bool warning = unavailable || flashing;

        float alpha = isCenter ? 0f : sideTintAlpha;
        if (flashing) alpha = flashTintAlpha;
        else if (warning) alpha = isCenter ? emptyCenterTintAlpha : unavailableTintAlpha;

        Color color = warning ? unavailableTintColor : sideTintColor;
        color.a = alpha;
        slot.tint.color = color;
    }

    private static void PrepareTint(Image tint)
    {
        if (tint == null) return;

        tint.raycastTarget = false;
        tint.preserveAspect = false;

        RectTransform rect = tint.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private Sprite GetSprite(MinionRoleType role)
    {
        return role switch
        {
            MinionRoleType.Melee => meleeSprite,
            MinionRoleType.Ranged => rangedSprite,
            MinionRoleType.Support => supportSprite,
            _ => meleeSprite
        };
    }

    private void OnEmptySelectionRequested(MinionRoleType role)
    {
        flashRole = role;
        hasFlashRole = true;
        flashUntil = Time.unscaledTime + Mathf.Max(0.01f, emptyFlashDuration);
        Refresh();
    }

    private void ResolveSlotReferences()
    {
        ResolveSlot(leftSlot, "LeftSlot");
        ResolveSlot(centerSlot, "CenterSlot");
        ResolveSlot(rightSlot, "RightSlot");

        if (countText == null)
        {
            Transform count = transform.Find("CenterSlot/CountText");
            if (count != null) countText = count.GetComponent<TMP_Text>();
        }
    }

    private void ResolveSlot(Slot slot, string path)
    {
        if (slot == null) return;

        Transform root = transform.Find(path);
        if (root == null) return;

        if (slot.icon == null)
        {
            Transform icon = root.Find("MinionIcon");
            if (icon != null) slot.icon = icon.GetComponent<Image>();
        }

        if (slot.tint == null)
        {
            Transform tint = root.Find("Tint");
            if (tint != null) slot.tint = tint.GetComponent<Image>();
        }
    }

    private void CaptureDefaultSprites()
    {
        if (meleeSprite == null && centerSlot?.icon != null) meleeSprite = centerSlot.icon.sprite;
        if (rangedSprite == null && rightSlot?.icon != null) rangedSprite = rightSlot.icon.sprite;
        if (supportSprite == null && leftSlot?.icon != null) supportSprite = leftSlot.icon.sprite;
    }
}
