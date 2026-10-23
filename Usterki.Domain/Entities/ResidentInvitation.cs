using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public class ResidentInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UnitId { get; set; }

    public Unit Unit { get; set; } = null!;

    public Guid CreatedByUserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? AcceptedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}