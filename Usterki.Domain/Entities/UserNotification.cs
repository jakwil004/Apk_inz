using System;
using System.Collections.Generic;
using System.Text;
using Usterki.Domain.Enums;

namespace Usterki.Domain.Entities;

public sealed class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EventId { get; set; }

    public Guid RecipientUserId { get; set; }

    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public NotificationType Type { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAtUtc { get; set; }
}