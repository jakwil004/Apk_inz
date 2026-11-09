using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;

namespace Usterki.Contracts.Technicians;

public sealed class AddTechnicianRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;
}

public sealed record TechnicianResponse(
    Guid UserId,
    string FirstName,
    string LastName,
    string Email);