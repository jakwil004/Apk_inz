using System;
using System.Collections.Generic;
using System.Text;

using Usterki.Domain.Enums;

namespace Usterki.Domain.Entities;

public class OrganizationMembership
{
    public Guid OrganizationId { get; set; }

    public Organization Organization { get; set; } = null!;

    public Guid UserId { get; set; }

    public OrganizationRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}