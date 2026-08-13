using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View binding for the Planning Phase panel. Attach this to the level UI
/// Canvas, assign its panel and button, and keep the panel inactive by default.
/// </summary>
public class PlanningPhaseManager : MonoBehaviour
{
    [Header("View References")]
    [SerializeField] private GameObject planningPanel;
    [SerializeField] private Button confirmLayoutButton;
    [SerializeField] private TMP_Text confirmButtonLabel;

    private PlanningPhaseViewModel _vm;

    private void Awake()
    {
        _vm = new PlanningPhaseViewModel();
        _vm.VisibilityChanged += ApplyVisibility;

        if (confirmLayoutButton != null)
            confirmLayoutButton.onClick.AddListener(_vm.ConfirmLayout);

        ApplyVisibility(false);
    }

    private IEnumerator Start()
    {
        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.OnPlanningPhaseStarted.AddListener(_vm.Show);
        GameManager.Instance.OnDayStarted.AddListener(_vm.Hide);

        // Handles the case where GameManager initialized before this view.
        if (GameManager.Instance.PlanningPhaseActive)
            _vm.Show();
    }

    private void OnDestroy()
    {
        if (confirmLayoutButton != null)
            confirmLayoutButton.onClick.RemoveListener(_vm.ConfirmLayout);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPlanningPhaseStarted.RemoveListener(_vm.Show);
            GameManager.Instance.OnDayStarted.RemoveListener(_vm.Hide);
        }

        if (_vm != null)
            _vm.VisibilityChanged -= ApplyVisibility;
    }

    private void ApplyVisibility(bool visible)
    {
        if (planningPanel != null)
            planningPanel.SetActive(visible);

        if (confirmButtonLabel != null)
            confirmButtonLabel.text = "Start Day";
    }
}
