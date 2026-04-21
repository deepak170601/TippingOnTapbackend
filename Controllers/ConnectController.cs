using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using StripeTerminalBackend.Data;
using StripeTerminalBackend.Services;
using System.Security.Claims;

namespace StripeTerminalBackend.Controllers;

[ApiController]
[Route("connect")]
public class ConnectController : ControllerBase
{
    private readonly StripeConnectService _connect;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ConnectController> _logger;

    public ConnectController(
        StripeConnectService connect,
        AppDbContext db,
        IConfiguration config,
        ILogger<ConnectController> logger)
    {
        _connect = connect;
        _db = db;
        _config = config;
        _logger = logger;
    }

    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("User ID not found in token.");

    // ── POST /connect/onboard ─────────────────────────────────
    // Creates a connected account (if needed) and returns the
    // Stripe-hosted onboarding URL. Single-use, expires in 10 min.
    [HttpPost("onboard")]
    [Authorize]
    public async Task<IActionResult> Onboard()
    {
        try
        {
            // Step 1 — Ensure connected account exists
            await _connect.CreateConnectedAccountAsync(UserId);

            // Step 2 — Generate onboarding link
            var baseUrl = _config["App:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return StatusCode(500, new { message = "App:BaseUrl is not configured." });
            }

            var url = await _connect.GenerateOnboardingLinkAsync(
                userId: UserId,
                returnUrl: $"{baseUrl}/connect/return",
                refreshUrl: $"{baseUrl}/connect/refresh"
            );

            return Ok(new { url });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (StripeException ex)
        {
            _logger.LogError("Stripe error on /connect/onboard: {Message}", ex.Message);
            return StatusCode(500, new { message = "Stripe error. Please try again." });
        }
    }

    // ── GET /connect/return ───────────────────────────────────
    // Stripe redirects here after onboarding completes.
    // This endpoint redirects the browser to the app deep link.
    [HttpGet("return")]
    [AllowAnonymous]
    public IActionResult OnboardingReturn()
    {
        return Redirect("tippingonthego://connect/return");
    }

    // ── GET /connect/refresh ──────────────────────────────────
    // Stripe redirects here if the onboarding link expires.
    // This endpoint redirects the browser to the app deep link.
    [HttpGet("refresh")]
    [AllowAnonymous]
    public IActionResult OnboardingRefresh()
    {
        return Redirect("tippingonthego://connect/refresh");
    }

    // ── GET /connect/status ───────────────────────────────────
    // Returns the current onboarding and capability flags.
    // Safe to call before onboarding — returns all false if no account.
    [HttpGet("status")]
    [Authorize]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var user = await _db.Users.FindAsync(UserId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            // No account yet — return all false, do not throw
            if (string.IsNullOrEmpty(user.StripeAccountId))
            {
                return Ok(new
                {
                    onboardingComplete = false,
                    chargesEnabled = false,
                    payoutsEnabled = false,
                });
            }

            var (chargesEnabled, payoutsEnabled) =
                await _connect.GetAccountStatusAsync(UserId);

            return Ok(new
            {
                onboardingComplete = user.OnboardingComplete,
                chargesEnabled,
                payoutsEnabled,
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError("Stripe error on /connect/status: {Message}", ex.Message);
            return StatusCode(500, new { message = "Stripe error. Please try again." });
        }
    }

    // ── GET /connect/balance ──────────────────────────────────
    // Returns the connected account's available and pending balance in cents.
    // Requires onboarding to be complete.
    [HttpGet("balance")]
    [Authorize]
    public async Task<IActionResult> GetBalance()
    {
        try
        {
            var user = await _db.Users.FindAsync(UserId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            if (!user.OnboardingComplete)
                return BadRequest(new
                {
                    message = "Complete onboarding before checking balance.",
                });

            var (available, pending) = await _connect.GetConnectedBalanceAsync(UserId);

            return Ok(new { available, pending });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (StripeException ex)
        {
            _logger.LogError("Stripe error on /connect/balance: {Message}", ex.Message);
            return StatusCode(500, new { message = "Stripe error. Please try again." });
        }
    }

    // ── POST /connect/withdraw ────────────────────────────────
    // Initiates a payout to the professional's linked bank account.
    // amountCents = null → pays out full available balance.
    [HttpPost("withdraw")]
    [Authorize]
    public async Task<IActionResult> Withdraw([FromBody] WithdrawRequest request)
    {
        try
        {
            var user = await _db.Users.FindAsync(UserId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            if (!user.OnboardingComplete)
                return BadRequest(new { message = "Complete onboarding first." });

            if (!user.PayoutsEnabled)
                return BadRequest(new { message = "Payouts not enabled on your account." });

            var payoutId = await _connect.CreatePayoutAsync(UserId, request.AmountCents);

            return Ok(new
            {
                payoutId,
                message = "Payout initiated successfully.",
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (StripeException ex)
        {
            _logger.LogError("Stripe error on /connect/withdraw: {Message}", ex.Message);
            return StatusCode(500, new { message = "Stripe error. Please try again." });
        }
    }

    // ── POST /connect/webhook ─────────────────────────────────
    // Receives Stripe events. No auth — Stripe signs the payload.
    // ALWAYS return 200 — Stripe retries on any non-200 response.
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        string body;
        try
        {
            using var reader = new StreamReader(Request.Body);
            body = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to read webhook body: {Message}", ex.Message);
            return BadRequest();
        }

        var signatureHeader = Request.Headers["Stripe-Signature"].ToString();
        var webhookSecret = _config["Stripe:WebhookSecret"];

        if (string.IsNullOrEmpty(webhookSecret))
        {
            _logger.LogError("Stripe:WebhookSecret is not configured.");
            return StatusCode(500);
        }

        // Validate Stripe signature — return 400 if invalid
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                body, signatureHeader, webhookSecret,
                throwOnApiVersionMismatch: false
            );
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Invalid Stripe webhook signature: {Message}", ex.Message);
            return BadRequest();
        }

        _logger.LogInformation("Received Stripe webhook event: {Type}", stripeEvent.Type);

        // ── Handle account.updated ────────────────────────────
        if (stripeEvent.Type == "account.updated")
        {
            var account = stripeEvent.Data.Object as Account;
            if (account == null)
            {
                _logger.LogWarning("account.updated event had null Account object.");
                return Ok(); // Always 200
            }

            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.StripeAccountId == account.Id);

            if (user == null)
            {
                // Log but do not block — Stripe must receive 200
                _logger.LogWarning(
                    "account.updated: no user found for account {AccountId}.",
                    account.Id);
                return Ok();
            }

            user.ChargesEnabled = account.ChargesEnabled;
            user.PayoutsEnabled = account.PayoutsEnabled;

            if (account.ChargesEnabled && account.PayoutsEnabled)
                user.OnboardingComplete = true;

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Synced account {AccountId} — charges: {Charges}, payouts: {Payouts}, complete: {Complete}.",
                account.Id, user.ChargesEnabled, user.PayoutsEnabled, user.OnboardingComplete);
        }

        return Ok();
    }
}

// ── Request DTO ───────────────────────────────────────────────
public record WithdrawRequest(long? AmountCents);