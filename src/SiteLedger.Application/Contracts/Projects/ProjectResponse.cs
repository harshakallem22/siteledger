using SiteLedger.Domain.Enums;

namespace SiteLedger.Application.Contracts.Projects;

public sealed record ProjectResponse(
    Guid Id,
    string ProjectCode,
    string Name,
    decimal BudgetUsd,
    ProjectStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
