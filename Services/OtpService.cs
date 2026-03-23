using Microsoft.EntityFrameworkCore;
using SendGrid;
using SendGrid.Helpers.Mail;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using StripeTerminalBackend.Data;
using StripeTerminalBackend.Models;

namespace StripeTerminalBackend.Services;

public class OtpService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<OtpService> _logger;

    public OtpService(AppDbContext db, IConfiguration config, ILogger<OtpService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    // ── Send phone OTP via Twilio SMS ─────────────────────────
    public async Task SendPhoneOtpAsync(string phoneNumber)
    {
        var code = await GenerateAndSaveOtpAsync(phoneNumber, OtpType.Phone);
        await SendSmsAsync(phoneNumber, code);
        _logger.LogInformation("Phone OTP sent to {Phone}.", phoneNumber);
    }

    // ── Send email OTP via SendGrid ───────────────────────────
    public async Task SendEmailOtpAsync(string email)
    {
        var code = await GenerateAndSaveOtpAsync(email, OtpType.Email);
        await SendEmailAsync(email, code);
        _logger.LogInformation("Email OTP sent to {Email}.", email);
    }

    // ── Verify any OTP ────────────────────────────────────────
    public async Task<bool> VerifyOtpAsync(string target, string code, OtpType type)
    {
        var otp = await _db.OtpCodes
            .Where(o => o.Target == NormaliseTarget(target, type)
                     && o.Code == code
                     && o.Type == type
                     && o.UsedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp == null || otp.IsExpired) { return false; }

        otp.VerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Check if verified (not yet consumed) ──────────────────
    public async Task<bool> IsVerifiedAsync(string target, OtpType type)
        => await _db.OtpCodes
            .AnyAsync(o => o.Target == NormaliseTarget(target, type)
                        && o.Type == type
                        && o.VerifiedAt != null
                        && o.UsedAt == null
                        && o.ExpiresAt > DateTime.UtcNow);

    // ── Consume after use ─────────────────────────────────────
    public async Task ConsumeOtpAsync(string target, OtpType type)
    {
        var otp = await _db.OtpCodes
            .Where(o => o.Target == NormaliseTarget(target, type)
                     && o.Type == type
                     && o.VerifiedAt != null
                     && o.UsedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp != null)
        {
            otp.UsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    // ── Internal: generate + save ─────────────────────────────
    private async Task<string> GenerateAndSaveOtpAsync(string target, OtpType type)
    {
        var normalised = NormaliseTarget(target, type);

        // Invalidate existing unused OTPs for this target+type
        var existing = await _db.OtpCodes
            .Where(o => o.Target == normalised && o.Type == type && o.UsedAt == null)
            .ToListAsync();
        _db.OtpCodes.RemoveRange(existing);

        var code = Random.Shared.Next(100000, 999999).ToString();

        _db.OtpCodes.Add(new OtpCode
        {
            Target = normalised,
            Type = type,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        });

        await _db.SaveChangesAsync();
        return code;
    }

    // ── Twilio SMS ────────────────────────────────────────────
    private Task SendSmsAsync(string phoneNumber, string code)
    {
        var accountSid = _config["Twilio:AccountSid"]
            ?? throw new InvalidOperationException("Twilio:AccountSid missing.");
        var authToken = _config["Twilio:AuthToken"]
            ?? throw new InvalidOperationException("Twilio:AuthToken missing.");
        var fromNumber = _config["Twilio:FromNumber"]
            ?? throw new InvalidOperationException("Twilio:FromNumber missing.");

        TwilioClient.Init(accountSid, authToken);

        MessageResource.Create(
            to: new Twilio.Types.PhoneNumber(phoneNumber),
            from: new Twilio.Types.PhoneNumber(fromNumber),
            body: $"Your Tipping On The Go verification code is: {code}. Valid for 10 minutes."
        );

        return Task.CompletedTask;
    }

    // ── SendGrid Email ────────────────────────────────────────
    private async Task SendEmailAsync(string toEmail, string code)
    {
        var apiKey = _config["SendGrid:ApiKey"] ?? throw new InvalidOperationException("SendGrid:ApiKey missing.");
        var fromEmail = _config["SendGrid:FromEmail"] ?? "noreply@tippingonthego.com";
        var fromName = _config["SendGrid:FromName"] ?? "Tipping On The Go";

        var client = new SendGridClient(apiKey);
        var from = new EmailAddress(fromEmail, fromName);
        var to = new EmailAddress(toEmail);
        var subject = "Verify your email — Tipping On The Go";

        var html = $@"
            <div style='font-family:Arial,sans-serif;max-width:480px;margin:0 auto;'>
              <div style='background:#1A3ADB;padding:24px;border-radius:12px 12px 0 0;text-align:center;'>
                <h1 style='color:white;margin:0;font-size:24px;'>Tipping On The Go</h1>
              </div>
              <div style='background:#f8f9ff;padding:32px;border-radius:0 0 12px 12px;text-align:center;'>
                <p style='color:#374151;font-size:16px;'>Your email verification code:</p>
                <div style='background:white;border:2px solid #1A3ADB;border-radius:12px;
                            padding:20px;margin:20px 0;display:inline-block;'>
                  <span style='font-size:40px;font-weight:bold;color:#1A3ADB;
                               letter-spacing:12px;'>{code}</span>
                </div>
                <p style='color:#6B7280;font-size:14px;'>Expires in <strong>10 minutes</strong>.</p>
              </div>
            </div>";

        var msg = MailHelper.CreateSingleEmail(from, to, subject,
            $"Your email verification code: {code}. Expires in 10 minutes.", html);

        var response = await client.SendEmailAsync(msg);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            _logger.LogError("SendGrid failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException("Failed to send verification email.");
        }
    }

    private static string NormaliseTarget(string target, OtpType type)
        => type == OtpType.Email
            ? target.Trim().ToLowerInvariant()
            : target.Trim();
}