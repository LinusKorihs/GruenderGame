using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ItemSelectionUI : MonoBehaviour
{
    public static ItemSelectionUI Instance;

    public ItemCardUI[] cards;

    private void Awake()
    {
        Instance = this;
        gameObject.SetActive(false);
    }

    public void Open()
    {
        gameObject.SetActive(true);
        Time.timeScale = 0f;

        List<ItemData> items = ItemDatabase.Instance.GetRandomItems(3);

        for (int i = 0; i < cards.Length; i++)
        {
            if (i < items.Count)
            {
                cards[i].gameObject.SetActive(true);
                cards[i].Setup(items[i]);
            }
            else
            {
                cards[i].gameObject.SetActive(false);
            }
        }

        Button preferred = null;
        if (cards != null && cards.Length > 0)
        {
            int center = cards.Length / 2;
            if (cards[center] != null && cards[center].gameObject.activeInHierarchy)
                preferred = cards[center].button;
        }
        StartCoroutine(ControllerMenuNavigation.FocusNextFrame(transform, preferred));
    }

    public void SelectItem(ItemData item)
    {
        ItemDatabase.Instance.RemoveIfNeeded(item);

        Time.timeScale = 1f;
        gameObject.SetActive(false);
    }
}
