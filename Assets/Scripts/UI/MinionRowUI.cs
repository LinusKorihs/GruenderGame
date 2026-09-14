using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MinionRowUI : MonoBehaviour
{
    public TMP_Text valueText;
    public Button plusButton;
    public Button minusButton;

    private int value = 0;
    private int maxValue = int.MaxValue;
    private RunStartUI manager;

    public void Setup(RunStartUI ui, int initialValue = 0, int maxValue = int.MaxValue)
    {
        manager = ui;
        this.maxValue = Mathf.Max(0, maxValue);
        value = Mathf.Clamp(initialValue, 0, this.maxValue);

        plusButton.onClick.RemoveListener(Add);
        minusButton.onClick.RemoveListener(Remove);
        plusButton.onClick.AddListener(Add);
        minusButton.onClick.AddListener(Remove);

        UpdateUI();
    }

    void Add()
    {
        if (manager.CanAdd() && value < maxValue)
        {
            value++;
            UpdateUI();
            manager.OnValueChanged();
        }
    }

    void Remove()
    {
        if (value > 0)
        {
            value--;
            UpdateUI();
            manager.OnValueChanged();
        }
    }

    void UpdateUI()
    {
        valueText.text = value.ToString();
        minusButton.interactable = value > 0;
        plusButton.interactable = manager == null || (manager.CanAdd() && value < maxValue);
    }

    public void RefreshInteractable()
    {
        UpdateUI();
    }

    public int GetValue()
    {
        return value;
    }

    public void SetValue(int newValue)
    {
        value = Mathf.Clamp(newValue, 0, maxValue);
        UpdateUI();
        manager?.OnValueChanged();
    }
}
