public record CapturePaymentRequest(
    string PaymentIntentId,
    string EventId
);