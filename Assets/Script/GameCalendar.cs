using UnityEngine;
using TMPro;
using System;

public class GameCalendar : MonoBehaviour
{
    [Header("Time Settings")]
    [SerializeField] private float secondsPerDay = 2f;   // 2 real seconds = 1 in-game day
    [SerializeField] private int totalDays = 90;         // 3 months = 90 days

    [Header("UI References")]
    [SerializeField] private TMP_Text dateText;
    [SerializeField] private TMP_Text dayCounterText;

    private DateTime currentDate;
    private float timer;
    private int elapsedDays = 0;

    // True when the simulation reaches the maximum number of days
    public bool IsSimulationFinished => elapsedDays >= totalDays;

    private void Start()
    {
        // Automatically use the player's local computer date
        // Example: if today is 17/05/2026, the game starts from 17/05/2026.
        currentDate = DateTime.Today;

        // Initialize the UI immediately
        UpdateCalendarUI();
    }

    private void Update()
    {
        // Stop updating when the simulation is complete
        if (IsSimulationFinished)
            return;

        // Accumulate real time
        timer += Time.deltaTime;

        // Advance one in-game day every `secondsPerDay`
        while (timer >= secondsPerDay && !IsSimulationFinished)
        {
            timer -= secondsPerDay;
            AdvanceOneDay();
        }
    }

    private void AdvanceOneDay()
    {
        elapsedDays++;
        currentDate = currentDate.AddDays(1);

        UpdateCalendarUI();

        if (IsSimulationFinished)
        {
            Debug.Log("Simulation finished after " + totalDays + " days.");
            // TODO: Trigger your end-of-level or results screen here
        }
    }

    private void UpdateCalendarUI()
    {
        // Date example: 17/05/2026
        if (dateText != null)
        {
            dateText.text = currentDate.ToString("dd/MM/yyyy");
        }

        // Day counter example: (Day 10/90)
        if (dayCounterText != null)
        {
            dayCounterText.text = $"(Day {elapsedDays}/{totalDays})";
        }
    }

    // Optional helper methods
    public DateTime GetCurrentDate()
    {
        return currentDate;
    }

    public int GetElapsedDays()
    {
        return elapsedDays;
    }

    public void ResetCalendar()
    {
        timer = 0f;
        elapsedDays = 0;
        currentDate = DateTime.Today; // Reset to the user's current local date
        UpdateCalendarUI();
    }
}