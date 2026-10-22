using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;

namespace Usterki.Contracts.Properties;

public sealed class CreateBuildingRequest
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string City { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string PostalCode { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Street { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string BuildingNumber { get; set; } = string.Empty;
}

public sealed record BuildingResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string City,
    string PostalCode,
    string Street,
    string BuildingNumber);

public sealed class CreateUnitRequest
{
    [Required, StringLength(20)]
    public string Number { get; set; } = string.Empty;

    [Range(-10, 300)]
    public int? Floor { get; set; }
}

public sealed record UnitResponse(
    Guid Id,
    Guid BuildingId,
    string Number,
    int? Floor);

public sealed class PaginationQuery
{
    [Range(1, 1_000_000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
