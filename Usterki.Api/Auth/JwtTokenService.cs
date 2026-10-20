using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Usterki.Infrastructure.Identity;

namespace Usterki.Api.Auth;

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(JwtOptions options)
    {
        _options = options;

        var key = new SymmetricSecurityKey(
            Convert.FromBase64String(options.SigningKey));

        _credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);
    }

    public (string AccessToken, DateTime ExpiresAtUtc) CreateToken(
        ApplicationUser user,
        Guid sessionId,
        DateTime sessionExpiresAtUtc)
    {
        var now = DateTime.UtcNow;

        var expiresAt = now.AddMinutes(
            _options.AccessTokenMinutes);

        if (expiresAt > sessionExpiresAtUtc)
        {
            expiresAt = sessionExpiresAtUtc;
        }

        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()),

            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString()),

            new Claim("sid", sessionId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: _credentials);

        return (
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}