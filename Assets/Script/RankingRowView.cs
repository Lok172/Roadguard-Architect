using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A single leaderboard row is rendered here.

public class RankingRowView : MonoBehaviour
{
    [Header("Text Fields (auto-found by child name if left empty)")]
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text idText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text daysText;

    [Header("Medal Badge")]
    [SerializeField] private Image badgeImage;

    private static readonly Color ColBadgeGold = new Color(0xFF / 255f, 0xF4 / 255f, 0x00 / 255f, 1f);
    private static readonly Color ColBadgeSilver = new Color(0xCB / 255f, 0xCB / 255f, 0xCB / 255f, 1f);
    private static readonly Color ColBadgeBronze = new Color(0xFF / 255f, 0x9E / 255f, 0x00 / 255f, 1f);

 
    private static readonly Color ColCurrentPlayer = new Color(0x50 / 255f, 0xFF / 255f, 0x64 / 255f, 1f);

    private void Awake()
    {
        if (rankText == null) rankText = transform.Find("Rank")?.GetComponent<TMP_Text>();
        if (nameText == null) nameText = transform.Find("Name")?.GetComponent<TMP_Text>();
        if (idText == null) idText = transform.Find("ID")?.GetComponent<TMP_Text>();
        if (scoreText == null) scoreText = transform.Find("Safety Score")?.GetComponent<TMP_Text>();
        if (daysText == null) daysText = transform.Find("Days")?.GetComponent<TMP_Text>();

        if (badgeImage == null)
        {
            var badgeT = transform.Find("Badge");
            if (badgeT != null) badgeImage = badgeT.GetComponent<Image>();
        }
    }

    public void Configure(RankingEntryData entry)
    {
        Color rowColor = entry.isCurrentPlayer ? ColCurrentPlayer : Color.white;
        foreach (var tmp in GetComponentsInChildren<TMP_Text>())
            tmp.color = rowColor;

        if (rankText != null) rankText.text = entry.rank.ToString();
        if (nameText != null) nameText.text = entry.playerName;
        if (idText != null) idText.text = entry.playerId.ToString("D6");
        if (scoreText != null) scoreText.text = entry.safetyScore.ToString();
        if (daysText != null) daysText.text = entry.daysUsed.ToString();

        if (badgeImage != null)
        {
            bool showBadge = entry.rank is 1 or 2 or 3;
            badgeImage.gameObject.SetActive(showBadge);
            if (showBadge)
            {
                badgeImage.color = entry.rank switch
                {
                    1 => ColBadgeGold,
                    2 => ColBadgeSilver,
                    3 => ColBadgeBronze,
                    _ => Color.white
                };
            }
        }
    }
}