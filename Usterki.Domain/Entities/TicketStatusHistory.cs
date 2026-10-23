using System;
using System.Collections.Generic;
using System.Text;
using Usterki.Domain.Enums;

namespace Usterki.Domain.Entities;

public class TicketStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public TicketStatus? FromStatus { get; set; }

    public TicketStatus ToStatus { get; set; }

    public Guid ChangedByUserId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}