using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SmartDoc.Domain.Entities;

namespace SmartDoc.Api.Features.Auth;

/// <summary>
/// Signs JWTs with HMAC-SHA256 using Jwt:Secret. ValidateIssuer/ValidateAudience are off on
/// the validation side (Program.cs) — this is a single internal service, not a multi-issuer
/// setup, so there is no separate "issuer" to check against.
/// </summary>
public class JwtTokenGenerator(IConfiguration configuration)
{
    public (string Token, DateTimeOffset ExpiresAt) GenerateToken(User user)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        var expirationMinutes = configuration.GetValue("Jwt:ExpirationMinutes", 60);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), // "who this token is about" — the UserId
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // unique token id, for future revocation support
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(claims: claims, expires: expiresAt.UtcDateTime, signingCredentials: credentials);
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return (tokenString, expiresAt);
    }
}
