using System.IO;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  USER SESSION
//
//  Manages the local player profile for the single-player game.
//  The profile (display name and server-assigned userId) is stored
//  as JSON at Application.persistentDataPath/userprofile.json and
//  loaded on each launch. When online and no userId has been
//  assigned yet, the backend registers the player and the returned
//  id is persisted back to the local file.
// ─────────────────────────────────────────────────────────────────

public static class UserSession
{
    // ── In-memory current user ───────────────────────────────────
    public static UserDto CurrentUser { get; private set; }
    public static bool    IsLoggedIn  => CurrentUser != null;

    // ── File path ────────────────────────────────────────────────
    private static string ProfilePath =>
        Path.Combine(Application.persistentDataPath, "userprofile.json");

    // ─────────────────────────────────────────
    //  Load / Save
    // ─────────────────────────────────────────

    /// <summary>
    /// Tries to load a saved profile from disk.
    /// Returns true and populates CurrentUser on success.
    /// </summary>
    public static bool TryLoadFromDisk()
    {
        if (!File.Exists(ProfilePath)) return false;

        try
        {
            string json = File.ReadAllText(ProfilePath);
            UserDto dto = JsonUtility.FromJson<UserDto>(json);
            if (dto == null || string.IsNullOrEmpty(dto.username)) return false;

            CurrentUser = dto;
            Debug.Log($"[UserSession] Loaded profile: '{dto.username}' (userId={dto.userId})");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[UserSession] Failed to load profile: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sets the in-memory session and persists it to disk.
    /// </summary>
    public static void Login(UserDto user)
    {
        CurrentUser = user;
        SaveToDisk();
    }

    /// <summary>
    /// Updates only the userId field (called once online registration succeeds)
    /// and re-saves the file.
    /// </summary>
    public static void SetUserId(int userId)
    {
        if (CurrentUser == null) return;
        CurrentUser.userId = userId;
        SaveToDisk();
        Debug.Log($"[UserSession] userId synced from server: {userId}");
    }

    public static void Logout()
    {
        CurrentUser = null;
    }

    /// <summary>Deletes the local profile file (called by "New User" / reset).</summary>
    public static void DeleteProfile()
    {
        CurrentUser = null;
        if (File.Exists(ProfilePath))
        {
            File.Delete(ProfilePath);
            Debug.Log("[UserSession] Profile file deleted.");
        }
    }

    // ─────────────────────────────────────────
    //  Internal
    // ─────────────────────────────────────────

    private static void SaveToDisk()
    {
        try
        {
            string json = JsonUtility.ToJson(CurrentUser, prettyPrint: true);
            File.WriteAllText(ProfilePath, json);
            Debug.Log($"[UserSession] Profile saved: {ProfilePath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[UserSession] Failed to save profile: {ex.Message}");
        }
    }
}

// ─────────────────────────────────────────────────────────────────
//  DTO  — serialised to / from JSON on disk and to the Web API
// ─────────────────────────────────────────────────────────────────

[System.Serializable]
public class UserDto
{
    /// <summary>
    /// 0 means "not yet assigned by server".
    /// Assigned the first time the player connects to the internet.
    /// </summary>
    public int    userId;
    public string username;
}
