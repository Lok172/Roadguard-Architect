using System.ComponentModel.DataAnnotations;

namespace RoadguardAPI.Models;

public class Player
{
    [Key]
    public int PlayerID { get; set; }

    [Required]
    [MaxLength(50)]
    public string PlayerName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<LevelResult> LevelResults { get; set; } = new();
}