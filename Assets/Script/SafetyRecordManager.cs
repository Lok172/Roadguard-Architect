using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Global Safety Records screen is rendered here, including the player card and ranking table.

public class SafetyRecordManager : MonoBehaviour
{
    [Header("Level Dropdown")]
    public TMP_Dropdown levelDropdown;

    [Header("Refresh")]
    public Button refreshButton;
    public TMP_Text statusText;

    [Header("Player Card")]
    public TMP_Text playerNameText;
    public TMP_Text playerIdText;
    public TMP_Text bestScoreText;
    public TMP_Text playerRankText;

    [Header("Ranking Table")]
    public Transform rankingParent;
    public GameObject rankingRowPrefab;

    private static readonly Color ColConnected = new Color(0x50 / 255f, 0xFF / 255f, 0x64 / 255f, 1f);
    private static readonly Color ColDisconnect = new Color(255f / 255f, 49f / 255f, 0f / 255f, 1f);
    private static readonly Color ColScoreFound = new Color(0x50 / 255f, 0xFF / 255f, 0x64 / 255f, 1f);
    private static readonly Color ColRankFound = new Color(0xFF / 255f, 0x68 / 255f, 0x4A / 255f, 1f);

    private SafetyRecordViewModel _vm;

    private void Start()
    {
        SetupDropdown();

        _vm = new SafetyRecordViewModel();
        _vm.OnStatusChanged      += HandleStatusChanged;
        _vm.OnPlayerCardUpdated  += HandlePlayerCardUpdated;
        _vm.OnLeaderboardUpdated += HandleLeaderboardUpdated;
        _vm.OnLeaderboardCleared += HandleLeaderboardCleared;

        if (refreshButton != null)
            refreshButton.onClick.AddListener(() => _vm.Refresh());

        _vm.Initialize();
    }

    private void OnDestroy()
    {
        if (_vm == null) return;
        _vm.OnStatusChanged      -= HandleStatusChanged;
        _vm.OnPlayerCardUpdated  -= HandlePlayerCardUpdated;
        _vm.OnLeaderboardUpdated -= HandleLeaderboardUpdated;
        _vm.OnLeaderboardCleared -= HandleLeaderboardCleared;
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
        levelDropdown.onValueChanged.AddListener(index => _vm?.SetLevel(index));
    }

    private void HandleStatusChanged(ConnectionStatus status)
    {
        if (statusText == null) return;

        switch (status)
        {
            case ConnectionStatus.Connecting:
                statusText.text = "Status: Connecting...";
                statusText.color = Color.white;
                break;
            case ConnectionStatus.Connected:
                statusText.text = "Status: Connected";
                statusText.color = ColConnected;
                break;
            case ConnectionStatus.Disconnected:
                statusText.text = "Status: Disconnect";
                statusText.color = ColDisconnect;
                break;
        }
    }

    private void HandlePlayerCardUpdated(PlayerCardData data)
    {
        bool loggedIn = !string.IsNullOrEmpty(data.playerName);

        if (playerNameText != null)
            playerNameText.text = loggedIn ? data.playerName : "No Record";

        if (playerIdText != null)
        {
            if (data.playerId > 0) playerIdText.text = data.playerId.ToString("D6");
            else if (loggedIn) playerIdText.text = "No ID";
            else playerIdText.text = "No Record";
        }

        SetOrNoRecord(bestScoreText, data.bestScore?.ToString(), ColScoreFound);
        SetOrNoRecord(playerRankText, data.rank?.ToString(), ColRankFound);
    }

    private static void SetOrNoRecord(TMP_Text label, string value, Color foundColor)
    {
        if (label == null) return;

        if (string.IsNullOrEmpty(value))
        {
            label.text = "No Record";
            label.color = Color.white;
        }
        else
        {
            label.text = value;
            label.color = foundColor;
        }
    }

    private void HandleLeaderboardUpdated(List<RankingEntryData> entries)
    {
        ClearRows();
        if (rankingParent == null || rankingRowPrefab == null) return;

        foreach (var entry in entries)
        {
            GameObject row = Instantiate(rankingRowPrefab, rankingParent);
            RankingRowView view = row.GetComponent<RankingRowView>();
            if (view == null) view = row.AddComponent<RankingRowView>();
            view.Configure(entry);
        }
    }

    private void HandleLeaderboardCleared() => ClearRows();

    private void ClearRows()
    {
        if (rankingParent == null) return;
        foreach (Transform child in rankingParent)
            Destroy(child.gameObject);
    }
}
