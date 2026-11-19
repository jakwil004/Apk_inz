using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public sealed class NotificationPushMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid NotificationId { get; set; }

    public UserNotification Notification { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;

    public int Attempts { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }
    public Guid? LeaseId { get; set; }

    public DateTime? LockedUntilUtc { get; set; }

    public string? CompletionReason { get; set; }
}