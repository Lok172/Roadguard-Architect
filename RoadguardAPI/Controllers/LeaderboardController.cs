using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoadguardAPI.Data;

namespace RoadguardAPI.Controllers;

[ApiController]
[Route("api/leaderboard")]
public class LeaderboardController : ControllerBase
{
    private readonly RoadguardContext _context;

    public LeaderboardController(RoadguardContext context)
    {
        _context = context;
    }

    // Top 10 for a specific level
    [HttpGet("top10/{level}")]
    public async Task<IActionResult> GetTop10(int level)
    {
        var results = await _context.LevelResults
            .Include(r => r.Player)
            .Where(r => r.LevelNumber == level)
            .GroupBy(r => new
            {
                r.PlayerId,
                r.Player.PlayerName
            })
            .Select(g => new
            {
                PlayerId = g.Key.PlayerId,
                PlayerName = g.Key.PlayerName,
                SafetyScore = g.Max(x => x.SafetyScore),
                DaysUsed = g.OrderByDescending(x => x.SafetyScore)
                            .First().DaysUsed
            })
            .OrderByDescending(x => x.SafetyScore)
            .Take(10)
            .ToListAsync();

        var ranked = results.Select((x, index) => new
        {
            Rank = index + 1,
            x.PlayerId,
            x.PlayerName,
            x.SafetyScore,
            x.DaysUsed
        });

        return Ok(ranked);
    }

    // Current player's best score for a level
    [HttpGet("player/{playerId}/level/{level}")]
    public async Task<IActionResult> GetPlayerBestScore(
        int playerId,
        int level)
    {
        var bestScore = await _context.LevelResults
            .Where(r =>
                r.PlayerId == playerId &&
                r.LevelNumber == level)
            .MaxAsync(r => (int?)r.SafetyScore);

        return Ok(new
        {
            BestScore = bestScore ?? 0
        });
    }

    // Current player's rank for a level
    [HttpGet("rank/{playerId}/level/{level}")]
    public async Task<IActionResult> GetPlayerRank(
        int playerId,
        int level)
    {
        var leaderboard = await _context.LevelResults
            .Include(r => r.Player)
            .Where(r => r.LevelNumber == level)
            .GroupBy(r => r.PlayerId)
            .Select(g => new
            {
                PlayerId = g.Key,
                BestScore = g.Max(x => x.SafetyScore)
            })
            .OrderByDescending(x => x.BestScore)
            .ToListAsync();

        var rank = leaderboard
            .FindIndex(x => x.PlayerId == playerId) + 1;

        return Ok(new
        {
            Rank = rank == 0 ? -1 : rank
        });
    }
}