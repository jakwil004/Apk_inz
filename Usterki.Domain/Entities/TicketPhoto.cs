using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public sealed class TicketPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public Guid UploadedByUserId { get; set; }

    public Guid StorageId { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}