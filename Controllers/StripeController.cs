using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Terminal;
using StripeTerminalBackend.Data;
using StripeTerminalBackend.Services;

namespace StripeTerminalBackend.Controllers;

[ApiController]
[Route("/")]
[Authorize]
public class StripeController : ControllerBase
{
    private readonly ILogger<StripeController> _logger;
    private readonly TipService _tips;
    private readonly AppDbContext _db;

    public StripeController(
        ILogger<StripeController> logger,
        TipService tips,
        AppDbContext db)
    {
        _logger = logger;
        _tips = tips;
        _db = db;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── POST /connection_token ────────────────────────────────
    [HttpPost("connection_token")]
    public async Task<IActionResult> CreateConnectionToken()
    {
        var service = new ConnectionTokenService();
        var token = await service.CreateAsync(new ConnectionTokenCreateOptions());
        _logger.LogInformation("Connection token created.");
        return Ok(new { secret = token.Secret });
    }

    // ── POST /create_payment_intent ───────────────────────────
    [HttpPost("create_payment_intent")]
    public async Task<IActionResult> CreatePaymentIntent(
        [FromBody] CreatePaymentRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest(new { message = "Amount must be greater than 0 cents." });

        if (string.IsNullOrWhiteSpace(request.EventId))
            return BadRequest(new { message = "eventId is required." });

        // ── Load event + professional ─────────────────────────
        var ev = await _db.Events.FindAsync(request.EventId);
        if (ev == null)
            return NotFound(new { message = "Event not found." });

        var professional = await _db.Users.FindAsync(ev.UserId);
        if (professional == null)
            return NotFound(new { message = "Professional not found." });

        // ── Validate onboarding ───────────────────────────────
        if (string.IsNullOrEmpty(professional.StripeAccountId))
            return BadRequest(new
            {
                message = "Professional has not set up their payment account yet.",
            });

        if (!professional.OnboardingComplete)
            return BadRequest(new
            {
                message = "Professional has not completed payment setup.",
            });

        // ── 5% platform fee ───────────────────────────────────
        var applicationFee = (long)Math.Round(request.Amount * 0.05);

        var options = new PaymentIntentCreateOptions
        {
            Amount = request.Amount,
            Currency = request.Currency ?? "usd",
            Description = request.Description,
            PaymentMethodTypes = new List<string> { "card_present" },
            CaptureMethod = "manual",

            // ── Stripe Connect routing — Goal 4 ───────────────
            ApplicationFeeAmount = applicationFee,
            OnBehalfOf = professional.StripeAccountId,
            TransferData = new PaymentIntentTransferDataOptions
            {
                Destination = professional.StripeAccountId,
            },

            Metadata = new Dictionary<string, string>
            {
                { "created_by",  User.Identity?.Name ?? "unknown" },
                { "event_id",    request.EventId },
                { "platform_fee", applicationFee.ToString() },
            },
        };

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(options);

        _logger.LogInformation(
            "PaymentIntent {Id} created — amount: {Amount}, fee: {Fee}, account: {Account}.",
            intent.Id, intent.Amount, applicationFee, professional.StripeAccountId);

        return Ok(new
        {
            id = intent.Id,
            clientSecret = intent.ClientSecret,
            amount = intent.Amount,
            currency = intent.Currency,
            status = intent.Status,
        });
    }

    // ── POST /capture_payment_intent ──────────────────────────
    [HttpPost("capture_payment_intent")]
    public async Task<IActionResult> CapturePaymentIntent(
        [FromBody] CapturePaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentIntentId))
            return BadRequest(new { message = "paymentIntentId is required." });

        if (string.IsNullOrWhiteSpace(request.EventId))
            return BadRequest(new { message = "eventId is required." });

        // ── Load event + professional ─────────────────────────
        var ev = await _db.Events.FindAsync(request.EventId);
        if (ev == null)
            return NotFound(new { message = "Event not found." });

        var professional = await _db.Users.FindAsync(ev.UserId);
        if (professional == null)
            return NotFound(new { message = "Professional not found." });

        // ── Capture on behalf of connected account ────────────
        // CRITICAL: RequestOptions required because the PaymentIntent was
        // created with OnBehalfOf — capture must use the same account.
        var requestOptions = new RequestOptions
        {
            StripeAccount = professional.StripeAccountId,
        };

        var service = new PaymentIntentService();
        var intent = await service.CaptureAsync(
            request.PaymentIntentId,
            new PaymentIntentCaptureOptions(),
            requestOptions
        );

        // ── Record tip in DB ──────────────────────────────────
        await _tips.RecordTipAsync(UserId, new(
            EventId: request.EventId,
            Amount: intent.Amount,
            PaymentIntentId: intent.Id
        ));

        _logger.LogInformation(
            "PaymentIntent {Id} captured and tip recorded — amount: {Amount}, account: {Account}.",
            intent.Id, intent.Amount, professional.StripeAccountId);

        return Ok(new { id = intent.Id, amount = intent.Amount, status = intent.Status });
    }
}
