using Microsoft.EntityFrameworkCore;
using Usterki.Contracts.Tickets;
using Usterki.Domain.Entities;
using Usterki.Domain.Enums;

namespace Usterki.Api.Queries;

public static class TicketQueryExtensions
{
    public static IQueryable<Ticket> ApplyFilters(
        this IQueryable<Ticket> query,
        TicketListQuery filters)
    {
        if (!string.IsNullOrEmpty(filters.Status))
        {
            var status = Enum.Parse<TicketStatus>(filters.Status);

            query = query.Where(ticket => ticket.Status == status);
        }

        if (!string.IsNullOrEmpty(filters.Category))
        {
            var category = Enum.Parse<TicketCategory>(filters.Category);

            query = query.Where(ticket => ticket.Category == category);
        }

        if (filters.BuildingId is Guid buildingId)
        {
            query = query.Where(
                ticket => ticket.Unit.BuildingId == buildingId);
        }

        if (filters.UnitId is Guid unitId)
        {
            query = query.Where(ticket => ticket.UnitId == unitId);
        }

        if (filters.AssignedTechnicianId is Guid technicianId)
        {
            query = query.Where(
                ticket => ticket.AssignedTechnicianId == technicianId);
        }

        if (filters.UnassignedOnly)
        {
            query = query.Where(
                ticket => ticket.AssignedTechnicianId == null);
        }

        if (filters.CreatedFromUtc is DateTimeOffset from)
        {
            var fromUtc = from.UtcDateTime;

            query = query.Where(
                ticket => ticket.CreatedAtUtc >= fromUtc);
        }

        if (filters.CreatedToUtc is DateTimeOffset to)
        {
            var toUtc = to.UtcDateTime;

            query = query.Where(
                ticket => ticket.CreatedAtUtc < toUtc);
        }

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = EscapeLikePattern(filters.Search.Trim());
            var pattern = $"%{search}%";

            query = query.Where(ticket =>
                EF.Functions.ILike(ticket.Title, pattern, "\\") ||
                EF.Functions.ILike(ticket.Description, pattern, "\\"));
        }

        return query;
    }

    public static IOrderedQueryable<Ticket> ApplySorting(
        this IQueryable<Ticket> query,
        string sort)
    {
        return sort switch
        {
            "created_desc" => query
                .OrderByDescending(ticket => ticket.CreatedAtUtc)
                .ThenBy(ticket => ticket.Id),

            "created_asc" => query
                .OrderBy(ticket => ticket.CreatedAtUtc)
                .ThenBy(ticket => ticket.Id),

            "title_asc" => query
                .OrderBy(ticket => ticket.Title)
                .ThenBy(ticket => ticket.Id),

            "title_desc" => query
                .OrderByDescending(ticket => ticket.Title)
                .ThenBy(ticket => ticket.Id),

            _ => throw new ArgumentOutOfRangeException(
                nameof(sort),
                sort,
                "Nieobsługiwany sposób sortowania.")
        };
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }
}