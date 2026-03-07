namespace StripeTerminalBackend.Controllers;

public record CreatePaymentRequest(long Amount, string? Currency, string? Description);
public record PaymentIntentActionRequest(string PaymentIntentId);