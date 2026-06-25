using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────
//  SAFETY RECORD MANAGER
//
//  Displays the Global Safety Records screen:
//    • Level dropdown  — switches which level's data is shown
//    • Refresh button  — re-fetches all data from the API
//    • Player card     — name, ID, personal best score, personal rank
//    • Top-10 table    — spawns / refreshes ranking row prefabs
//
//  Depends on: UserSession (profile), ApiClient (HTTP singleton)
//  NOTE: baseUrl is owned by ApiClient — do NOT duplicate it here.
// ─────────────────────────────────────────────────────────────────

public class SafetyRecordManager : MonoBehaviour
{
    // ── Level Dropdown ───────────────────────────────────────────
    [Header("Level Dropdown")]
    public TMP_Dropdown levelDropdown;

    // ── Refresh ──────────────────────────────────────────────────
    [Header("Refresh")]
    public Button refreshButton;
    public TMP_Text statusText;

    // ── Player Card ──────────────────────────────────────────────
    [Header("Player Card")]
    public TMP_Text playerNameText;
    public TMP_Text playerIdText;
    public TMP_Text bestScoreText;
    public TMP_Text playerRankText;

    // ── Ranking Table ────────────────────────────────────────────
    [Header("Ranking Table")]
    public Transform rankingParent;
    public GameObject rankingRowPrefab;

    // ── Colours ──────────────────────────────────────────────────
    private static readonly Color ColConnected = new Color(0x50 / 255f, 0xFF / 255f, 0x64 / 255f, 1f); // #50FF64
    private static readonly Color ColDisconnect = new Color(255f / 255f, 49f / 255f, 0f / 255f, 1f); // #FF3100
    private static readonly Color ColScoreFound = new Color(0x50 / 255f, 0xFF / 255f, 0x64 / 255f, 1f); // #50FF64
    private static readonly Color ColRankFound = new Color(0xFF / 255f, 0x68 / 255f, 0x4A / 255f, 1f); // #FF684A
    private static readonly Color ColRowDefault = new Color(0x31 / 255f, 0x41 / 255f, 0x58 / 255f, 1f); // #314158
    private static readonly Color ColPlayerRow = new Color(0x83 / 255f, 0xD7 / 255f, 0x6E / 255f, 1f); // #83D76E
    private static readonly Color ColBadgeGold = new Color(0xFF / 255f, 0xF4 / 255f, 0x00 / 255f, 1f); // #FFF400
    private static readonly Color ColBadgeSilver = new Color(0xCB / 255f, 0xCB / 255f, 0xCB / 255f, 1f); // #CBCBCB
    private static readonly Color ColBadgeBronze = new Color(0xFF / 255f, 0x9E / 255f, 0x00 / 255f, 1f); // #FF9E00

    // ── Internal state ───────────────────────────────────────────
    private int _currentLevel = 1;   // 1-based
    private int _playerId = 0;
    private bool _isFetching = false;

    // ─────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        if (!UserSession.IsLoggedIn)
            UserSession.TryLoadFromDisk();

        PopulatePlayerCard();
        SetupDropdown();
        ClearLeaderboard();   // show nothing until a successful fetch

        refreshButton.onClick.AddListener(OnRefreshClicked);

