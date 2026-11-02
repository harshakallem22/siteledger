using System.Text.RegularExpressions;
using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.Domain.Entities;

public sealed partial class Project
{
    public const int ProjectCodeMinLength = 3;
    public const int ProjectCodeMaxLength = 24;
    public const int NameMinLength = 3;
    public const int NameMaxLength = 120;

    // Sanity cap that also keeps portfolio sums far from decimal(18,2) overflow.
    public const decimal MaxBudgetUsd = 1_000_000_000.00m;

    // For EF Core.
    private Project()
    {
    }

    private Project(string projectCode, string name, decimal budgetUsd, ProjectStatus status, DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        ProjectCode = projectCode;
        Name = name;
        BudgetUsd = budgetUsd;
        Status = status;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    // Unique, stored upper-case, immutable after creation.
    public string ProjectCode { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public decimal BudgetUsd { get; private set; }

    public ProjectStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    // The only way to create a project, so validation always runs.
    public static Project Create(string projectCode, string name, decimal budgetUsd, ProjectStatus status, DateTimeOffset nowUtc)
    {
        if (status == ProjectStatus.Completed)
        {
            throw new DomainValidationException("status", "A new project must be Draft or Active.");
        }

        return new Project(NormalizeCode(projectCode), NormalizeName(name), ValidateBudget(budgetUsd), status, nowUtc);
    }

    public void Rename(string name, DateTimeOffset nowUtc)
    {
        EnsureNotCompleted();
        Name = NormalizeName(name);
        UpdatedAtUtc = nowUtc;
    }

    // May go below approved actuals on purpose: reports flag the overrun instead of blocking a correction.
    public void ChangeBudget(decimal budgetUsd, DateTimeOffset nowUtc)
    {
        EnsureNotCompleted();
        BudgetUsd = ValidateBudget(budgetUsd);
        UpdatedAtUtc = nowUtc;
    }

    // Draft → Active → Completed (no pending items); same status is a no-op.
    public void ChangeStatus(ProjectStatus target, bool hasPendingSubmissions, DateTimeOffset nowUtc)
    {
        if (target == Status)
        {
            return;
        }

        EnsureNotCompleted();

        var allowed = (Status, target) switch
        {
            (ProjectStatus.Draft, ProjectStatus.Active) => true,
            (ProjectStatus.Active, ProjectStatus.Completed) => true,
            _ => false,
        };

        if (!allowed)
        {
            throw new DomainConflictException($"A project cannot move from {Status} to {target}.");
        }

        if (target == ProjectStatus.Completed && hasPendingSubmissions)
        {
            throw new DomainConflictException("A project with pending timesheets or expenses cannot be completed.");
        }

        Status = target;
        UpdatedAtUtc = nowUtc;
    }

    private void EnsureNotCompleted()
    {
        if (Status == ProjectStatus.Completed)
        {
            throw new DomainConflictException("A completed project cannot be modified.");
        }
    }

    private static string NormalizeCode(string projectCode)
    {
        var code = (projectCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < ProjectCodeMinLength or > ProjectCodeMaxLength || !ProjectCodePattern().IsMatch(code))
        {
            throw new DomainValidationException(
                "projectCode",
                $"Project code must be {ProjectCodeMinLength}-{ProjectCodeMaxLength} letters, digits or single hyphens, e.g. CON-2026-001.");
        }

        return code;
    }

    private static string NormalizeName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < NameMinLength or > NameMaxLength)
        {
            throw new DomainValidationException("name", $"Name must be {NameMinLength}-{NameMaxLength} characters.");
        }

        return trimmed;
    }

    private static decimal ValidateBudget(decimal budgetUsd)
    {
        if (budgetUsd <= 0 || budgetUsd > MaxBudgetUsd)
        {
            throw new DomainValidationException("budgetUsd", $"Budget must be greater than 0 and at most {MaxBudgetUsd:N2}.");
        }

        if (decimal.Round(budgetUsd, 2) != budgetUsd)
        {
            throw new DomainValidationException("budgetUsd", "Budget can have at most 2 decimal places.");
        }

        return budgetUsd;
    }

    [GeneratedRegex("^[A-Z0-9]+(-[A-Z0-9]+)*$")]
    private static partial Regex ProjectCodePattern();
}
