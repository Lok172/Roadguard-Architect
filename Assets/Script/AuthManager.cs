using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────────
//  AUTH MANAGER  (v4 — confirmation panel, restructured inspector)
//
//  Flow
//  ────
//  On Start:
//    1. Try to load saved profile from disk (UserSession.TryLoadFromDisk).
//    2a. Profile found → show Welcome Panel, then navigate to Level Select.
//        If userId == 0 and internet is reachable, silently sync userId.
//    2b. No profile → show Name Entry Panel.
//        Player types a display name (≥ 2 chars) and presses Confirm.
//        → Confirmation Panel shown (header: "Once confirmed, this username
//          cannot be changed. Continue?").  Continue button does nothing
//          (assign via Navigation.cs).  Cancel returns to Name Entry.
//
//  "New User" button (under Welcome Panel header):
//    → Confirmation Panel shown (header: "Create a new profile?
//      Your current profile will be replaced and cannot be recovered").
//      Continue does nothing (assign via Navigation.cs).
//      Cancel hides the Confirmation Panel and shows the Welcome Panel again.
//
//  Welcome Panel text:
//    "Welcome, {PlayerName}" always.
//    If userId > 0, a second line: "Player ID: {PlayerID}".
//
//  Wire all UI references in the Inspector.
// ─────────────────────────────────────────────────────────────────

