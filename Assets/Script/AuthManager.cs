using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Profile-related UI panels are displayed here in response to AuthViewModel state.

public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance { get; private set; }

    [Header("Name Entry Panel")]
    public GameObject nameEntryPanel;
    public TMP_InputField nameInputField;
    public Button confirmNameButton;

    [Header("Welcome Panel")]
    public GameObject welcomePanel;
    public TMP_Text welcomeText;
    public Button newUserButton;

    [Header("Confirmation Panel")]
    public GameObject confirmationPanel;
    public TMP_Text confirmationHeaderText;
    public Button confirmationContinueButton;
    public Button confirmationCancelButton;

    [Header("Name Error Panel")]
    public GameObject nameErrorPanel;
    public TMP_Text nameErrorText;
    public Button errorCloseButton;

    [Header("Navigation")]
    [Tooltip("Scene or UI page name to navigate to after the profile is ready.")]
    [SceneName] public string levelSelectScene = "LevelSelect";

    private AuthViewModel _vm;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        HideAll();
        SetupButtons();

        _vm = new AuthViewModel();
        _vm.OnShowNameEntry    += HandleShowNameEntry;
        _vm.OnShowWelcome      += HandleShowWelcome;
        _vm.OnShowConfirmation += HandleShowConfirmation;
        _vm.OnShowError        += HandleShowError;
        _vm.OnUserIdSynced     += UpdateWelcomeText;

        _vm.Initialize();
    }

    private void OnDestroy()
    {
        if (_vm == null) return;
        _vm.OnShowNameEntry    -= HandleShowNameEntry;
        _vm.OnShowWelcome      -= HandleShowWelcome;
        _vm.OnShowConfirmation -= HandleShowConfirmation;
        _vm.OnShowError        -= HandleShowError;
        _vm.OnUserIdSynced     -= UpdateWelcomeText;
    }

    private void SetupButtons()
    {
        confirmNameButton?.onClick.AddListener(
            () => _vm.SubmitName(nameInputField != null ? nameInputField.text : ""));
        newUserButton?.onClick.AddListener(() => _vm.RequestNewUser());
        confirmationCancelButton?.onClick.AddListener(() => _vm.ConfirmationCancel());
        errorCloseButton?.onClick.AddListener(OnErrorClose);
        confirmationContinueButton?.onClick.AddListener(CommitConfirmation);
    }

    public void CommitConfirmation() => _vm.ConfirmationContinue();

    private void HandleShowNameEntry()
    {
        HideAll();
        nameEntryPanel?.SetActive(true);
        if (nameEntryPanel != null)
            MusicManager.Instance?.HookPanel(nameEntryPanel);
    }

    private void HandleShowWelcome(bool isReturning)
    {
        HideAll();
        if (welcomePanel != null)
        {
            welcomePanel.SetActive(true);
            UpdateWelcomeText();
        }
    }

    private void HandleShowConfirmation(string headerMessage)
    {
        HideAll();
        if (confirmationPanel == null) return;

        confirmationPanel.SetActive(true);
        if (confirmationHeaderText != null)
            confirmationHeaderText.text = headerMessage;

        MusicManager.Instance?.HookPanel(confirmationPanel);
    }

    private void HandleShowError(string msg)
    {
        if (nameErrorPanel == null) return;

        nameErrorPanel.SetActive(true);
        if (nameErrorText != null)
            nameErrorText.text = msg;

        MusicManager.Instance?.HookPanel(nameErrorPanel);
    }

    private void UpdateWelcomeText()
    {
        if (welcomeText == null || !_vm.IsLoggedIn) return;

        string msg = $"Welcome, <b>{_vm.CurrentUsername}</b>";
        if (_vm.CurrentUserId > 0)
            msg += $"\nPlayer ID: {_vm.CurrentUserId.ToString("D6")}";

        welcomeText.text = msg;
    }

    public void NavigateToLevelSelect()
    {
        if (PageManager.Instance != null)
            PageManager.Instance.ChangeUI(levelSelectScene);
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(levelSelectScene);
    }

    private void HideAll()
    {
        nameEntryPanel?.SetActive(false);
        welcomePanel?.SetActive(false);
        nameErrorPanel?.SetActive(false);
        confirmationPanel?.SetActive(false);
    }

    private void OnErrorClose()
    {
        nameErrorPanel?.SetActive(false);
    }
}
