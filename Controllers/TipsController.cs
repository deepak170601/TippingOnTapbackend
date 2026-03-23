using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StripeTerminalBackend.Services;
using System.Security.Claims;

namespace StripeTerminalBackend.Controllers;

[ApiController]
[Route("tips")]
[Authorize]
public class TipsController : ControllerBase
{
    private readonly TipService _tips;
    private readonly ILogger<TipsController> _logger;

    public TipsController(TipService tips, ILogger<TipsController> logger)
    {
        _tips = tips;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("User ID not found in token.");

    // ── POST /tips ────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> RecordTip([FromBody] RecordTipDto dto)
    {
        var tip = await _tips.RecordTipAsync(UserId, new(
            EventId: dto.EventId,
            Amount: dto.Amount,
            PaymentIntentId: dto.PaymentIntentId
        ));

        _logger.LogInformation(
            "Tip {Id} of {Amount} cents recorded for event {EventId}.",
            tip.Id, tip.Amount, tip.EventId);

        return Ok(new
        {
            id = tip.Id,
            amount = tip.Amount,
            eventId = tip.EventId,
            createdAt = tip.CreatedAt,
        });
    }

    // ── GET /wallet ───────────────────────────────────────────
    [HttpGet("/wallet")]
    public async Task<IActionResult> GetWallet()
    {
        var summary = await _tips.GetWalletSummaryAsync(UserId);
        return Ok(new
        {
            totalAllTime = summary.TotalAllTime,
            days = summary.Days.Select(d => new
            {
                date = d.Date.ToString("MMM dd, yyyy"),
                total = d.Total,
                tipCount = d.TipCount,
            }),
        });
    }
}

// ── Request DTO ───────────────────────────────────────────────
public record RecordTipDto(
    string EventId,
    long Amount,
    string? PaymentIntentId
);