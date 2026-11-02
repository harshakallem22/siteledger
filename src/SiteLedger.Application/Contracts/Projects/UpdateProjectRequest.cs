using System.ComponentModel.DataAnnotations;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Application.Contracts.Projects;

// Partial update: null means "not provided"; ProjectCode is immutable.
public sealed record UpdateProjectRequest : IValidatableObject
{
    public string? Name { get; init; }

    public decimal? BudgetUsd { get; init; }

    [EnumDataType(typeof(ProjectStatus))]
    public ProjectStatus? Status { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is null && BudgetUsd is null && Status is null)
        {
            yield return new ValidationResult("Provide at least one of name, budgetUsd or status.");
        }
    }
}
