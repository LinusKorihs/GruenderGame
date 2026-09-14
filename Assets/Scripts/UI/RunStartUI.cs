using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class RunStartUI : MonoBehaviour
{
    public static RunStartUI Instance;

    public MinionRowUI rowA;
    public MinionRowUI rowB;
    public MinionRowUI rowC;

    public TMP_Text totalText;
    public int maxTotal = RunSetupData.DefaultMaxTotal;
    public string fallbackRunSceneName = "LevelStart";

    private void Awake()
    {
        Instance = this;
        gameObject.SetActive(false);
    }

    public void Open()
    {
        RunSetupData data = RunSetupData.EnsureInstance();
        maxTotal = Mathf.Clamp(data.maxTotal, 1, RunSetupData.DefaultMaxTotal);
        data.SetMinionCounts(data.typeA, data.typeB, data.typeC, maxTotal);

        gameObject.SetActive(true);
        Time.timeScale = 0f;

        rowA.Setup(this, data.typeA);
        rowB.Setup(this, data.typeB);
        rowC.Setup(this, data.typeC, RunSetupData.MaxSupportTotal);

        UpdateTotal();
    }

    public bool CanAdd()
    {
        return GetTotal() < maxTotal;
    }

    public void OnValueChanged()
    {
        UpdateTotal();
    }

    void UpdateTotal()
    {
        totalText.text = GetTotal() + "/" + maxTotal + "  Support " + rowC.GetValue() + "/" + RunSetupData.MaxSupportTotal;
        rowA.RefreshInteractable();
        rowB.RefreshInteractable();
        rowC.RefreshInteractable();
    }

    int GetTotal()
    {
        return rowA.GetValue() + rowB.GetValue() + rowC.GetValue();
    }

    public void StartRun()
    {
        RunSetupData data = RunSetupData.EnsureInstance();
        data.levelIndex = 1;
        data.SetMinionCounts(rowA.GetValue(), rowB.GetValue(), rowC.GetValue(), maxTotal);

        Time.timeScale = 1f;
        gameObject.SetActive(false);

        if (LevelStartRunFlowController.Instance != null)
        {
            LevelStartRunFlowController.Instance.StartSelectedRun(data.typeA, data.typeB, data.typeC);
            return;
        }

        SceneManager.LoadScene(fallbackRunSceneName);
    }
}
