using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StripeTerminalBackend.Models;
using StripeTerminalBackend.Services;
using System.Security.Claims;

namespace StripeTerminalBackend.Controllers;

[ApiController]
[Route("events")]
[Authorize]
public class EventsController : ControllerBase
{
    private readonly EventService _events;
    private readonly TipService _tips;
    private readonly ILogger<EventsController> _logger;

    public EventsController(EventService events, TipService tips, ILogger<EventsController> logger)
    {
        _events = events;
        _tips = tips;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("User ID not found in token.");

    // ── POST /events ──────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventDto dto)
    {
        var ev = await _events.CreateEventAsync(UserId, new(
            Name: dto.Name,
            Date: DateOnly.Parse(dto.Date),
            Time: dto.Time is not null ? TimeOnly.ParseExact(dto.Time, "HH:mm") : null,
            Location: dto.Location,
            Description: dto.Description,
            TipOptions: dto.TipOptions
        ));

        _logger.LogInformation("Event {Id} created by user {UserId}.", ev.Id, UserId);
        return Ok(MapEvent(ev));
    }

    // ── GET /events ───────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAllEvents()
    {
        var result = await _events.GetAllEventsAsync(UserId);
        return Ok(new
        {
            upcoming = result.Upcoming.Select(MapEvent),
            active = result.Active.Select(MapEvent),
            past = result.Past.Select(MapEvent),
        });
    }

    // ── GET /events/upcoming ──────────────────────────────────
    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming()
    {
        var events = await _events.GetByStatusAsync(UserId, EventStatus.Upcoming);
        return Ok(events.Select(MapEvent));
    }

    // ── GET /events/active ────────────────────────────────────
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var events = await _events.GetByStatusAsync(UserId, EventStatus.Active);
        return Ok(events.Select(MapEvent));
    }

    // ── GET /events/past ──────────────────────────────────────
    [HttpGet("past")]
    public async Task<IActionResult> GetPast()
    {
        var events = await _events.GetByStatusAsync(UserId, EventStatus.Past);
        return Ok(events.Select(MapEvent));
    }

    // ── POST /events/:id/start ────────────────────────────────
    [HttpPost("{id}/start")]
    public async Task<IActionResult> StartEvent(string id)
    {
        var ev = await _events.StartEventAsync(UserId, id);
        _logger.LogInformation("Event {Id} started by user {UserId}.", ev.Id, UserId);
        return Ok(MapEvent(ev));
    }

    // ── POST /events/:id/end ──────────────────────────────────
    [HttpPost("{id}/end")]
    public async Task<IActionResult> EndEvent(string id)
    {
        var ev = await _events.EndEventAsync(UserId, id);
        _logger.LogInformation("Event {Id} ended by user {UserId}.", ev.Id, UserId);
        return Ok(MapEvent(ev));
    }

    // ── GET /stats/home ───────────────────────────────────────
    [HttpGet("/stats/home")]
    public async Task<IActionResult> GetHomeStats()
    {
        var total = await _events.GetTotalProfitAsync(UserId);
        return Ok(new { totalProfit = total });
    }

    // ── GET /events/:id/tips ──────────────────────────────────
    [HttpGet("{id}/tips")]
    public async Task<IActionResult> GetEventTips(string id)
    {
        var summary = await _tips.GetEventTipsAsync(UserId, id);
        return Ok(new
        {
            totalAmount = summary.TotalAmount,
            tipsCollected = summary.TipsCollected,
            tips = summary.Tips.Select(t => new
            {
                id = t.Id,
                amount = t.Amount,
                createdAt = t.CreatedAt,
            }),
        });
    }

    // ── Map to response shape ─────────────────────────────────
    private static object MapEvent(Event e) => new
    {
        id = e.Id,
        name = e.Name,
        date = e.Date.ToString("MMM dd, yyyy"),
        time = e.Time?.ToString("h:mm tt"),
        location = e.Location,
        description = e.Description,
        tipOptions = e.TipOptions,
        status = e.Status.ToString().ToLower(),
        tipsCollected = e.TipsCollected,
        totalAmount = e.TotalAmount,
        startedAt = e.StartedAt,
        endedAt = e.EndedAt,
    };
}

// ── Request DTO ───────────────────────────────────────────────
public record CreateEventDto(
    string Name,
    string Date,
    string? Time,
    string Location,
    string? Description,
    int[] TipOptions
);