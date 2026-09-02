using Microsoft.AspNetCore.Mvc;

namespace RoadguardAPI.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { rank = 0 });
}