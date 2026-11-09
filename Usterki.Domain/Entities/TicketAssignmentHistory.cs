using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public class TicketAssignmentHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public Guid? PreviousTechnicianId { get; set; }

    public Guid? NewTechnicianId { get; set; }

    public Guid ChangedByUserId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}