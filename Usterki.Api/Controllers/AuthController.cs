using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Usterki.Api.Auth;
using Usterki.Contracts.Auth;
using Usterki.Domain.Entities;
using Usterki.Domain.Enums;
using Usterki.Infrastructure.Identity;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AuthSessionService _sessionService;

    public AuthController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AuthSessionService sessionService)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _sessionService = sessionService;
    }

    [HttpPost("register-owner")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<RegisterOwnerResponse>> RegisterOwner(
        RegisterOwnerRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            IsActive = true,
            LockoutEnabled = true
        };

        try
        {
            var result = await _userManager.CreateAsync(
                user,
                request.Password);

            if (!result.Succeeded)
            {
                if (result.Errors.Any(error =>
                    error.Code is "DuplicateUserName" or "DuplicateEmail"))
                {
                    return Conflict(new
                    {
                        message = "Nie można utworzyć konta z podanymi danymi."
                    });
                }

                return BadRequest(new
                {
                    errors = result.Errors.Select(error => new
                    {
                        error.Code,
                        error.Description
                    })
                });
            }

            var organization = new Organization
            {
                Name = request.OrganizationName.Trim()
            };

            _db.Organizations.Add(organization);

            _db.OrganizationMemberships.Add(
                new OrganizationMembership
                {
                    OrganizationId = organization.Id,
                    UserId = user.Id,
                    Role = OrganizationRole.Owner,
                    IsActive = true
                });

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                new RegisterOwnerResponse(user.Id, organization.Id));
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            //Obsługa  równoczesnych rejestracji tego samego konta.
            return Conflict(new
            {
                message = "Nie można utworzyć konta z podanymi danymi."
            });
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Login(
    LoginRequest request,
    CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store";

        var user = await _userManager.FindByEmailAsync(
            request.Email.Trim());

        if (user is null || !user.IsActive || user.TwoFactorEnabled)
        {
            return InvalidLogin();
        }

        var result = await _signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return InvalidLogin();
        }

        var response = await _sessionService.StartSessionAsync(
            user,
            cancellationToken);

        return Ok(response);
    }
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Refresh(
    RefreshRequest request,
    CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store";

        var response = await _sessionService.RefreshAsync(
            request.RefreshToken,
            cancellationToken);

        if (response is null)
        {
            return Unauthorized(new
            {
                message = "Sesja wygasła lub jest nieprawidłowa. Zaloguj się ponownie."
            });
        }

        return Ok(response);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store";

        var subject = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        var sessionClaim = User.FindFirst("sid")?.Value;

        if (!Guid.TryParse(subject, out var userId) ||
            !Guid.TryParse(sessionClaim, out var sessionId))
        {
            return Unauthorized();
        }

        await _sessionService.RevokeSessionAsync(
            userId,
            sessionId,
            cancellationToken);

        return NoContent();
    }


    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>> Me(
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var user = await _db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.Id == userId && user.IsActive,
                cancellationToken);

        if (user is null)
        {
            return Unauthorized();
        }

        var memberships = await _db.OrganizationMemberships
            .AsNoTracking()
            .Where(membership =>
                membership.UserId == userId &&
                membership.IsActive)
            .Select(membership => new
            {
                membership.OrganizationId,
                OrganizationName = membership.Organization.Name,
                membership.Role
            })
            .ToListAsync(cancellationToken);

        var organizations = memberships
            .Select(membership => new OrganizationMembershipResponse(
                membership.OrganizationId,
                membership.OrganizationName,
                membership.Role.ToString()))
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new CurrentUserResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            organizations));
    }

    private UnauthorizedObjectResult InvalidLogin()
    {
        return Unauthorized(new
        {
            message = "Nieprawidłowe dane logowania lub konto jest niedostępne."
        });
    }
}