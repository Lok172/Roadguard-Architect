using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RoadguardAPI.Models;

public class LevelResult
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("Player")]
    public int PlayerId { get; set; }

    public int LevelNumber { get; set; }

    public int SafetyScore { get; set; }

    public int DaysUsed { get; set; }

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    public Player Player { get; set; } = null!;
}