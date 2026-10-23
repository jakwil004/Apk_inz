using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Usterki.Api.Authorization;
using Usterki.Contracts.Residents;
using Usterki.Domain.Entities;
using Usterki.Domain.Enums;
using Usterki.Infrastructure.Identity;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class ResidentInvitationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ResidentInvitationsController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpPost(
        "organizations/{organizationId:guid}/buildings/{buildingId:guid}" +
        "/units/{unitId:guid}/invitations")]
    [Authorize(Policy = AuthorizationPolicies.ManageProperties)]
    public async Task<ActionResult<ResidentInvitationResponse>> Create(
        Guid organizationId,
        Guid buildingId,
        Guid unitId,
        CreateResidentInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var unitExists = await _db.Units.AnyAsync(
            unit =>
                unit.Id == unitId &&
                unit.BuildingId == buildingId &&
                unit.Building.OrganizationId == organizationId,
            cancellationToken);

        if (!unitExists)
        {
            return NotFound();
        }

        var subject = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var email = request.Email.Trim();

        var token = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(64));

        var invitation = new ResidentInvitation
        {
            UnitId = unitId,
            CreatedByUserId = userId,
            Email = email,
            NormalizedEmail = _userManager.NormalizeEmail(email)!,
            TokenHash = HashToken(token),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };

        _db.ResidentInvitations.Add(invitation);
        await _db.SaveChangesAsync(cancellationToken);

        Response.Headers["Cache-Control"] = "no-store";

        return StatusCode(
            StatusCodes.Status201Created,
            new ResidentInvitationResponse(
                invitation.Id,
                invitation.UnitId,
                invitation.Email,
                token,
                invitation.ExpiresAtUtc));
    }

    [HttpDelete(
        "organizations/{organizationId:guid}/invitations/{invitationId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageProperties)]
    public async Task<IActionResult> Revoke(
        Guid organizationId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var updated = await _db.ResidentInvitations
            .Where(invitation =>
                invitation.Id == invitationId &&
                invitation.Unit.Building.OrganizationId == organizationId &&
                invitation.AcceptedAtUtc == null &&
                invitation.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    invitation => invitation.RevokedAtUtc,
                    (DateTime?)now),
                cancellationToken);

        if (updated == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
    
    [HttpPost("invitations/accept")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AcceptedResidentInvitationResponse>> Accept(
        AcceptResidentInvitationRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store";

        var subject = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        var tokenHash = HashToken(request.Token);
        var now = DateTime.UtcNow;

        var invitation = await _db.ResidentInvitations
            .AsNoTracking()
            .Where(invitation =>
                invitation.TokenHash == tokenHash &&
                invitation.AcceptedAtUtc == null &&
                invitation.RevokedAtUtc == null &&
                invitation.ExpiresAtUtc > now)
            .Select(invitation => new
            {
                invitation.Id,
                invitation.UnitId,
                invitation.CreatedByUserId,
                invitation.NormalizedEmail,
                OrganizationId =
                    invitation.Unit.Building.OrganizationId
            })
            .SingleOrDefaultAsync(cancellationToken);

        
        if (invitation is null ||
            invitation.NormalizedEmail != user.NormalizedEmail)
        {
            return InvalidInvitation();
        }

        //sprawdzamy czy twórca zaproszenia jest aktywny i ma uprawnienia do zarządzania
        var issuerCanManage = await _db.OrganizationMemberships.AnyAsync(
            membership =>
                membership.UserId == invitation.CreatedByUserId &&
                membership.OrganizationId == invitation.OrganizationId &&
                membership.IsActive &&
                (membership.Role == OrganizationRole.Owner ||
                 membership.Role == OrganizationRole.Manager),
            cancellationToken);

        var issuerIsActive = await _db.Users.AnyAsync(
            issuer =>
                issuer.Id == invitation.CreatedByUserId &&
                issuer.IsActive,
            cancellationToken);

        if (!issuerCanManage || !issuerIsActive)
        {
            return InvalidInvitation();
        }
        //transakcja obejmuje aktualizację zaproszenia, dodanie członkostwa
        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        
        var acceptedAt = DateTime.UtcNow;

        var claimed = await _db.ResidentInvitations
            .Where(item =>
                item.Id == invitation.Id &&
                item.AcceptedAtUtc == null &&
                item.RevokedAtUtc == null &&
                item.ExpiresAtUtc > acceptedAt)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    item => item.AcceptedAtUtc,
                    (DateTime?)acceptedAt),
                cancellationToken);

        if (claimed != 1)
        {
            return InvalidInvitation();
        }

        var membership = await _db.OrganizationMemberships
            .SingleOrDefaultAsync(
                item =>
                    item.OrganizationId == invitation.OrganizationId &&
                    item.UserId == userId,
                cancellationToken);

        
        if (membership is not null &&
            (!membership.IsActive ||
             membership.Role != OrganizationRole.Resident))
        {
            return Conflict(new
            {
                message = "Istniejące członkostwo nie pozwala " +
                          "przyjąć zaproszenia mieszkańca."
            });
        }

        if (membership is null)
        {
            _db.OrganizationMemberships.Add(
                new OrganizationMembership
                {
                    OrganizationId = invitation.OrganizationId,
                    UserId = userId,
                    Role = OrganizationRole.Resident,
                    IsActive = true
                });
        }

        var assignment = await _db.UnitResidents
            .SingleOrDefaultAsync(
                item =>
                    item.UnitId == invitation.UnitId &&
                    item.UserId == userId,
                cancellationToken);

        if (assignment is not null && !assignment.IsActive)
        {
            return Conflict(new
            {
                message = "Przypisanie do lokalu jest nieaktywne. " +
                          "Skontaktuj się z zarządcą."
            });
        }

        if (assignment is null)
        {
            _db.UnitResidents.Add(new UnitResident
            {
                UnitId = invitation.UnitId,
                UserId = userId,
                IsActive = true
            });
        }
        
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {

           
            return Conflict(new
            {
                message = "Dane członkostwa zmieniły się. " +
                          "Ponów próbę zaakceptowania zaproszenia."
            });
        }

        return Ok(new AcceptedResidentInvitationResponse(
            invitation.OrganizationId,
            invitation.UnitId));
    }

    private BadRequestObjectResult InvalidInvitation()
    {
        return BadRequest(new
        {
            message = "Zaproszenie jest nieprawidłowe, niedostępne " +
                      "lub przeznaczone dla innego konta."
        });
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}