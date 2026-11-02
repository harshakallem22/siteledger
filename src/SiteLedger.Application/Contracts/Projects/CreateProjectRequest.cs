using System.ComponentModel.DataAnnotations;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Application.Contracts.Projects;

// Nullable so [Required] reports a missing field; business rules live in Project.Create.
public sealed record CreateProjectRequest
{
    [Required]
    public string? ProjectCode { get; init; }

    [Required]
    public string? Name { get; init; }

    [Required]
    public decimal? BudgetUsd { get; init; }

    // Draft or Active; defaults to Active.
    [EnumDataType(typeof(ProjectStatus))]
    public ProjectStatus? Status { get; init; }
}
