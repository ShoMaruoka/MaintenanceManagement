using Microsoft.AspNetCore.Mvc;
using MaintenanceManagement.Api.Models;
using MaintenanceManagement.Api.Services;

namespace MaintenanceManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly DatabaseService _db;

    public HistoryController(DatabaseService db)
    {
        _db = db;
    }

    [HttpGet("sessions")]
    public IActionResult GetSessions([FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 500);
        var sessions = _db.GetRecentSessions(limit);
        return Ok(sessions);
    }

    [HttpGet("sessions/{sessionId:long}")]
    public IActionResult GetSession(long sessionId)
    {
        var session = _db.GetSessionById(sessionId);
        if (session is null) return NotFound();

        session.Details = _db.GetSessionDetails(sessionId);
        return Ok(session);
    }

    [HttpGet("stats")]
    public IActionResult GetStats([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        return Ok(_db.GetDashboardStats(days));
    }

    [HttpGet("prepare")]
    public IActionResult GetPrepareLogs([FromQuery] int limit = 20)
    {
        var logs = _db.GetRecentPrepLogs(limit);
        return Ok(logs);
    }

    [HttpGet("pilot-runs")]
    public IActionResult GetPilotRuns([FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        return Ok(_db.GetRecentPilotRuns(limit));
    }

    [HttpGet("pilot-runs/{runId}")]
    public IActionResult GetPilotRun(string runId)
    {
        var run = _db.GetPilotRunById(runId);
        if (run is null) return NotFound();
        return Ok(run);
    }
}
