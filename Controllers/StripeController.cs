using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Terminal;
using StripeTerminalBackend.Services;

namespace StripeTerminalBackend.Controllers;

[ApiController]
[Route("/")]
[Authorize]
public class StripeController : ControllerBase
{
    private readonly ILogger<StripeController> _logger;
    private readonly TipService _tips;

    public StripeController(ILogger<StripeController> logger, TipService tips)
    {
        _logger = logger;
        _tips = tips;
    }

    [HttpPost("connection_token")]
    public async Task<IActionResult> CreateConnectionToken()
    {
        var service = new ConnectionTokenService();
        var token = await service.CreateAsync(new ConnectionTokenCreateOptions());
        _logger.LogInformation("Connection token created.");
        return Ok(new { secret = token.Secret });
    }

    [HttpPost("create_payment_intent")]
    public async Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest(new { message = "Amount must be greater than 0 cents." });

        var options = new PaymentIntentCreateOptions
        {
            Amount = request.Amount,
            Currency = request.Currency ?? "usd",
            Description = request.Description,
            PaymentMethodTypes = new List<string> { "card_present" },
            CaptureMethod = "manual",
            Metadata = new Dictionary<string, string>
            {
                { "created_by", User.Identity?.Name ?? "unknown" }
            }
        };

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(options);

        _logger.LogInformation("PaymentIntent {Id} created for {Amount} {Currency}.",
            intent.Id, intent.Amount, intent.Currency);

        return Ok(new
        {
            id = intent.Id,
            clientSecret = intent.ClientSecret,
            amount = intent.Amount,
            currency = intent.Currency,
            status = intent.Status,
        });
    }

    [HttpPost("capture_payment_intent")]
    public async Task<IActionResult> CapturePaymentIntent([FromBody] CapturePaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentIntentId))
            return BadRequest(new { message = "paymentIntentId is required." });

        if (string.IsNullOrWhiteSpace(request.EventId))
            return BadRequest(new { message = "eventId is required." });

        var service = new PaymentIntentService();
        var intent = await service.CaptureAsync(request.PaymentIntentId);

        // ── Record the tip immediately after successful capture ───────────
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await _tips.RecordTipAsync(userId, new(
            EventId: request.EventId,
            Amount: intent.Amount,
            PaymentIntentId: intent.Id
        ));

        _logger.LogInformation(
            "PaymentIntent {Id} captured and tip recorded. Amount: {Amount}.",
            intent.Id, intent.Amount);

        return Ok(new { id = intent.Id, amount = intent.Amount, status = intent.Status });
    }
}