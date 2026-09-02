using Microsoft.AspNetCore.Mvc;
using RoadguardAPI.Data;
using RoadguardAPI.Models;

namespace RoadguardAPI.Controllers;

[ApiController]
[Route("api/results")]
public class ResultsController : ControllerBase
{
    private readonly RoadguardContext _context;

    public ResultsController(RoadguardContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<ActionResult> AddResult(LevelResultRequest request)
    {
        var result = new LevelResult
        {
            PlayerId = request.PlayerId,
            LevelNumber = request.LevelNumber,
            SafetyScore = request.SafetyScore,
            DaysUsed = request.DaysUsed
        };

        _context.LevelResults.Add(result);
        await _context.SaveChangesAsync();

        return Ok(new { id = result.Id });
    }
}