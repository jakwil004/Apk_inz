using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;

namespace Usterki.Contracts.Tickets;

public sealed class CreateTicketRequest
{
    [Required, StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string LocationDetails { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Category { get; set; } = string.Empty;
}

public sealed record TicketResponse(
    Guid Id,
    Guid OrganizationId,
    Guid BuildingId,
    Guid UnitId,
    string UnitNumber,
    Guid ReporterId,
    string Title,
    string Description,
    string LocationDetails,
    string Category,
    string Status,
    DateTime CreatedAtUtc,
    long Version, 
    Guid? AssignedTechnicianId);

public sealed class ChangeTicketStatusRequest
{
    [Required, StringLength(30)]
    public string Status { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
}

public sealed record TicketStatusHistoryResponse(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    Guid ChangedByUserId,
    string Reason,
    DateTime ChangedAtUtc);

public sealed class CreateTicketCommentRequest
{
    [Required, StringLength(4000)]
    public string Content { get; set; } = string.Empty;
}

public sealed record TicketCommentResponse(
    Guid Id,
    Guid TicketId,
    Guid AuthorId,
    string AuthorName,
    string Content,
    DateTime CreatedAtUtc);

public sealed record TicketCategoryResponse(
    string Code,
    string Name);

public sealed class ChangeTicketAssignmentRequest
{
    //null to odpięcie technika
    public Guid? TechnicianId { get; set; }

    [Required, StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
}

public sealed record TicketAssignmentHistoryResponse(
    Guid Id,
    Guid? PreviousTechnicianId,
    Guid? NewTechnicianId,
    Guid ChangedByUserId,
    string Reason,
    DateTime ChangedAtUtc);