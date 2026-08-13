using UnityEngine;

// Level unlock state is checked here, and the selected level is persisted.

public class LevelSelectViewModel
{
    public bool IsUnlocked(int level, bool developerModeOverride)
    {
        return developerModeOverride || LevelProgress.IsLevelUnlocked(level);
    }

    public void SelectLevel(int level)
    {
        PlayerPrefs.SetInt("CurrentLevel", level);
        PlayerPrefs.Save();
    }
}
