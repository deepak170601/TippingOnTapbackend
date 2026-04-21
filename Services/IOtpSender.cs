namespace StripeTerminalBackend.Services;

public interface IOtpSender
{
    Task SendOtpAsync(string toEmail, string code);
}