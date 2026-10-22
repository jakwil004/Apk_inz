using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Usterki.Api.Authorization;
using Usterki.Contracts.Properties;
using Usterki.Domain.Entities;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Api.Controllers;

[ApiController]
[Route("api/organizations/{organizationId:guid}/buildings")]
[Authorize(Policy = AuthorizationPolicies.ManageProperties)]
public sealed class BuildingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public BuildingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<BuildingResponse>>> GetAll(
        Guid organizationId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        var query = _db.Buildings
            .AsNoTracking()
            .Where(building =>
                building.OrganizationId == organizationId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(building => building.Name)
            .ThenBy(building => building.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(building => new BuildingResponse(
                building.Id,
                building.OrganizationId,
                building.Name,
                building.City,
                building.PostalCode,
                building.Street,
                building.BuildingNumber))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<BuildingResponse>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount));
    }

    [HttpGet("{buildingId:guid}")]
    public async Task<ActionResult<BuildingResponse>> GetById(
        Guid organizationId,
        Guid buildingId,
        CancellationToken cancellationToken)
    {
        var building = await _db.Buildings
            .AsNoTracking()
            .Where(building =>
                building.OrganizationId == organizationId &&
                building.Id == buildingId)
            .Select(building => new BuildingResponse(
                building.Id,
                building.OrganizationId,
                building.Name,
                building.City,
                building.PostalCode,
                building.Street,
                building.BuildingNumber))
            .SingleOrDefaultAsync(cancellationToken);

        if (building is null)
        {
            return NotFound();
        }

        return Ok(building);
    }

    [HttpPost]
    public async Task<ActionResult<BuildingResponse>> Create(
        Guid organizationId,
        CreateBuildingRequest request,
        CancellationToken cancellationToken)
    {
        var building = new Building
        {
            OrganizationId = organizationId,
            Name = request.Name.Trim(),
            City = request.City.Trim(),
            PostalCode = request.PostalCode.Trim(),
            Street = request.Street.Trim(),
            BuildingNumber = request.BuildingNumber.Trim()
        };

        _db.Buildings.Add(building);
        await _db.SaveChangesAsync(cancellationToken);

        var response = new BuildingResponse(
            building.Id,
            building.OrganizationId,
            building.Name,
            building.City,
            building.PostalCode,
            building.Street,
            building.BuildingNumber);

        return CreatedAtAction(
            nameof(GetById),
            new
            {
                organizationId,
                buildingId = building.Id
            },
            response);
    }
}