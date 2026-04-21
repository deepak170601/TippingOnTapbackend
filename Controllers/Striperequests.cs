namespace StripeTerminalBackend.Controllers;

// ── Payment request DTOs ──────────────────────────────────────
public record CreatePaymentRequest(
    long Amount,
    string EventId,
    string? Currency = null,
    string? Description = null
);

public record CapturePaymentRequest(string PaymentIntentId, string EventId);