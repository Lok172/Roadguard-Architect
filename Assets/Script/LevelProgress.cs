using UnityEngine;

// Local level-unlock progress is tracked here using PlayerPrefs.

public static class LevelProgress
{
    private const string KEY_PREFIX = "RoadguardArchitect_LevelCleared_";
    private const int MAX_TRACKED_LEVELS = 20;

    public static bool IsLevelCleared(int level) =>
        PlayerPrefs.GetInt(KEY_PREFIX + level, 0) == 1;

    public static bool IsLevelUnlocked(int level)
    {
        if (level <= 1) return true;
        return IsLevelCleared(level - 1);
    }

    public static void MarkLevelCleared(int level)
    {
        PlayerPrefs.SetInt(KEY_PREFIX + level, 1);
        PlayerPrefs.Save();
    }

    public static void RecordDailyAccidentRate(LevelResultPayload payload, int day, int accidentRate)
    {
        if (payload == null) return;
        payload.accidentSnapshots.Add(new AccidentSnapshot { day = day, accidentRate = accidentRate });
    }

    public static void ResetProgress()
    {
        for (int i = 1; i <= MAX_TRACKED_LEVELS; i++)
            PlayerPrefs.DeleteKey(KEY_PREFIX + i);
        PlayerPrefs.Save();
    }
}

[System.Serializable]
public class AccidentSnapshot
{
    public int day;
    public int accidentRate;
}

[System.Serializable]
public class DeviceEffectivenessEntry
{
    public string deviceType;
    public int placedCount;
    public float effectivenessPercent;
}

[System.Serializable]
public class LevelResultPayload
{
    public int userId;
    public int level;

    public int daysUsed;
    public int finalAccidentRate;
    public float finalHappiness;
    public int safetyScore;

    public float overallDeviceEffectiveness;

    public System.Collections.Generic.List<AccidentSnapshot> accidentSnapshots = new();
    public System.Collections.Generic.List<DeviceEffectivenessEntry> deviceEffectiveness = new();
}

[System.Serializable]
public class SubmitResultResponse
{
    public int safetyScore;
    public int rank;
}
