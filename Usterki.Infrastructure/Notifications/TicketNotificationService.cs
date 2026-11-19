using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Usterki.Domain.Entities;
using Usterki.Domain.Enums;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Infrastructure.Notifications;

public sealed class TicketNotificationService
{
    private readonly AppDbContext _db;

    public TicketNotificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task StageAsync(
        Ticket ticket,
        Guid actorUserId,
        NotificationType type,
        CancellationToken cancellationToken)
    {
        var message = type switch
        {
            NotificationType.CommentAdded =>
                "Dodano komentarz do zgłoszenia.",

            NotificationType.StatusChanged =>
                "Zmieniono status zgłoszenia.",

            NotificationType.AssignmentChanged =>
                "Zmieniono przydział serwisanta do zgłoszenia.",

            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        var organizationId = await _db.Units
            .Where(unit => unit.Id == ticket.UnitId)
            .Select(unit => unit.Building.OrganizationId)
            .SingleAsync(cancellationToken);

        var recipientIds = await _db.OrganizationMemberships
            .Where(membership =>
                membership.OrganizationId == organizationId &&
                membership.IsActive &&
                membership.UserId != actorUserId &&
                (
                    membership.Role == OrganizationRole.Owner ||
                    membership.Role == OrganizationRole.Manager ||

                    (
                        membership.Role == OrganizationRole.Technician &&
                        ticket.AssignedTechnicianId == membership.UserId
                    ) ||

                    (
                        membership.Role == OrganizationRole.Resident &&
                        ticket.ReporterId == membership.UserId &&
                        _db.UnitResidents.Any(assignment =>
                            assignment.UnitId == ticket.UnitId &&
                            assignment.UserId == membership.UserId &&
                            assignment.IsActive)
                    )
                ))
            .Where(membership => _db.Users.Any(user =>
                user.Id == membership.UserId &&
                user.IsActive))
            .Select(membership => membership.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        foreach (var recipientId in recipientIds)
        {
            var notification = new UserNotification
            {
                EventId = eventId,
                RecipientUserId = recipientId,
                TicketId = ticket.Id,
                Type = type,
                Message = message,
                CreatedAtUtc = now
            };

            _db.UserNotifications.Add(notification);

            _db.NotificationPushMessages.Add(
                new NotificationPushMessage
                {
                    Notification = notification,
                    CreatedAtUtc = now,
                    NextAttemptAtUtc = now
                });
        }
    }
}