using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoadguardAPI.Data;
using RoadguardAPI.Models;

namespace RoadguardAPI.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly RoadguardContext _context;

    public AuthController(RoadguardContext context)
    {
        _context = context;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var existing = await _context.Players
            .FirstOrDefaultAsync(x => x.PlayerName == request.Username);

        if (existing != null)
        {
            return Ok(new AuthResponse
            {
                UserId = existing.PlayerID,
                Username = existing.PlayerName
            });
        }

        var player = new Player
        {
            PlayerName = request.Username
        };

        _context.Players.Add(player);
        await _context.SaveChangesAsync();

        return Ok(new AuthResponse
        {
            UserId = player.PlayerID,
            Username = player.PlayerName
        });
    }
}