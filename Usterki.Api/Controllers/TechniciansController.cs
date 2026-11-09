using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Usterki.Api.Authorization;
using Usterki.Api.Errors;
using Usterki.Contracts.Properties;
using Usterki.Contracts.Technicians;
using Usterki.Domain.Entities;
using Usterki.Domain.Enums;
using Usterki.Infrastructure.Identity;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Api.Controllers;

[ApiController]
[Route("api/organizations/{organizationId:guid}/technicians")]
[Authorize(Policy = AuthorizationPolicies.ManageTickets)]
public sealed class TechniciansController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public TechniciansController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<TechnicianResponse>>> GetAll(
        Guid organizationId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        var query =
            from membership in _db.OrganizationMemberships.AsNoTracking()
            join user in _db.Users.AsNoTracking()
                on membership.UserId equals user.Id
            where membership.OrganizationId == organizationId
                  && membership.Role == OrganizationRole.Technician
                  && membership.IsActive
                  && user.IsActive
            select new
            {
                User = user
            };

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(item => item.User.LastName)
            .ThenBy(item => item.User.FirstName)
            .ThenBy(item => item.User.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(item => new TechnicianResponse(
                item.User.Id,
                item.User.FirstName,
                item.User.LastName,
                item.User.Email!))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<TechnicianResponse>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount));
    }

    [HttpPost]
    public async Task<ActionResult<TechnicianResponse>> Add(
        Guid organizationId,
        AddTechnicianRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(
            request.Email.Trim());

        if (user is null || !user.IsActive)
        {
            return NotFound(ApiErrors.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                code: "technician_account_unavailable",
                detail: "Nie znaleziono aktywnego konta. " +
                        "Serwisant powinien najpierw się zarejestrować."));
        }

        var membershipExists = await _db.OrganizationMemberships.AnyAsync(
            membership =>
                membership.OrganizationId == organizationId &&
                membership.UserId == user.Id,
            cancellationToken);

        if (membershipExists)
        {
            return MembershipConflict();
        }

        _db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = organizationId,
            UserId = user.Id,
            Role = OrganizationRole.Technician,
            IsActive = true
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            return MembershipConflict();
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new TechnicianResponse(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email!));
    }

    private ConflictObjectResult MembershipConflict()
    {
        return Conflict(ApiErrors.Create(
            HttpContext,
            StatusCodes.Status409Conflict,
            code: "organization_membership_exists",
            detail: "Konto ma już członkostwo w tej organizacji. " +
                    "Ta operacja nie zmienia roli ani nie wznawia członkostwa."));
    }
}