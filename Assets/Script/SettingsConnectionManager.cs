using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The connection-test UI is rendered here.

public class SettingsConnectionManager : MonoBehaviour
{
    [Header("UI References")]
    public Button connectionButton;
    public TMP_Text lastSyncTimeText;
    public TMP_Text dataCachedText;
    public TMP_Text statusText;

    [Header("User Info")]
    public TMP_Text userNameText;
    public TMP_Text userIdText;

    private static readonly Color ColConnected = new Color(0x6A / 255f, 0xFF / 255f, 0x68 / 255f, 1f);
    private static readonly Color ColDisconnected = new Color(0xFF / 255f, 0x00 / 255f, 0x00 / 255f, 1f);
    private static readonly Color ColChecking = Color.white;

    private ConnectionViewModel _vm;

    private void Start()
    {
        _vm = new ConnectionViewModel();
        _vm.OnStatusChanged     += HandleStatusChanged;
        _vm.OnLastSyncUpdated   += HandleLastSyncUpdated;
        _vm.OnDataCachedChanged += HandleDataCachedChanged;
        _vm.OnUserInfoUpdated   += HandleUserInfoUpdated;

        if (connectionButton != null)
            connectionButton.onClick.AddListener(() => _vm.TestConnection());

        _vm.Initialize();
    }

    private void OnDestroy()
    {
        if (_vm == null) return;
        _vm.OnStatusChanged     -= HandleStatusChanged;
        _vm.OnLastSyncUpdated   -= HandleLastSyncUpdated;
        _vm.OnDataCachedChanged -= HandleDataCachedChanged;
        _vm.OnUserInfoUpdated   -= HandleUserInfoUpdated;
    }

    private void HandleStatusChanged(ConnectionTestStatus status)
    {
        SetConnectionButtonInteractable(status != ConnectionTestStatus.Connecting);

        if (statusText == null) return;

        switch (status)
        {
            case ConnectionTestStatus.Connecting:
                statusText.text = "Connecting...";
                statusText.color = ColChecking;
                break;
            case ConnectionTestStatus.Connected:
                statusText.text = "Connected";
                statusText.color = ColConnected;
                break;
            case ConnectionTestStatus.Disconnected:
                statusText.text = "Disconnected";
                statusText.color = ColDisconnected;
                break;
        }
    }

    private void HandleLastSyncUpdated(string time)
    {
        if (lastSyncTimeText == null) return;
        lastSyncTimeText.text = string.IsNullOrEmpty(time) ? "No Record" : time;
    }

    private void HandleDataCachedChanged(bool cached)
    {
        if (dataCachedText == null) return;
        dataCachedText.text = cached ? "Yes" : "No";
        dataCachedText.color = cached ? ColConnected : Color.white;
    }

    private void HandleUserInfoUpdated(string username, int userId)
    {
        if (userNameText != null)
            userNameText.text = string.IsNullOrEmpty(username) ? "—" : username;

        if (userIdText != null)
            userIdText.text = userId > 0 ? userId.ToString("D6") : "—";
    }

    private void SetConnectionButtonInteractable(bool interactable)
    {
        if (connectionButton == null) return;
        connectionButton.interactable = interactable;
        var label = connectionButton.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = interactable ? "Test Connection" : "Testing...";
    }
}
