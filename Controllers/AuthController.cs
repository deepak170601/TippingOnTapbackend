using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
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

    public AuthController(IConfiguration config, UserService users)
    {
        _config = config;
        _users = users;
    }

    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] AuthRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { message = "Full name, email and password are required." });

        var user = await _users.CreateUserAsync(request.FullName!, request.Email, request.Password);
        if (user == null)
            return Conflict(new { message = "A user with that email already exists." });

        return Ok(new { message = "Account created successfully." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Email and password are required." });

        var user = await _users.ValidateUserAsync(request.Email, request.Password);
        if (user == null)
            return Unauthorized(new { message = "Invalid email or password." });

        var accessToken = GenerateJwt(user.Email);
        var refreshToken = await _users.CreateRefreshTokenAsync(user.Id);

        return Ok(new
        {
            accessToken,
            refreshToken = refreshToken.Token,
            expiresIn = 7200,
            user = new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
            },
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { message = "refreshToken is required." });

        var (user, newRefreshToken) = await _users.RotateRefreshTokenAsync(request.RefreshToken);
        if (user == null || newRefreshToken == null)
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        return Ok(new
        {
            accessToken = GenerateJwt(user.Email),
            refreshToken = newRefreshToken.Token,
            expiresIn = 7200,
            user = new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
            },
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { message = "refreshToken is required." });

        await _users.RevokeRefreshTokenAsync(request.RefreshToken);
        return Ok(new { message = "Logged out successfully." });
    }

    private string GenerateJwt(string email)
    {
        var jwtKey = _config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is missing.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            claims: new[] { new Claim(ClaimTypes.Email, email) },
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record AuthRequest(string? FullName, string Email, string Password);
public record RefreshRequest(string RefreshToken);