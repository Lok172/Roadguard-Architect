using UnityEngine;
using UnityEngine.EventSystems;

public class ClickProxy : MonoBehaviour, IPointerClickHandler
{
    private System.Action onClickAction;
    public void Setup(System.Action action) => onClickAction = action;
    public void OnPointerClick(PointerEventData eventData) => onClickAction?.Invoke();
}
public class UniversalNavigation : MonoBehaviour, IPointerClickHandler
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

    private void TriggerNavigation(string targetScene)
    {
        if (pageManager != null && !string.IsNullOrEmpty(targetScene))
        {
            pageManager.ChangeUI(targetScene);
        }
    }
}

