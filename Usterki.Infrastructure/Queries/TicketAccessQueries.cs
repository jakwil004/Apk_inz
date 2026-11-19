using System;
using System.Collections.Generic;
using System.Text;
using Usterki.Domain.Entities;
using Usterki.Domain.Enums;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Infrastructure.Queries;

public static class TicketAccessQueries
{
    public static IQueryable<Ticket> VisibleTo(
        this IQueryable<Ticket> query,
        AppDbContext db,
        Guid userId)
    {
        return query.Where(ticket =>
            db.OrganizationMemberships.Any(membership =>
                membership.UserId == userId &&
                membership.OrganizationId ==
                    ticket.Unit.Building.OrganizationId &&
                membership.IsActive &&
                (
                    membership.Role == OrganizationRole.Owner ||
                    membership.Role == OrganizationRole.Manager ||

                    (
                        membership.Role == OrganizationRole.Technician &&
                        ticket.AssignedTechnicianId == userId
                    ) ||

                    (
                        membership.Role == OrganizationRole.Resident &&
                        ticket.ReporterId == userId &&
                        db.UnitResidents.Any(assignment =>
                            assignment.UnitId == ticket.UnitId &&
                            assignment.UserId == userId &&
                            assignment.IsActive)
                    )
                )));
    }
}