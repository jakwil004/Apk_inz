using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public class UnitResident
{
    public Guid UnitId { get; set; }

    public Unit Unit { get; set; } = null!;

    public Guid UserId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}