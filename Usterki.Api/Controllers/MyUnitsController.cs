using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Usterki.Contracts.Properties;
using Usterki.Contracts.Residents;
using Usterki.Domain.Enums;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Api.Controllers;

[ApiController]
[Route("api/me/units")]
[Authorize]
public sealed class MyUnitsController : ControllerBase
{
    private readonly AppDbContext _db;

    public MyUnitsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<MyUnitResponse>>> GetAll(
        [FromQuery] PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var query = _db.UnitResidents
            .AsNoTracking()
            .Where(assignment =>
                assignment.UserId == userId &&
                assignment.IsActive &&
                _db.OrganizationMemberships.Any(membership =>
                    membership.UserId == userId &&
                    membership.OrganizationId ==
                        assignment.Unit.Building.OrganizationId &&
                    membership.IsActive &&
                    membership.Role == OrganizationRole.Resident));

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(assignment => assignment.Unit.Building.Name)
            .ThenBy(assignment => assignment.Unit.Number)
            .ThenBy(assignment => assignment.UnitId)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(assignment => new MyUnitResponse(
                assignment.UnitId,
                assignment.Unit.Number,
                assignment.Unit.Floor,
                assignment.Unit.BuildingId,
                assignment.Unit.Building.Name,
                assignment.Unit.Building.OrganizationId,
                assignment.Unit.Building.Organization.Name,
                assignment.Unit.Building.City,
                assignment.Unit.Building.Street,
                assignment.Unit.Building.BuildingNumber))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<MyUnitResponse>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount));
    }
}