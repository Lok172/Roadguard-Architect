using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// ─────────────────────────────────────────────────────────────────
//  API CLIENT
//
//  Thin HTTP helper used by AuthManager and LevelProgress (and any
//  future Unity ↔ ASP.NET Core Web API calls).
//
//  Base URL is set in the Inspector on the GameObject that holds
//  ApiClient, or simply change the constant below for quick testing.
//
//  All public methods are coroutines that accept Action<T, string>
//  callbacks:  (result, errorMessage).  errorMessage is null on
//  success; result is default(T) on failure.
// ─────────────────────────────────────────────────────────────────

public class ApiClient : MonoBehaviour
{
    public static ApiClient Instance { get; private set; }

    [Header("Backend URL")]
    [Tooltip("Base URL of the ASP.NET Core Web API, e.g. http://localhost:5000")]
    public string baseUrl = "http://localhost:5000";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── POST ─────────────────────────────────────────────────────

    public IEnumerator Post<TResponse>(string endpoint, object body,
        Action<TResponse, string> callback)
    {
        string json    = JsonUtility.ToJson(body);
        string url     = baseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/');
        byte[] payload = Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(payload);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        HandleResponse(req, callback);
    }

    // ── GET ──────────────────────────────────────────────────────

    public IEnumerator Get<TResponse>(string endpoint,
        Action<TResponse, string> callback)
    {
        string url = baseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/');

        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        HandleResponse(req, callback);
    }

    // ── Internal helper ──────────────────────────────────────────

    private void HandleResponse<TResponse>(UnityWebRequest req,
        Action<TResponse, string> callback)
    {
        if (req.result != UnityWebRequest.Result.Success)
        {
            string serverMsg = req.downloadHandler?.text;
            callback(default, string.IsNullOrEmpty(serverMsg) ? req.error : serverMsg);
            return;
        }

        try
        {
            var result = JsonUtility.FromJson<TResponse>(req.downloadHandler.text);
            callback(result, null);
        }
        catch (Exception ex)
        {
            callback(default, $"JSON parse error: {ex.Message}");
        }
    }
}
