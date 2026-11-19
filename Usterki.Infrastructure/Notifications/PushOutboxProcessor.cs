using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Usterki.Domain.Entities;
using Usterki.Infrastructure.Persistence;
using Usterki.Infrastructure.Queries;

namespace Usterki.Infrastructure.Notifications;

public sealed class PushOutboxProcessor
{
    private const int MaxAttempts = 8;

    private readonly AppDbContext _db;
    private readonly IPushSender _sender;

    public PushOutboxProcessor(
        AppDbContext db,
        IPushSender sender)
    {
        _db = db;
        _sender = sender;
    }

    public async Task<bool> ProcessNextAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var id = await _db.NotificationPushMessages
            .AsNoTracking()
            .Where(message =>
                message.ProcessedAtUtc == null &&
                message.NextAttemptAtUtc <= now &&
                (
                    message.LockedUntilUtc == null ||
                    message.LockedUntilUtc <= now
                ))
            .OrderBy(message => message.NextAttemptAtUtc)
            .ThenBy(message => message.Id)
            .Select(message => (Guid?)message.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id.HasValue &&
            await ProcessAsync(id.Value, cancellationToken);
    }

    public async Task<bool> ProcessAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseId = Guid.NewGuid();
        var lockedUntil = now.AddMinutes(2);

        var claimed = await _db.NotificationPushMessages
            .Where(message =>
                message.Id == messageId &&
                message.ProcessedAtUtc == null &&
                message.NextAttemptAtUtc <= now &&
                (
                    message.LockedUntilUtc == null ||
                    message.LockedUntilUtc <= now
                ))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        message => message.LeaseId,
                        (Guid?)leaseId)
                    .SetProperty(
                        message => message.LockedUntilUtc,
                        (DateTime?)lockedUntil)
                    .SetProperty(
                        message => message.Attempts,
                        message => message.Attempts + 1),
                cancellationToken);

        if (claimed == 0)
        {
            return false;
        }

        var queued = await _db.NotificationPushMessages
            .AsNoTracking()
            .Include(message => message.Notification)
            .SingleAsync(
                message => message.Id == messageId,
                cancellationToken);

        var notification = queued.Notification;

        if (!await CanReceiveAsync(notification, cancellationToken))
        {
            await FinishAsync(
                messageId,
                leaseId,
                "AccessRevoked",
                cancellationToken);

            return true;
        }

        var devices = await ActiveDevices(notification.RecipientUserId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var retryNeeded = false;
        var accepted = false;

        foreach (var device in devices)
        {
            // Odnawiamy blokadę przed kolejną operacją sieciową.
            var renewedUntil = DateTime.UtcNow.AddMinutes(2);
            var renewalTime = DateTime.UtcNow;

            var renewed = await _db.NotificationPushMessages
                .Where(message =>
                    message.Id == messageId &&
                    message.LeaseId == leaseId &&
                    message.ProcessedAtUtc == null &&
                    message.LockedUntilUtc > renewalTime)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        message => message.LockedUntilUtc,
                        (DateTime?)renewedUntil),
                    cancellationToken);

            if (renewed == 0)
            {
                return true;
            }

            if (!await CanReceiveAsync(
                    notification,
                    cancellationToken))
            {
                await FinishAsync(
                    messageId,
                    leaseId,
                    "AccessRevoked",
                    cancellationToken);

                return true;
            }

            var stillActive = await ActiveDevices(
                    notification.RecipientUserId)
                .AnyAsync(
                    current =>
                        current.Id == device.Id &&
                        current.Version == device.Version,
                    cancellationToken);

            if (!stillActive)
            {
                continue;
            }

            PushSendResult result;

            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            try
            {
                result = await _sender.SendAsync(
                    device.FirebaseInstallationId,
                    notification.Id,
                    timeout.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                result = PushSendResult.Retry;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                result = PushSendResult.Retry;
            }

            if (result == PushSendResult.Accepted)
            {
                accepted = true;
            }
            else if (result == PushSendResult.Unregistered)
            {
                //Nie dezaktywujemy urzadzenia gdy zostało na nowo zarejestrowane
                
                await _db.PushDevices
                    .Where(current =>
                        current.Id == device.Id &&
                        current.Version == device.Version)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                current => current.IsActive,
                                false)
                            .SetProperty(
                                current => current.Version,
                                current => current.Version + 1),
                        cancellationToken);
            }
            else
            {
                retryNeeded = true;
            }
        }

        if (!retryNeeded)
        {
            await FinishAsync(
                messageId,
                leaseId,
                accepted ? "AcceptedByFcm" : "NoActiveDevices",
                cancellationToken);

            return true;
        }

        if (queued.Attempts >= MaxAttempts)
        {
            await FinishAsync(
                messageId,
                leaseId,
                "Failed",
                cancellationToken);

            return true;
        }

        var delaySeconds = Math.Min(
            3600,
            60 * Math.Pow(2, queued.Attempts - 1));

        var nextAttempt = DateTime.UtcNow.AddSeconds(delaySeconds);

        await _db.NotificationPushMessages
            .Where(message =>
                message.Id == messageId &&
                message.LeaseId == leaseId &&
                message.ProcessedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        message => message.NextAttemptAtUtc,
                        nextAttempt)
                    .SetProperty(
                        message => message.LeaseId,
                        (Guid?)null)
                    .SetProperty(
                        message => message.LockedUntilUtc,
                        (DateTime?)null),
                cancellationToken);

        return true;
    }

    private IQueryable<PushDevice> ActiveDevices(Guid userId)
    {
        var now = DateTime.UtcNow;

        return _db.PushDevices.Where(device =>
            device.UserId == userId &&
            device.IsActive &&
            _db.UserSessions.Any(session =>
                session.Id == device.SessionId &&
                session.UserId == userId &&
                session.RevokedAtUtc == null &&
                session.ExpiresAtUtc > now));
    }

    private async Task<bool> CanReceiveAsync(
        UserNotification notification,
        CancellationToken cancellationToken)
    {
        var activeUser = await _db.Users.AnyAsync(
            user =>
                user.Id == notification.RecipientUserId &&
                user.IsActive,
            cancellationToken);

        if (!activeUser)
        {
            return false;
        }

        return await _db.Tickets
            .VisibleTo(_db, notification.RecipientUserId)
            .AnyAsync(
                ticket => ticket.Id == notification.TicketId,
                cancellationToken);
    }

    private Task<int> FinishAsync(
        Guid messageId,
        Guid leaseId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        return _db.NotificationPushMessages
            .Where(message =>
                message.Id == messageId &&
                message.LeaseId == leaseId &&
                message.ProcessedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        message => message.ProcessedAtUtc,
                        (DateTime?)now)
                    .SetProperty(
                        message => message.CompletionReason,
                        reason)
                    .SetProperty(
                        message => message.LeaseId,
                        (Guid?)null)
                    .SetProperty(
                        message => message.LockedUntilUtc,
                        (DateTime?)null),
                cancellationToken);
    }
}