using UnityEngine;

public enum LevelProfileType
{
    PCG,
    Tutorial,
    Boss,
    Menu,
    End
}

[CreateAssetMenu(menuName = "SO/PCG/Profiles/Level Config Profile", fileName = "LevelConfigProfile")]
public class LevelConfigProfile : ScriptableObject
{
    [Header("Level Identity")]
    public string levelId = "level_01";
    public string levelName = "Level 1";
    [Min(1)] public int levelIndex = 1;
    public LevelProfileType levelType = LevelProfileType.PCG;

    [Header("PCG")]
    public PCGConfigProfile pcgConfigProfile;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(levelName)) return levelName;
            if (!string.IsNullOrWhiteSpace(levelId)) return levelId;
            return name;
        }
    }

    private void OnValidate()
    {
        levelIndex = Mathf.Max(1, levelIndex);
    }
}
