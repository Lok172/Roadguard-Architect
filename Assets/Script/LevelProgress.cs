using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  LEVEL PROGRESS  (v3)
//
//  CHANGES vs v2:
//    • LevelResultPayload: totalBudgetSpent removed;
//      overallDeviceEffectiveness (float, 0–100) added.
//    • SubmitResult endpoint unchanged ("api/levelresult/submit").
//    • Everything else (unlock logic, daily snapshots, reset) unchanged.
// ─────────────────────────────────────────────────────────────────

public static class LevelProgress
{
    private const string KEY_PREFIX = "RoadguardArchitect_LevelCleared_";
    private const int MAX_TRACKED_LEVELS = 20;

    // ── Level unlock ─────────────────────────────────────────────

    public static bool IsLevelCleared(int level) =>
        PlayerPrefs.GetInt(KEY_PREFIX + level, 0) == 1;

    public static bool IsLevelUnlocked(int level)
    {
        if (level <= 1) return true;
        return IsLevelCleared(level - 1);
    }

    // ── Mark cleared (with full payload — primary path) ──────────

    /// <summary>
    /// Records local unlock AND submits the full result to the backend.
    /// Called by GameManager on VICTORY only — unlocks the next level.
    /// </summary>
    public static void MarkLevelCleared(int level, LevelResultPayload payload)
    {
        PlayerPrefs.SetInt(KEY_PREFIX + level, 1);
        PlayerPrefs.Save();

        if (ApiClient.Instance != null && UserSession.IsLoggedIn)
            CoroutineRunner.Instance.Run(SubmitResult(payload));
        else
            Debug.LogWarning("[LevelProgress] ApiClient or UserSession not ready — result not submitted.");
    }

    /// <summary>
    /// Submits the result to the backend WITHOUT marking the level cleared locally.
    /// Called by GameManager on GAME OVER — records stats but does NOT unlock next level.
    /// </summary>
    public static void SubmitResultOnly(int level, LevelResultPayload payload)
    {
        // Local progress intentionally NOT written — level stays locked.
        if (ApiClient.Instance != null && UserSession.IsLoggedIn)
            CoroutineRunner.Instance.Run(SubmitResult(payload));
        else
            Debug.LogWarning("[LevelProgress] ApiClient or UserSession not ready — result not submitted.");
    }

    // ── Mark cleared (backward-compat overload, no score data) ───

    public static void MarkLevelCleared(int level)
    {
        PlayerPrefs.SetInt(KEY_PREFIX + level, 1);
        PlayerPrefs.Save();
        Debug.LogWarning("[LevelProgress] MarkLevelCleared called without payload — score not submitted.");
    }

    // ── Daily accident rate snapshot ─────────────────────────────

    /// <summary>
    /// Called by GameManager each in-game day.
    /// Batches snapshots into the payload; all sent at once on end-game.
    /// </summary>
    public static void RecordDailyAccidentRate(LevelResultPayload payload, int day, int accidentRate)
    {
        if (payload == null) return;
        payload.accidentSnapshots.Add(new AccidentSnapshot { day = day, accidentRate = accidentRate });
    }

    // ── Server submission ─────────────────────────────────────────

    private static IEnumerator SubmitResult(LevelResultPayload payload)
    {
        yield return ApiClient.Instance.Post<SubmitResultResponse>(
            "api/levelresult/submit", payload,
            (response, error) =>
            {
                if (error != null)
                    Debug.LogError($"[LevelProgress] Failed to submit result: {error}");
                else
                    Debug.Log($"[LevelProgress] Result submitted — safetyScore={response.safetyScore}, rank={response.rank}");
            });
    }

    // ── Reset (local only) ───────────────────────────────────────

    public static void ResetProgress()
    {
        for (int i = 1; i <= MAX_TRACKED_LEVELS; i++)
            PlayerPrefs.DeleteKey(KEY_PREFIX + i);
        PlayerPrefs.Save();
    }
}

// ─────────────────────────────────────────────────────────────────
//  DATA STRUCTURES  (serialised to JSON for the Web API)
// ─────────────────────────────────────────────────────────────────

[System.Serializable]
public class AccidentSnapshot
{
    public int day;
    public int accidentRate;
}

[System.Serializable]
public class DeviceEffectivenessEntry
{
    public string deviceType;           // "StopSign", "SpeedBump", "TrafficLight"
    public int placedCount;
    public float effectivenessPercent; // 0–100  (correctPlaced / totalPlaced * 100)
}

[System.Serializable]
public class LevelResultPayload
{
    // Identity
    public int userId;
    public int level;

    // Outcome
    public int daysUsed;
    public int finalAccidentRate;
    public float finalHappiness;
    public int safetyScore;                       // 1–10 000, computed by GameManager

    // Overall device effectiveness (0–100), computed by GameManager on end-game.
    // = (Total Correct Placements ÷ Total Placements) × 100
    public float overallDeviceEffectiveness;

    // Time series (in-memory only, not sent to server)
    public System.Collections.Generic.List<AccidentSnapshot> accidentSnapshots = new();

    // Per-device breakdown (display only, not sent to server)
    public System.Collections.Generic.List<DeviceEffectivenessEntry> deviceEffectiveness = new();
}

[System.Serializable]
public class SubmitResultResponse
{
    public int safetyScore;
    public int rank;   // global leaderboard position
}