        StartCoroutine(FetchAll());
    }

    // ─────────────────────────────────────────────────────────────
    //  UI setup
    // ─────────────────────────────────────────────────────────────

    private void PopulatePlayerCard()
    {
        // Defaults in case session is missing
        SetNoRecord(playerNameText);
        SetNoRecord(playerIdText);
        SetNoRecord(bestScoreText);
        SetNoRecord(playerRankText);

        if (!UserSession.IsLoggedIn) return;

        var user = UserSession.CurrentUser;
        _playerId = user.userId;

        if (playerNameText != null) playerNameText.text = user.username;
        if (playerIdText != null && user.userId > 0) playerIdText.text = user.userId.ToString("D6");
        if(user.userId == 0) playerIdText.text = "No ID";
    }

    private void SetupDropdown()
    {
        if (levelDropdown == null) return;

        if (levelDropdown.options.Count == 0)
        {
            levelDropdown.options.Add(new TMP_Dropdown.OptionData("Level 1"));
            levelDropdown.options.Add(new TMP_Dropdown.OptionData("Level 2"));
            levelDropdown.options.Add(new TMP_Dropdown.OptionData("Level 3"));
        }

        levelDropdown.value = 0;
        _currentLevel = 1;
        levelDropdown.onValueChanged.AddListener(OnLevelChanged);
    }

    // ─────────────────────────────────────────────────────────────
    //  Button / Dropdown callbacks
    // ─────────────────────────────────────────────────────────────

    private void OnLevelChanged(int index)
    {
        _currentLevel = index + 1;
        if (!_isFetching)
            StartCoroutine(FetchAll());
    }

    private void OnRefreshClicked()
    {
        if (!_isFetching)
            StartCoroutine(FetchAll());
    }

    // ─────────────────────────────────────────────────────────────
    //  Master fetch coroutine
    // ─────────────────────────────────────────────────────────────

    private IEnumerator FetchAll()
    {
        _isFetching = true;
        SetStatus("Status: Connecting...", Color.white);

        // Reset player card to defaults before each fetch
        SetNoRecord(bestScoreText);
        SetNoRecord(playerRankText);
        ClearLeaderboard();   // hide rows while fetching; re-populated only on success

        bool anyError = false;

        // ── 1. Personal best score ───────────────────────────────
        yield return StartCoroutine(FetchBestScore(ok => { if (!ok) anyError = true; }));

        // ── 2. Player rank ───────────────────────────────────────
        yield return StartCoroutine(FetchPlayerRank(ok => { if (!ok) anyError = true; }));

        // ── 3. Top-10 leaderboard ────────────────────────────────
        yield return StartCoroutine(FetchTop10(ok => { if (!ok) anyError = true; }));

        bool nowConnected = !anyError;
        SetStatus(anyError ? "Status: Disconnect" : "Status: Connected",
                  anyError ? ColDisconnect : ColConnected);

        // If we ended up disconnected, wipe the board so stale data is never shown.
        if (!nowConnected)
            ClearLeaderboard();

        _isFetching = false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Individual fetch coroutines  (routed through ApiClient)
    // ─────────────────────────────────────────────────────────────

    private IEnumerator FetchBestScore(System.Action<bool> done)
    {
        bool finished = false;
        bool success = false;

        yield return ApiClient.Instance.StartCoroutine(
            ApiClient.Instance.Get<BestScoreResponse>(
                $"api/leaderboard/player/{_playerId}/level/{_currentLevel}",
                (resp, err) =>
                {
                    if (err != null)
                    {
                        Debug.LogWarning($"[SafetyRecordManager] BestScore error: {err}");
                        success = false;
                    }
                    else if (resp != null && resp.bestScore > 0)
                    {
                        if (bestScoreText != null)
                        {
                            bestScoreText.text = resp.bestScore.ToString();
                            bestScoreText.color = ColScoreFound;
                        }
                        success = true;
                    }
                    else
                    {
                        // No record — keep "No Record" default
                        success = true;
                    }
                    finished = true;
                }));

        yield return new WaitUntil(() => finished);
        done(success);
    }

    private IEnumerator FetchPlayerRank(System.Action<bool> done)
    {
        bool finished = false;
        bool success = false;

        yield return ApiClient.Instance.StartCoroutine(
            ApiClient.Instance.Get<RankResponse>(
                $"api/leaderboard/rank/{_playerId}/level/{_currentLevel}",
                (resp, err) =>
                {
                    if (err != null)
                    {
                        Debug.LogWarning($"[SafetyRecordManager] PlayerRank error: {err}");
                        success = false;
                    }
                    else if (resp != null && resp.rank > 0)
                    {
                        if (playerRankText != null)
                        {
                            playerRankText.text = resp.rank.ToString();
                            playerRankText.color = ColRankFound;
                        }
                        success = true;
                    }
                    else
                    {
                        success = true;
                    }
                    finished = true;
                }));

        yield return new WaitUntil(() => finished);
        done(success);
    }

    private IEnumerator FetchTop10(System.Action<bool> done)
    {
        bool finished = false;
        bool success = false;

        // ApiClient.Get<T> uses JsonUtility which can't deserialise top-level arrays,
        // so we use a wrapper response type and pass the JSON-wrapped endpoint via
        // a raw get through ApiClient's Get<LeaderboardWrapper> with the wrapping
        // handled server-side — instead we fall back to a local wrapper trick via
        // a custom coroutine that reuses ApiClient.Instance.baseUrl only.
        string url = $"{ApiClient.Instance.baseUrl.TrimEnd('/')}/api/leaderboard/top10/{_currentLevel}";

        yield return ApiClient.Instance.StartCoroutine(
            GetWrappedArray(url, (entries, err) =>
            {
                if (err != null)
                {
                    Debug.LogWarning($"[SafetyRecordManager] Top10 error: {err}");
                    success = false;
                }
                else
                {
                    PopulateLeaderboard(entries);
                    success = true;
                }
                finished = true;
            }));

        yield return new WaitUntil(() => finished);
        done(success);
    }

    /// <summary>
    /// JsonUtility cannot deserialise top-level JSON arrays, so we fetch the raw
    /// text and wrap it before parsing.  Reuses ApiClient's singleton so we only
    /// ever maintain one place for the base URL.
    /// </summary>
    private IEnumerator GetWrappedArray(string url,
        System.Action<List<LeaderboardEntry>, string> callback)
    {
        using var req = UnityEngine.Networking.UnityWebRequest.Get(url);
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            callback(null, req.error);
            yield break;
        }

        string wrapped = $"{{\"items\":{req.downloadHandler.text}}}";
        var data = JsonUtility.FromJson<LeaderboardWrapper>(wrapped);
        callback(data?.items ?? new List<LeaderboardEntry>(), null);
    }

    // ─────────────────────────────────────────────────────────────
    //  Leaderboard row spawning
    // ─────────────────────────────────────────────────────────────

    private void PopulateLeaderboard(List<LeaderboardEntry> entries)
    {
        if (rankingParent == null || rankingRowPrefab == null) return;

        foreach (Transform child in rankingParent)
            Destroy(child.gameObject);

        foreach (var entry in entries)
            ConfigureRow(Instantiate(rankingRowPrefab, rankingParent), entry);
    }

    /// <summary>Removes all spawned leaderboard rows, leaving the table empty.</summary>
    private void ClearLeaderboard()
    {
        if (rankingParent == null) return;
        foreach (Transform child in rankingParent)
            Destroy(child.gameObject);
    }

    private void ConfigureRow(GameObject row, LeaderboardEntry entry)
    {
        // ── Row background colour ────────────────────────────────
        var bg = row.GetComponent<Image>();
        if (bg != null)
            bg.color = entry.playerId == _playerId ? ColPlayerRow : ColRowDefault;

        // ── All text children → white font ───────────────────────
        foreach (var tmp in row.GetComponentsInChildren<TMP_Text>())
            tmp.color = Color.white;

        // ── Rank badge (child path: Rank/Badge) ──────────────────
        var badgeT = row.transform.Find("Rank/Badge");
        if (badgeT != null)
        {
            var badgeImg = badgeT.GetComponent<Image>();
            if (badgeImg != null)
            {
                bool showBadge = entry.rank is 1 or 2 or 3;
                badgeImg.gameObject.SetActive(showBadge);
                if (showBadge)
                    badgeImg.color = entry.rank switch
                    {
                        1 => ColBadgeGold,
                        2 => ColBadgeSilver,
                        3 => ColBadgeBronze,
                        _ => Color.white
                    };
            }
        }

        // ── Text fields ──────────────────────────────────────────
        // Child names match the hierarchy shown in the screenshot:
        //   Rank         →  rank number  (TMP_Text directly on the Rank object)
        //   Name         →  player name
        //   ID           →  player ID (6-digit zero-padded)
        //   Safety Score →  safety score
        //   Days         →  days used
        SetChildText(row, "Rank", entry.rank.ToString());
        SetChildText(row, "Name", entry.playerName);
        SetChildText(row, "ID", entry.playerId.ToString("D6"));
        SetChildText(row, "Safety Score", entry.safetyScore.ToString());
        SetChildText(row, "Days", entry.daysUsed.ToString());
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static void SetChildText(GameObject root, string path, string value)
    {
        var t = root.transform.Find(path);
        if (t == null) return;
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp != null) tmp.text = value;
    }

    private static void SetNoRecord(TMP_Text label)
    {
        if (label == null) return;
        label.text = "No Record";
        label.color = Color.white;
    }

    private void SetStatus(string message, Color colour)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = colour;
    }

    // ─────────────────────────────────────────────────────────────
    //  DTOs
    // ─────────────────────────────────────────────────────────────

    [System.Serializable] private class BestScoreResponse { public int bestScore; }
    [System.Serializable] private class RankResponse { public int rank; }

    [System.Serializable]
    private class LeaderboardEntry
    {
        public int rank;
        public int playerId;
        public string playerName;
        public int safetyScore;
        public int daysUsed;
    }

    [System.Serializable]
    private class LeaderboardWrapper { public List<LeaderboardEntry> items; }
}