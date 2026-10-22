using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Usterki.Api.Authorization;
using Usterki.Contracts.Properties;
using Usterki.Domain.Entities;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Api.Controllers;

[ApiController]
[Route(
    "api/organizations/{organizationId:guid}/buildings/{buildingId:guid}/units")]
[Authorize(Policy = AuthorizationPolicies.ManageProperties)]
public sealed class UnitsController : ControllerBase
{
    private readonly AppDbContext _db;

    public UnitsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<UnitResponse>>> GetAll(
        Guid organizationId,
        Guid buildingId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        if (!await BuildingExistsAsync(
                organizationId,
                buildingId,
                cancellationToken))
        {
            return NotFound();
        }

        var query = _db.Units
            .AsNoTracking()
            .Where(unit =>
                unit.BuildingId == buildingId &&
                unit.Building.OrganizationId == organizationId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(unit => unit.Number)
            .ThenBy(unit => unit.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(unit => new UnitResponse(
                unit.Id,
                unit.BuildingId,
                unit.Number,
                unit.Floor))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<UnitResponse>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount));
    }

    [HttpGet("{unitId:guid}")]
    public async Task<ActionResult<UnitResponse>> GetById(
        Guid organizationId,
        Guid buildingId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var unit = await _db.Units
            .AsNoTracking()
            .Where(unit =>
                unit.Id == unitId &&
                unit.BuildingId == buildingId &&
                unit.Building.OrganizationId == organizationId)
            .Select(unit => new UnitResponse(
                unit.Id,
                unit.BuildingId,
                unit.Number,
                unit.Floor))
            .SingleOrDefaultAsync(cancellationToken);

        if (unit is null)
        {
            return NotFound();
        }

        return Ok(unit);
    }

    [HttpPost]
    public async Task<ActionResult<UnitResponse>> Create(
        Guid organizationId,
        Guid buildingId,
        CreateUnitRequest request,
        CancellationToken cancellationToken)
    {
        if (!await BuildingExistsAsync(
                organizationId,
                buildingId,
                cancellationToken))
        {
            return NotFound();
        }

        var number = request.Number.Trim();

        var numberExists = await _db.Units.AnyAsync(
            unit =>
                unit.BuildingId == buildingId &&
                unit.Number == number,
            cancellationToken);

        if (numberExists)
        {
            return DuplicateUnitNumber();
        }

        var unit = new Unit
        {
            BuildingId = buildingId,
            Number = number,
            Floor = request.Floor
        };

        _db.Units.Add(unit);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_Units_BuildingId_Number"
            })
        {
            //Ochrona przed równoczesnymi żądaniami
            return DuplicateUnitNumber();
        }

        var response = new UnitResponse(
            unit.Id,
            unit.BuildingId,
            unit.Number,
            unit.Floor);

        return CreatedAtAction(
            nameof(GetById),
            new
            {
                organizationId,
                buildingId,
                unitId = unit.Id
            },
            response);
    }

    private Task<bool> BuildingExistsAsync(
        Guid organizationId,
        Guid buildingId,
        CancellationToken cancellationToken)
    {
        return _db.Buildings.AnyAsync(
            building =>
                building.Id == buildingId &&
                building.OrganizationId == organizationId,
            cancellationToken);
    }

    private ConflictObjectResult DuplicateUnitNumber()
    {
        return Conflict(new
        {
            message = "Lokal o podanym numerze już istnieje w tym budynku."
        });
    }
}