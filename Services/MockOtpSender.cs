// ============================================================
// MOCK OTP SENDER — FOR DEVELOPMENT USE ONLY
// SendGrid has been temporarily removed pending client's OTP provider.
// When the client provides OTP service credentials:
//   1. Create ClientOtpSender.cs implementing IOtpSender
//   2. Replace MockOtpSender registration in Program.cs with ClientOtpSender
//   3. See Goal 11 in the implementation plan
// ============================================================

namespace StripeTerminalBackend.Services;

public class MockOtpSender : IOtpSender
{
    private readonly ILogger<MockOtpSender> _logger;

    public MockOtpSender(ILogger<MockOtpSender> logger) => _logger = logger;

    public Task SendOtpAsync(string toEmail, string code)
    {
        _logger.LogWarning("==================================================");
        _logger.LogWarning("OTP MOCK — Email: {Email} | Code: {Code}", toEmail, code);
        _logger.LogWarning("==================================================");
        return Task.CompletedTask;
    }
}