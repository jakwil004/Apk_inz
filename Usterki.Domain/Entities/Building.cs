using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public class Building
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public Organization Organization { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string Street { get; set; } = string.Empty;

    public string BuildingNumber { get; set; } = string.Empty;

    public ICollection<Unit> Units { get; set; }
        = new List<Unit>();
}