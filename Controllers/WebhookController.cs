using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace StripeTerminalBackend.Controllers;

[ApiController]
[Route("webhook")]
public class WebhookController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IConfiguration config,
        ILogger<WebhookController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Handle()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"];
        var secret = _config["Stripe:WebhookSecret"]
            ?? throw new InvalidOperationException("Stripe:WebhookSecret is missing.");

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, signature, secret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Webhook signature validation failed: {Message}", ex.Message);
            return BadRequest(new { error = "Invalid webhook signature." });
        }

        _logger.LogInformation("Received Stripe event: {Type}", stripeEvent.Type);

        switch (stripeEvent.Type)
        {
            case EventTypes.PaymentIntentSucceeded:
                var succeeded = stripeEvent.Data.Object as PaymentIntent;
                _logger.LogInformation(
                    "PaymentIntent {Id} succeeded. Amount: {Amount} {Currency}.",
                    succeeded?.Id, succeeded?.Amount, succeeded?.Currency);
                break;

            case EventTypes.PaymentIntentPaymentFailed:
                var failed = stripeEvent.Data.Object as PaymentIntent;
                _logger.LogWarning(
                    "PaymentIntent {Id} failed. Reason: {Reason}.",
                    failed?.Id, failed?.LastPaymentError?.Message);
                break;

            case EventTypes.PaymentIntentCanceled:
                var cancelled = stripeEvent.Data.Object as PaymentIntent;
                _logger.LogInformation(
                    "PaymentIntent {Id} was cancelled.",
                    cancelled?.Id);
                break;

            default:
                _logger.LogInformation("Unhandled event type: {Type}", stripeEvent.Type);
                break;
        }

        return Ok();
    }
}