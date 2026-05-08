using UnityEngine;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [System.Serializable]
    public class UIButtonData
    {
        public Button button;
        public string sceneName;
    }

    [Header("Page Manager")]
    [SerializeField] private PageManager pageManager;

    [Header("Buttons + Scene Names")]
    [SerializeField] private UIButtonData[] uiButtons;

    private void Start()
    {
        // Auto-find PageManager
        if (pageManager == null)
        {
            pageManager = FindObjectOfType<PageManager>();
        }

        // Assign listeners dynamically
        foreach (UIButtonData data in uiButtons)
        {
            if (data.button != null)
            {
                string targetScene = data.sceneName;

                data.button.onClick.RemoveAllListeners();

                data.button.onClick.AddListener(() =>
                {
                    pageManager.ChangeUI(targetScene);
                });
            }
        }
    }
}