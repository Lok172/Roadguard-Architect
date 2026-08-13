using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

// Leaderboard data is fetched from the backend here, including best score, player rank, and top-10 rankings.

public enum ConnectionStatus { Connecting, Connected, Disconnected }

public struct PlayerCardData
{
    public string playerName;
    public int playerId;
    public int? bestScore;
    public int? rank;
}

public struct RankingEntryData
{
    public int rank;
    public int playerId;
    public string playerName;
    public int safetyScore;
    public int daysUsed;
    public bool isCurrentPlayer;
}

public class SafetyRecordViewModel
{
    public event Action<ConnectionStatus> OnStatusChanged;
    public event Action<PlayerCardData> OnPlayerCardUpdated;
    public event Action<List<RankingEntryData>> OnLeaderboardUpdated;
    public event Action OnLeaderboardCleared;

    public int CurrentLevel { get; private set; } = 1;
    private int _playerId;
    private string _playerName;
    private bool _isFetching;

    [Serializable] private class BestScoreResponse { public int bestScore; }
    [Serializable] private class RankResponse { public int rank; }

    [Serializable]
    private class LeaderboardEntryDto
    {
        public int rank;
        public int playerId;
        public string playerName;
        public int safetyScore;
        public int daysUsed;
    }

    [Serializable]
    private class LeaderboardWrapper { public List<LeaderboardEntryDto> items; }

    public void Initialize()
    {
        if (!UserSession.IsLoggedIn)
            UserSession.TryLoadFromDisk();

        _playerId = UserSession.IsLoggedIn ? UserSession.CurrentUser.userId : 0;
        _playerName = UserSession.IsLoggedIn ? UserSession.CurrentUser.username : null;

        OnPlayerCardUpdated?.Invoke(new PlayerCardData
        {
            playerName = _playerName,
            playerId = _playerId,
            bestScore = null,
            rank = null
        });

        OnLeaderboardCleared?.Invoke();

        Refresh();
    }

    public void SetLevel(int dropdownIndex)
    {
        CurrentLevel = dropdownIndex + 1;
        if (!_isFetching)
            Refresh();
    }

    public void Refresh()
    {
        if (_isFetching) return;
        CoroutineRunner.Instance?.Run(FetchAll());
    }

    private IEnumerator FetchAll()
    {
        _isFetching = true;
        OnStatusChanged?.Invoke(ConnectionStatus.Connecting);
        OnLeaderboardCleared?.Invoke();

        int? bestScore = null;
        int? rank = null;
        bool anyError = false;

        yield return FetchBestScore(v => bestScore = v, ok => anyError |= !ok);
        yield return FetchPlayerRank(v => rank = v, ok => anyError |= !ok);

        OnPlayerCardUpdated?.Invoke(new PlayerCardData
        {
            playerName = _playerName,
            playerId = _playerId,
            bestScore = bestScore,
            rank = rank
        });

        yield return FetchTop10(ok => anyError |= !ok);

        OnStatusChanged?.Invoke(anyError ? ConnectionStatus.Disconnected : ConnectionStatus.Connected);

        if (anyError)
            OnLeaderboardCleared?.Invoke();

        _isFetching = false;
    }

    private IEnumerator FetchBestScore(Action<int?> setValue, Action<bool> done)
    {
        bool finished = false;
        bool success = false;

        yield return ApiClient.Instance.StartCoroutine(
            ApiClient.Instance.Get<BestScoreResponse>(
                $"api/leaderboard/player/{_playerId}/level/{CurrentLevel}",
                (resp, err) =>
                {
                    if (err != null)
                    {
                        Debug.LogWarning($"[SafetyRecordViewModel] BestScore error: {err}");
                        success = false;
                    }
                    else
                    {
                        if (resp != null && resp.bestScore > 0)
                            setValue(resp.bestScore);
                        success = true;
                    }
                    finished = true;
                }));

        yield return new WaitUntil(() => finished);
        done(success);
    }

    private IEnumerator FetchPlayerRank(Action<int?> setValue, Action<bool> done)
    {
        bool finished = false;
        bool success = false;

        yield return ApiClient.Instance.StartCoroutine(
            ApiClient.Instance.Get<RankResponse>(
                $"api/leaderboard/rank/{_playerId}/level/{CurrentLevel}",
                (resp, err) =>
                {
                    if (err != null)
                    {
                        Debug.LogWarning($"[SafetyRecordViewModel] PlayerRank error: {err}");
                        success = false;
                    }
                    else
                    {
                        if (resp != null && resp.rank > 0)
                            setValue(resp.rank);
                        success = true;
                    }
                    finished = true;
                }));

        yield return new WaitUntil(() => finished);
        done(success);
    }

    private IEnumerator FetchTop10(Action<bool> done)
    {
        bool finished = false;
        bool success = false;

        string url = $"{ApiClient.Instance.baseUrl.TrimEnd('/')}/api/leaderboard/top10/{CurrentLevel}";

        yield return ApiClient.Instance.StartCoroutine(
            GetWrappedArray(url, (entries, err) =>
            {
                if (err != null)
                {
                    Debug.LogWarning($"[SafetyRecordViewModel] Top10 error: {err}");
                    success = false;
                }
                else
                {
                    var viewData = new List<RankingEntryData>(entries.Count);
                    foreach (var e in entries)
                    {
                        viewData.Add(new RankingEntryData
                        {
                            rank = e.rank,
                            playerId = e.playerId,
                            playerName = e.playerName,
                            safetyScore = e.safetyScore,
                            daysUsed = e.daysUsed,
                            isCurrentPlayer = e.playerId == _playerId
                        });
                    }
                    OnLeaderboardUpdated?.Invoke(viewData);
                    success = true;
                }
                finished = true;
            }));

        yield return new WaitUntil(() => finished);
        done(success);
    }

    private IEnumerator GetWrappedArray(string url,
        Action<List<LeaderboardEntryDto>, string> callback)
    {
        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            callback(null, req.error);
            yield break;
        }

        string wrapped = $"{{\"items\":{req.downloadHandler.text}}}";
        var data = JsonUtility.FromJson<LeaderboardWrapper>(wrapped);
        callback(data?.items ?? new List<LeaderboardEntryDto>(), null);
    }
}
