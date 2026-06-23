using System.IO;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  USER SESSION  (v2 — offline-first, local JSON profile)
//
//  Design
//  ──────
//  • The game is single-player.  No password is required.
//  • On first launch the player is asked for a display name.
//  • The profile is stored in a JSON file at:
//      Application.persistentDataPath/userprofile.json
//  • When an internet connection is available AND the player does not
//    yet have a server-assigned userId (userId == 0), UserSession
//    calls the backend to register/fetch the userId and persists it
//    back to the local file.
//  • Subsequent launches load the saved profile immediately.
//
//  Used by: AuthManager, GameManager, LevelProgress, LevelResultsManager
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
