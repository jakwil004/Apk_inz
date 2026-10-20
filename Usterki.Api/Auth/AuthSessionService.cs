using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Usterki.Contracts.Auth;
using Usterki.Domain.Entities;
using Usterki.Infrastructure.Identity;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Api.Auth;

public sealed class AuthSessionService
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwtTokenService;
    private readonly JwtOptions _options;

    public AuthSessionService(
        AppDbContext db,
        JwtTokenService jwtTokenService,
        JwtOptions options)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _options = options;
    }

    public async Task<AuthResponse> StartSessionAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var session = new UserSession
        {
            UserId = user.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_options.RefreshTokenDays)
        };

        var rawToken = GenerateRefreshToken();

        _db.UserSessions.Add(session);

        _db.RefreshTokens.Add(new RefreshToken
        {
            SessionId = session.Id,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = session.ExpiresAtUtc
        });

        var response = CreateResponse(user, session, rawToken);

        await _db.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task<AuthResponse?> RefreshAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(rawToken);

        //Szukanie identyfikatoa sesji
        var sessionId = await _db.RefreshTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => (Guid?)token.SessionId)
            .SingleOrDefaultAsync(cancellationToken);

        if (sessionId is null)
        {
            return null;
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        //Blokowanie sesji
        var sessions = await _db.UserSessions
            .FromSqlInterpolated($"""
                SELECT *
                FROM "UserSessions"
                WHERE "Id" = {sessionId.Value}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        var session = sessions.SingleOrDefault();

        if (session is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;

        if (session.RevokedAtUtc is not null ||
            session.ExpiresAtUtc <= now)
        {
            return null;
        }

        var token = await _db.RefreshTokens
            .SingleOrDefaultAsync(
                token => token.TokenHash == tokenHash &&
                         token.SessionId == session.Id,
                cancellationToken);

        if (token is null)
        {
            return null;
        }

        //Użycie tego samego tokenu unieważnia sesję
        if (token.UsedAtUtc is not null)
        {
            session.RevokedAtUtc = now;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        if (token.ExpiresAtUtc <= now)
        {
            return null;
        }

        var user = await _db.Users
            .SingleOrDefaultAsync(
                user => user.Id == session.UserId,
                cancellationToken);

        if (user is null || !user.IsActive || user.TwoFactorEnabled)
        {
            session.RevokedAtUtc = now;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        token.UsedAtUtc = now;

        var newRawToken = GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            SessionId = session.Id,
            TokenHash = HashToken(newRawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = session.ExpiresAtUtc
        });

        var response = CreateResponse(
            user,
            session,
            newRawToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return response;
    }

    public async Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await _db.UserSessions
            .Where(session =>
                session.Id == sessionId &&
                session.UserId == userId &&
                session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    session => session.RevokedAtUtc,
                    (DateTime?)now),
                cancellationToken);
    }

    private AuthResponse CreateResponse(
        ApplicationUser user,
        UserSession session,
        string rawRefreshToken)
    {
        var accessToken = _jwtTokenService.CreateToken(
            user,
            session.Id,
            session.ExpiresAtUtc);

        return new AuthResponse(
            accessToken.AccessToken,
            accessToken.ExpiresAtUtc,
            rawRefreshToken,
            session.ExpiresAtUtc);
    }

    private static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(64));
    }

    private static string HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }
}