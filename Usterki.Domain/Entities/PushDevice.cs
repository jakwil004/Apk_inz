using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public sealed class PushDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid SessionId { get; set; }

    public string FirebaseInstallationId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public long Version { get; set; } = 1;
}