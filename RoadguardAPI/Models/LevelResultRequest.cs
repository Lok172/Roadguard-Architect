namespace RoadguardAPI.Models;

public class LevelResultRequest
{
    public int PlayerId { get; set; }

    public int LevelNumber { get; set; }

    public int SafetyScore { get; set; }

    public int DaysUsed { get; set; }
}