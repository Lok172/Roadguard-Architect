using System.Collections;
using UnityEngine;

// The finished level's result payload is retrieved here and submitted to the backend when appropriate.

public class LevelResultsViewModel
{
    public event System.Action<string> OnUploadFailed;
    public event System.Action<int> OnUploadSucceeded;

    public LevelResultPayload GetPayload() => LastLevelResult.Payload;

    public bool ShouldUploadResult()
    {
        if (GameManager.Instance != null && GameManager.Instance.devMode) return false;

        var lsm = Object.FindFirstObjectByType<LevelSelectManager>();
        return lsm == null || !lsm.developerMode;
    }

    public void SubmitIfAppropriate(LevelResultPayload payload)
    {
        if (payload == null) return;
        if (!ShouldUploadResult()) return;
        if (ApiClient.Instance == null || !UserSession.IsLoggedIn) return;

        CoroutineRunner.Instance?.Run(UploadLevelResult(payload));
    }

    private IEnumerator UploadLevelResult(LevelResultPayload p)
    {
        var body = new LevelResultRequest
        {
            playerId = p.userId,
            levelNumber = p.level,
            safetyScore = p.safetyScore,
            daysUsed = p.daysUsed
        };

        yield return ApiClient.Instance.Post<LevelResultResponse>(
            "api/results", body,
            (response, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"[LevelResultsViewModel] Upload failed: {error}");
                    OnUploadFailed?.Invoke(error);
                }
                else
                {
                    Debug.Log($"[LevelResultsViewModel] Uploaded. Server id: {response?.id}");
                    OnUploadSucceeded?.Invoke(response?.id ?? 0);
                }
            });
    }
}

[System.Serializable]
public class LevelResultRequest
{
    public int playerId;
    public int levelNumber;
    public int safetyScore;
    public int daysUsed;
}

[System.Serializable]
public class LevelResultResponse { public int id; }

[System.Serializable]
public class PersonalRecordResponse { public int highestScore, rank; }
