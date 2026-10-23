using System;
using System.Collections.Generic;
using System.Text;
using Usterki.Domain.Enums;

namespace Usterki.Domain.Entities;

public class Ticket
{
    public long Version { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UnitId { get; set; }

    public Unit Unit { get; set; } = null!;

    public Guid ReporterId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string LocationDetails { get; set; } = string.Empty;

    public TicketCategory Category { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.New;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
}