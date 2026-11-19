using System;
using System.Collections.Generic;
using System.Text;
namespace Usterki.Contracts.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    Guid TicketId,
    string Type,
    string Message,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);