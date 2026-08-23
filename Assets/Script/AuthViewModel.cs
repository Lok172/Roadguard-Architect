using System.Collections;
using UnityEngine;

// This script is used to manage player profile creation, name validation,
// and session state, including confirmation flows and userId synchronization
// with the backend.

public class AuthViewModel
{
    private enum ConfirmContext { None, NewName, NewUser }
    private ConfirmContext _confirmContext = ConfirmContext.None;

    private string _pendingName = "";

    public bool IsLoggedIn => UserSession.IsLoggedIn;
    public string CurrentUsername => UserSession.IsLoggedIn ? UserSession.CurrentUser.username : null;
    public int CurrentUserId => UserSession.IsLoggedIn ? UserSession.CurrentUser.userId : 0;

    public event System.Action OnShowNameEntry;
    public event System.Action<bool> OnShowWelcome;
    public event System.Action<string> OnShowConfirmation;
    public event System.Action<string> OnShowError;
    public event System.Action OnUserIdSynced;

    public void Initialize()
    {
        if (UserSession.TryLoadFromDisk())
            ProfileReady(isReturning: true);
        else
            OnShowNameEntry?.Invoke();
    }

    public void SubmitName(string rawName)
    {
        string name = (rawName ?? "").Trim();

        if (name.Length < 2)
        {
            OnShowError?.Invoke("Name must be at least 2 characters.");
            return;
        }
        if (name.Length > 24)
        {
            OnShowError?.Invoke("Name must be 24 characters or fewer.");
            return;
        }

        _pendingName = name;
        _confirmContext = ConfirmContext.NewName;
        OnShowConfirmation?.Invoke("Once confirmed, this username cannot be changed.\nContinue?");
    }

    public void RequestNewUser()
    {
        _confirmContext = ConfirmContext.NewUser;
        OnShowConfirmation?.Invoke(
            "Create a new profile?\nYour current profile will be replaced and cannot be recovered.");
    }

    public void ConfirmationContinue()
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

    public void ConfirmationCancel()
    {
        switch (_confirmContext)
        {
            case ConfirmContext.NewName:
                OnShowNameEntry?.Invoke();
                break;
            case ConfirmContext.NewUser:
                OnShowWelcome?.Invoke(true);
                break;
        }
        _confirmContext = ConfirmContext.None;
    }

    private void CommitNewName()
    {
        UserDto newUser = new UserDto { userId = 0, username = _pendingName };
        UserSession.Login(newUser);
        Debug.Log($"[AuthViewModel] New profile created: '{_pendingName}'");

        PlayerPrefs.SetInt(TutorialManager.TUTORIAL_IS_READ_KEY, 0);
        PlayerPrefs.Save();
        Debug.Log("[AuthViewModel] TutorialIsRead reset to false for new account.");

        ProfileReady(isReturning: false);
    }

    private void CommitNewUser()
    {
        LevelProgress.ResetProgress();
        UserSession.DeleteProfile();
        OnShowNameEntry?.Invoke();
    }

    private void ProfileReady(bool isReturning)
    {
        OnShowWelcome?.Invoke(isReturning);

        if (UserSession.IsLoggedIn &&
            UserSession.CurrentUser.userId == 0 &&
            ApiClient.Instance != null)
        {
            CoroutineRunner.Instance?.Run(TrySyncUserId(UserSession.CurrentUser.username));
        }
    }

    private IEnumerator TrySyncUserId(string username)
    {
        var body = new RegisterRequest { username = username, password = "" };

        yield return ApiClient.Instance.Post<AuthResponse>(
            "api/auth/register", body,
            (response, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"[AuthViewModel] userId sync failed (offline?): {error}");
                }
                else if (response != null && response.userId > 0)
                {
                    UserSession.SetUserId(response.userId);
                    Debug.Log($"[AuthViewModel] userId assigned by server: {response.userId}");
                    OnUserIdSynced?.Invoke();
                }
            });
    }
}

[System.Serializable]
public class RegisterRequest
{
    public string username;
    public string password;
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
