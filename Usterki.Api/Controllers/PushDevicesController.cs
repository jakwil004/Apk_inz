using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Usterki.Api.Errors;
using Usterki.Contracts.Notifications;
using Usterki.Domain.Entities;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Api.Controllers;

[ApiController]
[Route("api/push-devices")]
[Authorize]
public sealed class PushDevicesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PushDevicesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPut]
    public async Task<IActionResult> Register(
        RegisterPushDeviceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId))
        {
            return Unauthorized();
        }

        var fid = request.FirebaseInstallationId.Trim();

        if (fid.Length < 10 || fid.Any(char.IsWhiteSpace))
        {
            return BadRequest(ApiErrors.Create(
                HttpContext,
                400,
                "invalid_push_device",
                "Nieprawidłowy identyfikator instalacji."));
        }

        var device = await _db.PushDevices.SingleOrDefaultAsync(
            item => item.FirebaseInstallationId == fid,
            cancellationToken);

        var now = DateTime.UtcNow;

        if (device is not null && device.UserId != userId)
        {
            var previousSessionIsActive =
                device.IsActive &&
                await _db.UserSessions.AnyAsync(
                    session =>
                        session.Id == device.SessionId &&
                        session.RevokedAtUtc == null &&
                        session.ExpiresAtUtc > now,
                    cancellationToken);

            if (previousSessionIsActive)
            {
                return Conflict(ApiErrors.Create(
                    HttpContext,
                    409,
                    "push_device_in_use",
                    "Instalacja jest powiązana z inną aktywną sesją."));
            }
        }

        if (device is null)
        {
            device = new PushDevice
            {
                FirebaseInstallationId = fid
            };

            _db.PushDevices.Add(device);
        }
        else
        {
            device.Version++;
        }

        device.UserId = userId;
        device.SessionId = sessionId;
        device.IsActive = true;
        device.UpdatedAtUtc = now;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RegistrationConflict();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            return RegistrationConflict();
        }

        return Ok(new PushDeviceResponse(device.Id));
    }

    [HttpDelete("{deviceId:guid}")]
    public async Task<IActionResult> Unregister(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId))
        {
            return Unauthorized();
        }

        var changed = await _db.PushDevices
            .Where(device =>
                device.Id == deviceId &&
                device.UserId == userId &&
                device.SessionId == sessionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(device => device.IsActive, false)
                    .SetProperty(
                        device => device.Version,
                        device => device.Version + 1),
                cancellationToken);

        return changed > 0 ? NoContent() : NotFound();
    }

    private bool TryGetIdentity(
        out Guid userId,
        out Guid sessionId)
    {
        var validUser = Guid.TryParse(
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            out userId);

        var validSession = Guid.TryParse(
            User.FindFirst("sid")?.Value,
            out sessionId);

        return validUser && validSession;
    }

    private ConflictObjectResult RegistrationConflict()
    {
        return Conflict(ApiErrors.Create(
            HttpContext,
            409,
            "push_device_conflict",
            "Rejestracja urządzenia została równocześnie zmieniona. Ponów żądanie."));
    }
}