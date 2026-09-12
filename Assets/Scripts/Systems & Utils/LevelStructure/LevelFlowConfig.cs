using System;
using System.Collections.Generic;
using UnityEngine;

public enum LevelFlowStepType
{
    Tutorial,
    PCG,
    Boss,
    End
}

public enum LevelFlowSceneMode
{
    LoadScene,
    RuntimeScene
}

[Serializable]
public sealed class LevelFlowStep
{
    public string stepId = "level_01";
    public string displayName = "Level 1";
    public LevelFlowStepType stepType = LevelFlowStepType.PCG;

    [Header("Scene")]
    public LevelFlowSceneMode sceneMode = LevelFlowSceneMode.LoadScene;
    public string sceneName;
    public string runtimeSceneName;
    public bool unloadPreviousRuntimeScene = true;

    public LevelConfigProfile levelProfile;
    public StaticLevelLayoutProfile staticLayoutProfile;

    [Tooltip("Visual mood that should be applied when this flow step becomes active.")]
    public LevelAtmosphereProfile atmosphereProfile;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
            if (levelProfile != null) return levelProfile.DisplayName;
            if (!string.IsNullOrWhiteSpace(stepId)) return stepId;
            return stepType.ToString();
        }
    }
}

[CreateAssetMenu(menuName = "SO/Level Flow/Level Flow Config", fileName = "LevelFlowConfig")]
public sealed class LevelFlowConfig : ScriptableObject
{
    [Header("Flow")]
    public List<LevelFlowStep> steps = new List<LevelFlowStep>();

    public int FirstPlayableStepIndex()
    {
        for (int i = 0; i < steps.Count; i++)
        {
            LevelFlowStep step = steps[i];
            if (step == null) continue;
            if (step.stepType == LevelFlowStepType.PCG || step.stepType == LevelFlowStepType.Boss)
                return i;
        }

        return -1;
    }

    public int NextStepIndex(int currentIndex)
    {
        for (int i = Mathf.Max(0, currentIndex + 1); i < steps.Count; i++)
        {
            if (steps[i] != null)
                return i;
        }

        return -1;
    }
}
