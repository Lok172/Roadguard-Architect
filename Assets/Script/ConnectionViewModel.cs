using System;
using System.Collections;
using UnityEngine;

// Backend connectivity is tested here, and the result is persisted.

public enum ConnectionTestStatus { Disconnected, Connecting, Connected }

public class ConnectionViewModel
{
    private const string PREF_LAST_SYNC = "Settings_LastSyncTime";
    private const string PREF_DATA_CACHED = "Settings_DataCached";
    private const string PING_PATH = "api/health";

    [Serializable] private class PingResponse { public int rank; }

    public bool IsTesting { get; private set; }

    public event Action<ConnectionTestStatus> OnStatusChanged;
    public event Action<string> OnLastSyncUpdated;
    public event Action<bool> OnDataCachedChanged;
    public event Action<string, int> OnUserInfoUpdated;

    public void Initialize()
    {
        string saved = PlayerPrefs.GetString(PREF_LAST_SYNC, "");
        OnLastSyncUpdated?.Invoke(string.IsNullOrEmpty(saved) ? null : saved);

        bool cached = PlayerPrefs.GetInt(PREF_DATA_CACHED, 0) == 1;
        OnDataCachedChanged?.Invoke(cached);

        OnStatusChanged?.Invoke(ConnectionTestStatus.Disconnected);

        PopulateUserInfo();
    }

    public void PopulateUserInfo()
    {
        if (!UserSession.IsLoggedIn)
            UserSession.TryLoadFromDisk();

        OnUserInfoUpdated?.Invoke(
            UserSession.IsLoggedIn ? UserSession.CurrentUser.username : null,
            UserSession.IsLoggedIn ? UserSession.CurrentUser.userId : 0);
    }

    public void TestConnection()
    {
        if (IsTesting) return;
        CoroutineRunner.Instance?.Run(TestConnectionRoutine());
    }

    private IEnumerator TestConnectionRoutine()
    {
        IsTesting = true;
        OnStatusChanged?.Invoke(ConnectionTestStatus.Connecting);

        bool connected = false;
        yield return PingServer(ok => connected = ok);

        if (connected)
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
            PlayerPrefs.SetString(PREF_LAST_SYNC, now);
            PlayerPrefs.SetInt(PREF_DATA_CACHED, 1);
            PlayerPrefs.Save();

            OnLastSyncUpdated?.Invoke(now);
            OnDataCachedChanged?.Invoke(true);
            OnStatusChanged?.Invoke(ConnectionTestStatus.Connected);
            PopulateUserInfo();

            Debug.Log($"[ConnectionViewModel] Connected at {now}");
        }
        else
        {
            PlayerPrefs.SetInt(PREF_DATA_CACHED, 0);
            PlayerPrefs.Save();

            OnDataCachedChanged?.Invoke(false);
            OnStatusChanged?.Invoke(ConnectionTestStatus.Disconnected);
        }

        IsTesting = false;
    }

    private IEnumerator PingServer(Action<bool> done)
    {
        bool finished = false;
        bool success = false;

        yield return ApiClient.Instance.StartCoroutine(
            ApiClient.Instance.Get<PingResponse>(
                PING_PATH,
                (resp, err) =>
                {
                    success = (err == null);
                    finished = true;
                    if (err != null)
                        Debug.LogWarning($"[ConnectionViewModel] Ping error: {err}");
                }));

        yield return new WaitUntil(() => finished);
        done(success);
    }
}
