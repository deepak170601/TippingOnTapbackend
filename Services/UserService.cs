using Microsoft.EntityFrameworkCore;
using StripeTerminalBackend.Data;
using StripeTerminalBackend.Models;
using System.Security.Cryptography;

namespace StripeTerminalBackend.Services;

public class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<User?> CreateUserAsync(string fullName, string email, string plainPassword)
    {
        bool exists = await _db.Users.AnyAsync(u => u.Email == email);
        if (exists) return null;

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword),
            CreatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<User?> ValidateUserAsync(string email, string plainPassword)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null) return null;
        return BCrypt.Net.BCrypt.Verify(plainPassword, user.PasswordHash) ? user : null;
    }

    public async Task<RefreshToken> CreateRefreshTokenAsync(string userId)
    {
        var existing = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var old in existing)
            old.RevokedAt = DateTime.UtcNow;

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

    public async Task<(User? user, RefreshToken? newToken)> RotateRefreshTokenAsync(
        string tokenString)
    {
        var token = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == tokenString);

        if (token == null || !token.IsValid)
            return (null, null);

        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var newToken = await CreateRefreshTokenAsync(token.UserId);
        return (token.User, newToken);
    }

    public async Task<bool> RevokeRefreshTokenAsync(string tokenString)
    {
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == tokenString);

        if (token == null || token.IsRevoked) return false;

        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}