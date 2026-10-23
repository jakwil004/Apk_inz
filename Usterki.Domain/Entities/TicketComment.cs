using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public class TicketComment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}