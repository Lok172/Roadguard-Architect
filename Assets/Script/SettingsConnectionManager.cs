using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────
//  SETTINGS CONNECTION MANAGER 
//
//  INSPECTOR SETUP:
//    • connectionButton → "Test Connection" button
//    • lastSyncTimeText → default "No Record"
//    • dataCachedText   → default "No"
//    • statusText       → default "Disconnected" (red)
//    • userNameText     → (optional) shows logged-in username
//    • userIdText       → (optional) shows logged-in user ID
//
// ─────────────────────────────────────────────────────────────────

public class SettingsConnectionManager : MonoBehaviour
{
    // ── Inspector fields ─────────────────────────────────────────
    [Header("UI References")]
    public Button connectionButton;
    public TMP_Text lastSyncTimeText;
    public TMP_Text dataCachedText;
    public TMP_Text statusText;

    [Header("User Info (optional)")]
    [Tooltip("Shows the logged-in player's username. Leave empty to skip.")]
    public TMP_Text userNameText;
    [Tooltip("Shows the logged-in player's user ID. Leave empty to skip.")]
    public TMP_Text userIdText;


    [System.Serializable] private class RankResponse { public int rank; }
    // ── Colours ──────────────────────────────────────────────────
    private static readonly Color ColConnected = new Color(0x6A / 255f, 0xFF / 255f, 0x68 / 255f, 1f); // #6AFF68
    private static readonly Color ColDisconnected = new Color(0xFF / 255f, 0x00 / 255f, 0x00 / 255f, 1f); // #FF0000
    private static readonly Color ColChecking = Color.white;

    // ── PlayerPrefs keys ─────────────────────────────────────────
    private const string PREF_LAST_SYNC = "Settings_LastSyncTime";
    private const string PREF_DATA_CACHED = "Settings_DataCached";

    // ── Ping path (appended to ApiClient.baseUrl) ────────────────
    private const string PING_PATH = "api/health";

    private bool _isTesting = false;

    // ─────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────

    private void Start()
    {
        // Restore persisted values from previous session
        RestoreFromPrefs();

        // Always start as Disconnected until a test is run this session
        SetStatus("Disconnected", ColDisconnected);

        // Populate user info from UserSession
        PopulateUserInfo();

        if (connectionButton != null)
            connectionButton.onClick.AddListener(OnConnectionButtonClicked);
    }

    // ─────────────────────────────────────────
    //  USER INFO
    // ─────────────────────────────────────────

    private void PopulateUserInfo()
    {
        // Try loading from disk if not already in memory
        if (!UserSession.IsLoggedIn)
            UserSession.TryLoadFromDisk();

        if (userNameText != null)
            userNameText.text = UserSession.IsLoggedIn ? UserSession.CurrentUser.username : "—";

        if (userIdText != null)
            userIdText.text = UserSession.IsLoggedIn
                ? UserSession.CurrentUser.userId.ToString("D6")
                : "—";
    }

    // ─────────────────────────────────────────
    //  BUTTON CALLBACK
    // ─────────────────────────────────────────

    private void OnConnectionButtonClicked()
    {
        if (_isTesting) return;
        StartCoroutine(TestConnection());
    }

    // ─────────────────────────────────────────
    //  CONNECTION TEST
    // ─────────────────────────────────────────

    private IEnumerator TestConnection()
    {
        _isTesting = true;
        SetConnectionButton(false);
        SetStatus("Connecting...", ColChecking);

        bool connected = false;

        yield return StartCoroutine(PingServer(ok => connected = ok));

        if (connected)
        {
            string now = System.DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
            PlayerPrefs.SetString(PREF_LAST_SYNC, now);
            PlayerPrefs.SetInt(PREF_DATA_CACHED, 1);
            PlayerPrefs.Save();

            SetLastSync(now);
            SetDataCached(true);
            SetStatus("Connected", ColConnected);
            PopulateUserInfo();

            Debug.Log($"[SettingsConnectionManager] Connected at {now}");
        }
        else
        {
            PlayerPrefs.SetInt(PREF_DATA_CACHED, 0);
            PlayerPrefs.Save();

            SetDataCached(false);
            SetStatus("Disconnected", ColDisconnected);
        }

        SetConnectionButton(true);
        _isTesting = false;
    }

    private IEnumerator PingServer(System.Action<bool> done)
    {
        bool finished = false;
        bool success = false;

        yield return ApiClient.Instance.StartCoroutine(
            ApiClient.Instance.Get<RankResponse>(
                PING_PATH,
                (resp, err) =>
                {
                    success = (err == null);
                    finished = true;
                    if (err != null)
                        Debug.LogWarning($"[SettingsConnectionManager] Ping error: {err}");
                }));

        yield return new WaitUntil(() => finished);
        done(success);
    }

    // ─────────────────────────────────────────
    //  RESTORE FROM PREFS
    // ─────────────────────────────────────────

    private void RestoreFromPrefs()
    {
        string saved = PlayerPrefs.GetString(PREF_LAST_SYNC, "");
        SetLastSync(string.IsNullOrEmpty(saved) ? null : saved);

        bool cached = PlayerPrefs.GetInt(PREF_DATA_CACHED, 0) == 1;
        SetDataCached(cached);
    }

    // ─────────────────────────────────────────
    //  UI HELPERS
    // ─────────────────────────────────────────

    private void SetLastSync(string time)
    {
        if (lastSyncTimeText == null) return;
        lastSyncTimeText.text = string.IsNullOrEmpty(time) ? "No Record" : time;
    }

    private void SetDataCached(bool cached)
    {
        if (dataCachedText == null) return;
        dataCachedText.text = cached ? "Yes" : "No";
        dataCachedText.color = cached ? ColConnected : Color.white;
    }

    private void SetStatus(string message, Color colour)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = colour;
    }

    private void SetConnectionButton(bool interactable)
    {
        if (connectionButton == null) return;
        connectionButton.interactable = interactable;
        var label = connectionButton.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = interactable ? "Test Connection" : "Testing...";
    }
}