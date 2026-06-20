using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  LEVEL PROGRESS
//
//  Tiny static helper that tracks which levels have been cleared,
//  persisted via PlayerPrefs so progress survives between sessions.
//
//  GameManager calls MarkLevelCleared(currentLevel) on victory.
//  LevelSelectManager calls IsLevelUnlocked(level) to decide whether
//  each LevelPanel's Select button should be interactable.
// ─────────────────────────────────────────────────────────────────

public static class LevelProgress
{
    private const string KEY_PREFIX = "RoadguardArchitect_LevelCleared_";
    private const int MAX_TRACKED_LEVELS = 20; // safety bound for ResetProgress

    /// <summary>True once the given level has been won at least once.</summary>
    public static bool IsLevelCleared(int level)
    {
        return PlayerPrefs.GetInt(KEY_PREFIX + level, 0) == 1;
    }

    /// <summary>Call on victory to permanently record that a level is cleared.</summary>
    public static void MarkLevelCleared(int level)
    {
        PlayerPrefs.SetInt(KEY_PREFIX + level, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Level 1 is always unlocked. Level N (N &gt; 1) unlocks once level N-1
    /// has been cleared.
    /// </summary>
    public static bool IsLevelUnlocked(int level)
    {
        if (level <= 1) return true;
        return IsLevelCleared(level - 1);
    }

    /// <summary>Wipes all saved progress (used by the developer "Reset Progress" button).</summary>
    public static void ResetProgress()
    {
        for (int i = 1; i <= MAX_TRACKED_LEVELS; i++)
            PlayerPrefs.DeleteKey(KEY_PREFIX + i);
        PlayerPrefs.Save();
    }
}
