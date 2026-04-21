using Microsoft.EntityFrameworkCore;
using StripeTerminalBackend.Data;
using StripeTerminalBackend.Models;

namespace StripeTerminalBackend.Services;

public class OtpService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<OtpService> _logger;
    private readonly IOtpSender _otpSender;

    public OtpService(
        AppDbContext db,
        IConfiguration config,
        ILogger<OtpService> logger,
        IOtpSender otpSender)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _otpSender = otpSender;
    }

    public async Task SendPhoneOtpAsync(string phoneNumber)
    {
        var code = await GenerateAndSaveOtpAsync(phoneNumber, OtpType.Phone);

        // Mock SMS OTP: no Twilio call
        await SendSmsAsync(phoneNumber, code);

        _logger.LogInformation("Phone OTP prepared for {Phone}.", phoneNumber);
    }

    public async Task SendEmailOtpAsync(string email)
    {
        var code = await GenerateAndSaveOtpAsync(email, OtpType.Email);

        await _otpSender.SendOtpAsync(email, code);

        _logger.LogInformation("Email OTP prepared for {Email}.", email);
    }

    public async Task<bool> VerifyOtpAsync(string target, string code, OtpType type)
    {
        var otp = await _db.OtpCodes
            .Where(o => o.Target == NormaliseTarget(target, type)
                     && o.Code == code
                     && o.Type == type
                     && o.UsedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp == null || otp.IsExpired)
        {
            return false;
        }

        otp.VerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsVerifiedAsync(string target, OtpType type)
        => await _db.OtpCodes
            .AnyAsync(o => o.Target == NormaliseTarget(target, type)
                        && o.Type == type
                        && o.VerifiedAt != null
                        && o.UsedAt == null
                        && o.ExpiresAt > DateTime.UtcNow);

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

    private async Task<string> GenerateAndSaveOtpAsync(string target, OtpType type)
    {
        var normalised = NormaliseTarget(target, type);

        var existing = await _db.OtpCodes
            .Where(o => o.Target == normalised && o.Type == type && o.UsedAt == null)
            .ToListAsync();

        _db.OtpCodes.RemoveRange(existing);

        var code = _config.GetValue<bool>("Otp:FixedCode")
            ? (_config["Otp:FixedCodeValue"] ?? "123456")
            : Random.Shared.Next(100000, 999999).ToString();

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

    private Task SendSmsAsync(string phoneNumber, string code)
    {
        _logger.LogInformation("[MOCK OTP SMS] Phone: {Phone}, Code: {Code}", phoneNumber, code);
        return Task.CompletedTask;
    }

    private static string NormaliseTarget(string target, OtpType type)
        => type == OtpType.Email
            ? target.Trim().ToLowerInvariant()
            : target.Trim();
}