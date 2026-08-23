using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// This script is used to handle HTTP communication between the game and the
// backend Web API, including GET and POST requests and response parsing.

public class ApiClient : MonoBehaviour
{
    public static ApiClient Instance { get; private set; }

    [Header("Backend URL")]
    [Tooltip("Base URL of the ASP.NET Core Web API, e.g. http://localhost:5000")]
    public string baseUrl = "http://13.210.241.10:5230";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

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

    public IEnumerator Get<TResponse>(string endpoint,
        Action<TResponse, string> callback)
    {
        string url = baseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/');

        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        HandleResponse(req, callback);
    }

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
