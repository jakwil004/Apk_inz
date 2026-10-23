using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;

namespace Usterki.Contracts.Residents;

public sealed class CreateResidentInvitationRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;
}

public sealed record ResidentInvitationResponse(
    Guid Id,
    Guid UnitId,
    string Email,
    string Token,
    DateTime ExpiresAtUtc);

public sealed class AcceptResidentInvitationRequest
{
    [Required, StringLength(200, MinimumLength = 40)]
    public string Token { get; set; } = string.Empty;
}

public sealed record AcceptedResidentInvitationResponse(
    Guid OrganizationId,
    Guid UnitId);

public sealed record MyUnitResponse(
    Guid UnitId,
    string Number,
    int? Floor,
    Guid BuildingId,
    string BuildingName,
    Guid OrganizationId,
    string OrganizationName,
    string City,
    string Street,
    string BuildingNumber);