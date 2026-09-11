using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MinionRowUI : MonoBehaviour
{
    public TMP_Text valueText;
    public Button plusButton;
    public Button minusButton;

    private int value = 0;
    private RunStartUI manager;

    public void Setup(RunStartUI ui, int initialValue = 0)
    {
        manager = ui;
        value = Mathf.Max(0, initialValue);

        plusButton.onClick.RemoveListener(Add);
        minusButton.onClick.RemoveListener(Remove);
        plusButton.onClick.AddListener(Add);
        minusButton.onClick.AddListener(Remove);

        UpdateUI();
    }

    void Add()
    {
        if (manager.CanAdd())
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
    }

    public int GetValue()
    {
        return value;
    }

    public void SetValue(int newValue)
    {
        value = Mathf.Max(0, newValue);
        UpdateUI();
        manager?.OnValueChanged();
    }
}
