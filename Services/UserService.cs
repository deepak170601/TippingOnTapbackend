using Microsoft.EntityFrameworkCore;
using StripeTerminalBackend.Data;
using StripeTerminalBackend.Models;
using System.Security.Cryptography;

namespace StripeTerminalBackend.Services;

public record RegisterUserRequest(
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

public class UserService
{
    private readonly AppDbContext _db;
    private readonly StripeConnectService _stripeConnect;
    private readonly ILogger<UserService> _logger;

    public UserService(
        AppDbContext db,
        StripeConnectService stripeConnect,
        ILogger<UserService> logger)
    {
        _db = db;
        _stripeConnect = stripeConnect;
        _logger = logger;
    }

    public async Task<User?> CreateUserAsync(RegisterUserRequest req)
    {
        bool phoneExists = await _db.Users.AnyAsync(u => u.PhoneNumber == req.PhoneNumber.Trim());
        if (phoneExists) return null;

        string email = req.Email.Trim().ToLowerInvariant();
        bool emailExists = await _db.Users.AnyAsync(u => u.Email == email);
        if (emailExists) return null;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            PhoneNumber = req.PhoneNumber.Trim(),
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            Email = email,
            IsEmailVerified = true,
            Address1 = req.Address1.Trim(),
            Address2 = req.Address2?.Trim(),
            City = req.City.Trim(),
            State = req.State.Trim().ToUpperInvariant(),
            Zip = req.Zip.Trim(),
            CompanyName = req.CompanyName?.Trim(),
            Ein = req.Ein?.Trim(),
            CreatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        try
        {
            await _stripeConnect.CreateConnectedAccountAsync(user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to auto-create Stripe connected account for user {UserId}. Will be created on first /connect/onboard call.",
                user.Id);
        }

        return user;
    }

    public async Task<User?> FindByPhoneAsync(string phoneNumber)
        => await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber.Trim());

    public async Task<RefreshToken> CreateRefreshTokenAsync(string userId)
    {
        var existing = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var old in existing)
        {
            old.RevokedAt = DateTime.UtcNow;
        }

        var token = new RefreshToken
        {
            Id = Guid.NewGuid().ToString(),
            Token = GenerateSecureToken(),
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow,
        };

        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync();
        return token;
    }

    public async Task<(User? user, RefreshToken? newToken)> RotateRefreshTokenAsync(string tokenString)
    {
        var token = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == tokenString);

        if (token == null || !token.IsValid)
        {
            return (null, null);
        }

        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var newToken = await CreateRefreshTokenAsync(token.UserId);
        return (token.User, newToken);
    }

    public async Task<bool> RevokeRefreshTokenAsync(string tokenString)
    {
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == tokenString);

        if (token == null || token.IsRevoked)
        {
            return false;
        }

        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    private static string GenerateSecureToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}