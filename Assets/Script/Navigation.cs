using UnityEngine;
using UnityEngine.EventSystems;

//  ClickProxy — lightweight click forwarder added at runtime by Navigation.
//  GetCurrentAction() lets MusicManager / LevelAudioManager wrap the existing
//  action without losing the original navigation callback.

public class ClickProxy : MonoBehaviour, IPointerClickHandler
{
    private System.Action onClickAction;

    public void Setup(System.Action action) => onClickAction = action;

    /// <summary>Returns the action currently stored in this proxy (may be null).</summary>
    public System.Action GetCurrentAction() => onClickAction;

    public void OnPointerClick(PointerEventData eventData) => onClickAction?.Invoke();
}

public class Navigation : MonoBehaviour, IPointerClickHandler
{
    [System.Serializable]
    public class NavigationData
    {
        [Tooltip("The GameObject you want to click (Can be an Image, Button, Sprite, or 3D Object)")]
        public GameObject clickableObject;

        [SceneName]
        public string sceneName;
    }

    [Header("Page Manager")]
    [SerializeField] private PageManager pageManager;

    [Header("Universal Click Targets")]
    public NavigationData[] navigationTargets;

    private void Start()
    {
        if (pageManager == null)
        {
            pageManager = Object.FindFirstObjectByType<PageManager>();
        }

        foreach (NavigationData data in navigationTargets)
        {
            if (data.clickableObject != null && data.clickableObject != gameObject)
            {
                ClickProxy proxy = data.clickableObject.GetComponent<ClickProxy>();
                if (proxy == null) proxy = data.clickableObject.AddComponent<ClickProxy>();

                string targetScene = data.sceneName;
                proxy.Setup(() => TriggerNavigation(targetScene));
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        foreach (NavigationData data in navigationTargets)
        {
            if (data.clickableObject == gameObject && !string.IsNullOrEmpty(data.sceneName))
            {
                TriggerNavigation(data.sceneName);
                break;
            }
        }
    }
    public void OnExitClicked()
    {
        PageManager.Instance.QuitApplication();
    }

    private void TriggerNavigation(string targetScene)
    {
        if (pageManager != null && !string.IsNullOrEmpty(targetScene))
        {
            pageManager.ChangeUI(targetScene);
        }
    }
}