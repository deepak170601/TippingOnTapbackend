using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using StripeTerminalBackend.Models;
using StripeTerminalBackend.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace StripeTerminalBackend.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly UserService _users;
    private readonly OtpService _otp;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IConfiguration config, UserService users, OtpService otp,
        ILogger<AuthController> logger)
    {
        _config = config;
        _users = users;
        _otp = otp;
        _logger = logger;
    }

    // ── POST /auth/send-phone-otp ─────────────────────────────
    // Step 1 for both login and registration
    [HttpPost("send-phone-otp")]
    public async Task<IActionResult> SendPhoneOtp([FromBody] PhoneRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PhoneNumber))
            return BadRequest(new { message = "Phone number is required." });

        await _otp.SendPhoneOtpAsync(req.PhoneNumber.Trim());
        return Ok(new { message = "Verification code sent." });
    }

    // ── POST /auth/verify-phone-otp ───────────────────────────
    // Step 2 — verifies OTP, returns tokens if user exists (login)
    // or returns { newUser: true } if number not registered (registration)
    [HttpPost("verify-phone-otp")]
    public async Task<IActionResult> VerifyPhoneOtp([FromBody] VerifyPhoneRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PhoneNumber) || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "Phone number and code are required." });

        var verified = await _otp.VerifyOtpAsync(req.PhoneNumber, req.Code, OtpType.Phone);
        if (!verified)
            return BadRequest(new { message = "Invalid or expired verification code." });

        // Check if user exists → login
        var user = await _users.FindByPhoneAsync(req.PhoneNumber);
        if (user != null)
        {
            // Consume OTP on login
            await _otp.ConsumeOtpAsync(req.PhoneNumber, OtpType.Phone);

            var accessToken = GenerateJwt(user.Id, user.Email);
            var refreshToken = await _users.CreateRefreshTokenAsync(user.Id);

            _logger.LogInformation("User {Phone} logged in via OTP.", req.PhoneNumber);

            return Ok(new
            {
                newUser = false,
                accessToken,
                refreshToken = refreshToken.Token,
                expiresIn = 7200,
                user = MapUser(user),
            });
        }

        // New number — go to registration
        return Ok(new { newUser = true, phoneNumber = req.PhoneNumber });
    }

    // ── POST /auth/send-email-otp ─────────────────────────────
    // Called from the Register screen email verify button
    [HttpPost("send-email-otp")]
    public async Task<IActionResult> SendEmailOtp([FromBody] EmailRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { message = "A valid email address is required." });

        await _otp.SendEmailOtpAsync(req.Email.Trim());
        return Ok(new { message = "Email verification code sent." });
    }

    // ── POST /auth/verify-email-otp ───────────────────────────
    // Called from the OTP dialog on Register screen
    [HttpPost("verify-email-otp")]
    public async Task<IActionResult> VerifyEmailOtp([FromBody] VerifyEmailRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "Email and code are required." });

        var verified = await _otp.VerifyOtpAsync(req.Email, req.Code, OtpType.Email);
        if (!verified)
            return BadRequest(new { message = "Invalid or expired verification code." });

        return Ok(new { message = "Email verified." });
    }

    // ── POST /auth/register ───────────────────────────────────
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        // All required fields
        if (string.IsNullOrWhiteSpace(req.PhoneNumber) ||
            string.IsNullOrWhiteSpace(req.FirstName) ||
            string.IsNullOrWhiteSpace(req.LastName) ||
            string.IsNullOrWhiteSpace(req.Email) ||
            string.IsNullOrWhiteSpace(req.Address1) ||
            string.IsNullOrWhiteSpace(req.City) ||
            string.IsNullOrWhiteSpace(req.State) ||
            string.IsNullOrWhiteSpace(req.Zip))
            return BadRequest(new { message = "All required fields must be filled." });

        // EIN ↔ Company cross-validation
        if (!string.IsNullOrWhiteSpace(req.CompanyName) && string.IsNullOrWhiteSpace(req.Ein))
            return BadRequest(new { message = "EIN is required when company name is provided." });
        if (!string.IsNullOrWhiteSpace(req.Ein) && string.IsNullOrWhiteSpace(req.CompanyName))
            return BadRequest(new { message = "Company name is required when EIN is provided." });

        // Phone must be OTP verified
        if (!await _otp.IsVerifiedAsync(req.PhoneNumber, OtpType.Phone))
            return BadRequest(new { message = "Phone number has not been verified." });

        // Email must be OTP verified
        if (!await _otp.IsVerifiedAsync(req.Email, OtpType.Email))
            return BadRequest(new { message = "Email has not been verified." });

        var user = await _users.CreateUserAsync(new RegisterUserRequest(
            PhoneNumber: req.PhoneNumber.Trim(),
            FirstName: req.FirstName.Trim(),
            LastName: req.LastName.Trim(),
            Email: req.Email.Trim(),
            Address1: req.Address1.Trim(),
            City: req.City.Trim(),
            State: req.State.Trim().ToUpper(),
            Zip: req.Zip.Trim(),
            Address2: req.Address2?.Trim(),
            CompanyName: req.CompanyName?.Trim(),
            Ein: req.Ein?.Trim()
        ));

        if (user == null)
            return Conflict(new { message = "An account with this phone number or email already exists." });

        // Consume both OTPs
        await _otp.ConsumeOtpAsync(req.PhoneNumber, OtpType.Phone);
        await _otp.ConsumeOtpAsync(req.Email, OtpType.Email);

        var accessToken = GenerateJwt(user.Id, user.Email);
        var refreshToken = await _users.CreateRefreshTokenAsync(user.Id);

        _logger.LogInformation("New user registered: {Phone}", req.PhoneNumber);

        return Ok(new
        {
            accessToken,
            refreshToken = refreshToken.Token,
            expiresIn = 7200,
            user = MapUser(user),
        });
    }

    // ── POST /auth/refresh ────────────────────────────────────
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return BadRequest(new { message = "refreshToken is required." });

        var (user, newRefreshToken) = await _users.RotateRefreshTokenAsync(req.RefreshToken);
        if (user == null || newRefreshToken == null)
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        return Ok(new
        {
            accessToken = GenerateJwt(user.Id, user.Email),
            refreshToken = newRefreshToken.Token,
            expiresIn = 7200,
            user = MapUser(user),
        });
    }

    // ── POST /auth/logout ─────────────────────────────────────
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.RefreshToken))
            await _users.RevokeRefreshTokenAsync(req.RefreshToken);
        return Ok(new { message = "Logged out." });
    }

    // ── Helpers ───────────────────────────────────────────────
    private string GenerateJwt(string userId, string email)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing.")));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Email,          email),
        };
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"], claims: claims,
            expires: DateTime.UtcNow.AddHours(2), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static object MapUser(User u) => new
    {
        id = u.Id,
        firstName = u.FirstName,
        lastName = u.LastName,
        fullName = u.FullName,
        email = u.Email,
        phoneNumber = u.PhoneNumber,
        companyName = u.CompanyName,
        address1 = u.Address1,
        address2 = u.Address2,
        city = u.City,
        state = u.State,
        zip = u.Zip,
        onboardingComplete = u.OnboardingComplete,   
    };
}

// ── DTOs ──────────────────────────────────────────────────────
public record PhoneRequest(string PhoneNumber);
public record VerifyPhoneRequest(string PhoneNumber, string Code);
public record EmailRequest(string Email);
public record VerifyEmailRequest(string Email, string Code);
public record RefreshRequest(string RefreshToken);
public record RegisterRequest(
    string PhoneNumber,
    string FirstName,
    string LastName,
    string Email,
    string Address1,
    string City,
    string State,
    string Zip,
    string? Address2 = null,
    string? CompanyName = null,
    string? Ein = null
);