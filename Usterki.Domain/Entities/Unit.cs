using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Domain.Entities;

public class Unit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BuildingId { get; set; }

    public Building Building { get; set; } = null!;

    public string Number { get; set; } = string.Empty;

    public int? Floor { get; set; }
}