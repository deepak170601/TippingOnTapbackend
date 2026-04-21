using Microsoft.EntityFrameworkCore;
using StripeTerminalBackend.Data;
using StripeTerminalBackend.Models;

namespace StripeTerminalBackend.Services;

public record RecordTipRequest(
    string EventId,
    long Amount,         // cents
    string? PaymentIntentId
);

public record DailyEarnings(
    DateOnly Date,
    long Total,      // cents
    int TipCount
);

public record WalletSummary(
    long TotalAllTime,
    IEnumerable<DailyEarnings> Days
);

public record EventTipSummary(
    long TotalAmount,
    int TipsCollected,
    IEnumerable<Tip> Tips
);

public class TipService
{
    private readonly AppDbContext _db;

    public TipService(AppDbContext db) => _db = db;

    // ── Record tip ────────────────────────────────────────────────
    public async Task<Tip> RecordTipAsync(string userId, RecordTipRequest req)
    {
        var ev = await _db.Events.FindAsync(req.EventId)
            ?? throw new KeyNotFoundException("Event not found.");

        if (ev.Status != EventStatus.Active)
            throw new InvalidOperationException("Tips can only be recorded on active events.");

        if (ev.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this event.");

        var tip = new Tip
        {
            UserId = userId,
            EventId = req.EventId,
            Amount = req.Amount,
            PaymentIntentId = req.PaymentIntentId,
        };

        // NOTE: The 5% platform commission is enforced by Stripe via
        // ApplicationFeeAmount on the PaymentIntent (StripeController — Goal 4).
        // TotalAmount and TipsCollected are display counters only, not financial records.
        ev.TipsCollected++;
        ev.TotalAmount += req.Amount;

        _db.Tips.Add(tip);
        await _db.SaveChangesAsync();
        return tip;
    }

    // ── Wallet summary ────────────────────────────────────────
    public async Task<WalletSummary> GetWalletSummaryAsync(string userId)
    {
        var tips = await _db.Tips
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var days = tips
            .GroupBy(t => DateOnly.FromDateTime(t.CreatedAt))
            .Select(g => new DailyEarnings(
                Date: g.Key,
                Total: g.Sum(t => t.Amount),
                TipCount: g.Count()
            ))
            .OrderByDescending(d => d.Date);

        return new WalletSummary(
            TotalAllTime: tips.Sum(t => t.Amount),
            Days: days
        );
    }

    // ── Event tips ────────────────────────────────────────────
    public async Task<EventTipSummary> GetEventTipsAsync(string userId, string eventId)
    {
        var ev = await _db.Events.FindAsync(eventId)
            ?? throw new KeyNotFoundException("Event not found.");

        if (ev.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this event.");

        var tips = await _db.Tips
            .Where(t => t.EventId == eventId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return new EventTipSummary(
            TotalAmount: ev.TotalAmount,
            TipsCollected: ev.TipsCollected,
            Tips: tips
        );
    }
}