public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance { get; private set; }

    // ── Name Entry Panel ─────────────────────────────────────────
    [Header("Name Entry Panel")]
    [Tooltip("Panel shown on first launch — asks the player for their name.")]
    public GameObject nameEntryPanel;

    [Tooltip("Input field where the player types their display name.")]
    public TMP_InputField nameInputField;

    [Tooltip("Button to confirm the entered name (leads to Confirmation Panel).")]
    public Button confirmNameButton;

    // ── Welcome Panel ────────────────────────────────────────────
    [Header("Welcome Panel")]
    [Tooltip("Panel shown when a returning player is detected or after a new name is confirmed.")]
    public GameObject welcomePanel;

    [Tooltip("Text inside the welcome panel. Shows 'Welcome, {name}' and optionally Player ID.")]
    public TMP_Text welcomeText;

    [Tooltip("Button that lets a returning player start a fresh profile.")]
    public Button newUserButton;

    // ── Confirmation Panel ───────────────────────────────────────
    [Header("Confirmation Panel")]
    [Tooltip("Panel shown before committing a name entry or a new-user reset.")]
    public GameObject confirmationPanel;

    [Tooltip("Header text inside the Confirmation Panel — set dynamically at runtime.")]
    public TMP_Text confirmationHeaderText;

    [Tooltip("Continue button — assign its action externally via Navigation.cs.")]
    public Button confirmationContinueButton;

    [Tooltip("Cancel button — returns to the previous panel.")]
    public Button confirmationCancelButton;

    // ── Name Error Panel ─────────────────────────────────────────
    [Header("Name Error Panel")]
    [Tooltip("Panel shown when name validation fails.")]
    public GameObject nameErrorPanel;

    [Tooltip("Text inside the error panel (explains what went wrong).")]
    public TMP_Text nameErrorText;

    [Tooltip("Button that dismisses the error panel and returns to the Name Entry panel.")]
    public Button errorCloseButton;

    // ── Navigation ───────────────────────────────────────────────
    [Header("Navigation")]
    [Tooltip("Scene or UI page name to navigate to after the profile is ready.")]
    [SceneName] public string levelSelectScene = "LevelSelect";

    // ── Private state ─────────────────────────────────────────────
    // Tracks what context opened the Confirmation Panel so Cancel knows
    // which panel to restore.
    private enum ConfirmContext { None, NewName, NewUser }
    private ConfirmContext _confirmContext = ConfirmContext.None;

    // The name validated in OnConfirmName — committed only when Continue is pressed.
    private string _pendingName = "";

    // ─────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        HideAll();
        SetupButtons();

        if (UserSession.TryLoadFromDisk())
        {
            OnProfileReady(isReturning: true);
        }
        else
        {
            ShowNameEntry();
        }
    }

    private void SetupButtons()
    {
        confirmNameButton?.onClick.AddListener(OnConfirmName);
        newUserButton?.onClick.AddListener(OnNewUser);
        confirmationCancelButton?.onClick.AddListener(OnConfirmationCancel);
        errorCloseButton?.onClick.AddListener(OnErrorClose);
        confirmationContinueButton?.onClick.AddListener(CommitConfirmation);
    }

    // ─────────────────────────────────────────
    //  NAME ENTRY
    // ─────────────────────────────────────────

    private void ShowNameEntry()
    {
        nameEntryPanel?.SetActive(true);
        if(nameEntryPanel!=null)
            MusicManager.Instance?.HookPanel(nameEntryPanel);
        nameErrorPanel?.SetActive(false);
    }

    private void OnConfirmName()
    {
        string name = nameInputField != null ? nameInputField.text.Trim() : "";

        if (name.Length < 2)
        {
            ShowError("Name must be at least 2 characters.");
            return;
        }
        if (name.Length > 24)
        {
            ShowError("Name must be 24 characters or fewer.");
            return;
        }

        // Store pending name — it will be committed when Continue is pressed.
        _pendingName = name;

        // Show confirmation panel with the "cannot change username" message.
        HideAll();
        _confirmContext = ConfirmContext.NewName;
        ShowConfirmationPanel(
            "Once confirmed, this username cannot be changed.\nContinue?");
    }

    // ─────────────────────────────────────────
    //  PUBLIC: called by Navigation.cs Continue button
    // ─────────────────────────────────────────

    /// <summary>
    /// Commits the pending action (new name or new user).
    /// Wire this to the Continue button via Navigation.cs if you need
    /// code-side logic to run — or leave the button for pure navigation.
    /// </summary>
    public void CommitConfirmation()
    {
        switch (_confirmContext)
        {
            case ConfirmContext.NewName:
                CommitNewName();
                break;

            case ConfirmContext.NewUser:
                CommitNewUser();
                break;
        }
        _confirmContext = ConfirmContext.None;
    }

    private void CommitNewName()
    {
        UserDto newUser = new UserDto { userId = 0, username = _pendingName };
        UserSession.Login(newUser);
        Debug.Log($"[AuthManager] New profile created: '{_pendingName}'");
        OnProfileReady(isReturning: false);
    }

    private void CommitNewUser()
    {
        LevelProgress.ResetProgress();
        UserSession.DeleteProfile();
        HideAll();
        ShowNameEntry();
    }

    // ─────────────────────────────────────────
    //  NEW USER (reset)
    // ─────────────────────────────────────────

    private void OnNewUser()
    {
        HideAll();
        _confirmContext = ConfirmContext.NewUser;
        ShowConfirmationPanel(
            "Create a new profile?\nYour current profile will be replaced and cannot be recovered.");
    }

    // ─────────────────────────────────────────
    //  CONFIRMATION PANEL
    // ─────────────────────────────────────────

    private void ShowConfirmationPanel(string headerMessage)
    {
        if (confirmationPanel != null)
        {
            confirmationPanel.SetActive(true);
            if (confirmationHeaderText != null)
                confirmationHeaderText.text = headerMessage;

            // Attach button sounds — panel was inactive during the initial sweep.
            MusicManager.Instance?.HookPanel(confirmationPanel);
        }
    }

    private void OnConfirmationCancel()
    {
        HideAll();

        // Restore whichever panel was active before the confirmation.
        switch (_confirmContext)
        {
            case ConfirmContext.NewName:
                ShowNameEntry();
                break;

            case ConfirmContext.NewUser:
                // Return to the welcome panel.
                if (welcomePanel != null)
                {
                    welcomePanel.SetActive(true);
                    if(welcomePanel!=null)
                        MusicManager.Instance?.HookPanel(welcomePanel);
                    UpdateWelcomeText();
                }
                break;
        }

        _confirmContext = ConfirmContext.None;
    }

    // ─────────────────────────────────────────
    //  PROFILE READY
    // ─────────────────────────────────────────

    private void OnProfileReady(bool isReturning)
    {
        HideAll();

        // Show the welcome panel.
        if (welcomePanel != null)
        {
            welcomePanel.SetActive(true);
            UpdateWelcomeText();
        }

        // Background userId sync — only if userId not yet assigned.
        if (UserSession.IsLoggedIn &&
            UserSession.CurrentUser.userId == 0 &&
            ApiClient.Instance != null)
        {
            CoroutineRunner.Instance?.Run(TrySyncUserId(UserSession.CurrentUser.username));
        }
    }

    private void UpdateWelcomeText()
    {
        if (welcomeText == null || !UserSession.IsLoggedIn) return;

        UserDto user = UserSession.CurrentUser;
        string msg = $"Welcome, <b>{user.username}</b>";

        if (user.userId > 0)
            msg += $"\nPlayer ID: {user.userId.ToString("D6")}";

        welcomeText.text = msg;
    }

    // ─────────────────────────────────────────
    //  NAVIGATION  (called by Navigation.cs or Continue button)
    // ─────────────────────────────────────────

    public void NavigateToLevelSelect()
    {
        if (PageManager.Instance != null)
            PageManager.Instance.ChangeUI(levelSelectScene);
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(levelSelectScene);
    }

    private IEnumerator NavigateAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        NavigateToLevelSelect();
    }

    // ─────────────────────────────────────────
    //  BACKGROUND USERID SYNC
    // ─────────────────────────────────────────

    private IEnumerator TrySyncUserId(string username)
    {
        var body = new RegisterRequest { username = username, password = "" };

        yield return ApiClient.Instance.Post<AuthResponse>(
            "api/auth/register", body,
            (response, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"[AuthManager] userId sync failed (offline?): {error}");
                }
                else if (response != null && response.userId > 0)
                {
                    UserSession.SetUserId(response.userId);
                    Debug.Log($"[AuthManager] userId assigned by server: {response.userId}");

                    // Refresh the welcome text now that the ID is known.
                    UpdateWelcomeText();
                }
            });
    }

    // ─────────────────────────────────────────
    //  UI HELPERS
    // ─────────────────────────────────────────

    private void HideAll()
    {
        nameEntryPanel?.SetActive(false);
        welcomePanel?.SetActive(false);
        nameErrorPanel?.SetActive(false);
        confirmationPanel?.SetActive(false);
    }

    private void ShowError(string msg)
    {
        if (nameErrorPanel == null) return;
        nameErrorPanel.SetActive(true);

        if (nameErrorText != null)
            nameErrorText.text = msg;

        // Attach button sounds — panel was inactive during the initial sweep.
        MusicManager.Instance?.HookPanel(nameErrorPanel);
    }

    private void OnErrorClose()
    {
        nameErrorPanel?.SetActive(false);
    }
}

// ── DTOs (kept for server sync; password is sent as empty string) ─

[System.Serializable]
public class RegisterRequest
{
    public string username;
    public string password;   // always "" for this offline-first flow
}

[System.Serializable]
public class LoginRequest
{
    public string username;
    public string password;
}

[System.Serializable]
public class AuthResponse
{
    public int userId;
    public string username;
}