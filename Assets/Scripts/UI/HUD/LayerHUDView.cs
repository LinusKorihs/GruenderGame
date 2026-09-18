using TMPro;
using UnityEngine;

public sealed class LayerHUDView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private string levelPrefix = "Layer";
    [SerializeField] private string tutorialText = "Tutorial";
    [SerializeField] private string bossText = "Boss";

    private void Awake()
    {
        ResolveReferences();
        Refresh();
    }

    private void OnEnable()
    {
        LevelStartRunFlowController.LevelTransitionStateChanged += HandleTransitionStateChanged;
        Refresh();
    }

    private void OnDisable()
    {
        LevelStartRunFlowController.LevelTransitionStateChanged -= HandleTransitionStateChanged;
    }

    private void HandleTransitionStateChanged(bool transitioning)
    {
        if (!transitioning)
            Refresh();
    }

    public void Refresh()
    {
        ResolveReferences();
        if (label == null) return;

        LevelFlowStep step = LevelFlowController.Instance != null ? LevelFlowController.Instance.CurrentStep : null;
        if (step != null)
        {
            if (step.stepType == LevelFlowStepType.Tutorial)
            {
                label.text = tutorialText;
                return;
            }

            if (step.stepType == LevelFlowStepType.Boss)
            {
                label.text = bossText;
                return;
            }
        }

        int profileIndex = step != null && step.levelProfile != null
            ? step.levelProfile.levelIndex
            : RunSetupData.Instance != null ? RunSetupData.Instance.levelIndex : 2;
        int levelIndex = Mathf.Max(1, profileIndex - 1);
        label.text = $"{levelPrefix} {levelIndex}";
    }

    private void ResolveReferences()
    {
        if (label != null) return;
        label = GetComponentInChildren<TMP_Text>(true);
    }
}
