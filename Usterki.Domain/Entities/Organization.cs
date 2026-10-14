using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Building> Buildings { get; set; }
        = new List<Building>();
}
