using Microsoft.EntityFrameworkCore;
using StripeTerminalBackend.Data;
using StripeTerminalBackend.Models;

namespace StripeTerminalBackend.Services;

public record CreateEventRequest(
    string Name,
    DateOnly Date,
    TimeOnly? Time,
    string Location,
    string? Description,
    int[] TipOptions
);

public record EventsResponse(
    IEnumerable<Event> Upcoming,
    IEnumerable<Event> Active,
    IEnumerable<Event> Past
);

public class EventService
{
    private readonly AppDbContext _db;

    public EventService(AppDbContext db) => _db = db;

    // ── Create ────────────────────────────────────────────────
    public async Task<Event> CreateEventAsync(string userId, CreateEventRequest req)
    {
        if (req.Date < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new InvalidOperationException("Event date cannot be in the past.");

        if (req.TipOptions.Length == 0)
            throw new InvalidOperationException("At least one tip option is required.");

        var ev = new Event
        {
            UserId = userId,
            Name = req.Name.Trim(),
            Date = req.Date,
            Time = req.Time,
            Location = req.Location.Trim(),
            Description = req.Description?.Trim(),
            TipOptions = req.TipOptions,
            Status = EventStatus.Upcoming,
        };

        _db.Events.Add(ev);
        await _db.SaveChangesAsync();
        return ev;
    }

    // ── Get all (three groups) ────────────────────────────────
    public async Task<EventsResponse> GetAllEventsAsync(string userId)
    {
        var all = await _db.Events
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        return new EventsResponse(
            Upcoming: all.Where(e => e.Status == EventStatus.Upcoming),
            Active: all.Where(e => e.Status == EventStatus.Active),
            Past: all.Where(e => e.Status == EventStatus.Past)
        );
    }

    // ── Get by status ─────────────────────────────────────────
    public async Task<IEnumerable<Event>> GetByStatusAsync(string userId, EventStatus status)
        => await _db.Events
            .Where(e => e.UserId == userId && e.Status == status)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

    // ── Start ─────────────────────────────────────────────────
    public async Task<Event> StartEventAsync(string userId, string eventId)
    {
        var ev = await GetOwnedEventAsync(userId, eventId);

        if (ev.Status != EventStatus.Upcoming)
            throw new InvalidOperationException("Only upcoming events can be started.");

        // Only one active event at a time
        var alreadyActive = await _db.Events
            .AnyAsync(e => e.UserId == userId && e.Status == EventStatus.Active);

        if (alreadyActive)
            throw new InvalidOperationException("You already have an active event. End it before starting another.");

        ev.Status = EventStatus.Active;
        ev.StartedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ev;
    }

    // ── End ───────────────────────────────────────────────────
    public async Task<Event> EndEventAsync(string userId, string eventId)
    {
        var ev = await GetOwnedEventAsync(userId, eventId);

        if (ev.Status != EventStatus.Active)
            throw new InvalidOperationException("Only active events can be ended.");

        ev.Status = EventStatus.Past;
        ev.EndedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ev;
    }

    // ── Home stats ────────────────────────────────────────────
    public async Task<long> GetTotalProfitAsync(string userId)
        => await _db.Tips
            .Where(t => t.UserId == userId)
            .SumAsync(t => (long?)t.Amount) ?? 0;

    // ── Helper ────────────────────────────────────────────────
    private async Task<Event> GetOwnedEventAsync(string userId, string eventId)
    {
        var ev = await _db.Events.FindAsync(eventId)
            ?? throw new KeyNotFoundException("Event not found.");

        if (ev.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this event.");

        return ev;
    }
}