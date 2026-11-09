using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;

namespace Usterki.Contracts.Tickets;

public sealed class TicketListQuery : IValidatableObject
{
    [Range(1, 1_000_000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    [RegularExpression(
        "^(New|InProgress|Resolved|Closed|Rejected)$",
        ErrorMessage = "Nieprawidłowy status zgłoszenia.")]
    public string? Status { get; set; }

    [RegularExpression(
        "^(Plumbing|Electrical|Heating|Building|Other)$",
        ErrorMessage = "Nieprawidłowa kategoria zgłoszenia.")]
    public string? Category { get; set; }

    public Guid? BuildingId { get; set; }

    public Guid? UnitId { get; set; }

    public Guid? AssignedTechnicianId { get; set; }

    public bool UnassignedOnly { get; set; }

    [StringLength(150)]
    public string? Search { get; set; }

    public DateTimeOffset? CreatedFromUtc { get; set; }

    public DateTimeOffset? CreatedToUtc { get; set; }

    [Required]
    [RegularExpression(
        "^(created_desc|created_asc|title_asc|title_desc)$",
        ErrorMessage = "Nieprawidłowy sposób sortowania.")]
    public string Sort { get; set; } = "created_desc";

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (BuildingId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Identyfikator budynku nie może być pusty.",
                new[] { nameof(BuildingId) });
        }

        if (UnitId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Identyfikator mieszkania nie może być pusty.",
                new[] { nameof(UnitId) });
        }

        if (AssignedTechnicianId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Identyfikator serwisanta nie może być pusty.",
                new[] { nameof(AssignedTechnicianId) });
        }

        if (UnassignedOnly && AssignedTechnicianId.HasValue)
        {
            yield return new ValidationResult(
                "Nie można jednocześnie filtrować po serwisancie " +
                "i wybierać zgłoszeń bez przydziału.",
                new[]
                {
                    nameof(UnassignedOnly),
                    nameof(AssignedTechnicianId)
                });
        }

        if (CreatedFromUtc.HasValue &&
            CreatedToUtc.HasValue &&
            CreatedFromUtc.Value >= CreatedToUtc.Value)
        {
            yield return new ValidationResult(
                "Początek zakresu dat musi poprzedzać jego koniec.",
                new[]
                {
                    nameof(CreatedFromUtc),
                    nameof(CreatedToUtc)
                });
        }
    }
}