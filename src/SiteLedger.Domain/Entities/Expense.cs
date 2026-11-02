using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.Domain.Entities;

public sealed class Expense : Submission
{
    public const int DescriptionMinLength = 3;
    public const int DescriptionMaxLength = 200;
    public const int ExternalReferenceMaxLength = 80;

    private Expense()
    {
    }

    private Expense(
        Guid projectId,
        string description,
        ExpenseCategory category,
        decimal amountUsd,
        DateOnly expenseDate,
        string? externalReference,
        string submittedBySubject,
        DateTimeOffset submittedAtUtc)
        : base(projectId, submittedBySubject, submittedAtUtc)
    {
        Description = description;
        Category = category;
        AmountUsd = amountUsd;
        ExpenseDate = expenseDate;
        ExternalReference = externalReference;
    }

    public string Description { get; private set; } = string.Empty;

    public ExpenseCategory Category { get; private set; }

    public decimal AmountUsd { get; private set; }

    public DateOnly ExpenseDate { get; private set; }

    // e.g. PO-1873; deliberately not unique (one PO can have several invoices).
    public string? ExternalReference { get; private set; }

    protected override SubmissionType AuditType => SubmissionType.Expense;

    // maxAmountUsd comes from configuration, keeping the domain free of config code; dates use UTC.
    public static Expense Submit(
        Project project,
        string description,
        ExpenseCategory category,
        decimal amountUsd,
        DateOnly expenseDate,
        string? externalReference,
        decimal maxAmountUsd,
        string submittedBySubject,
        DateTimeOffset nowUtc)
    {
        if (project.Status != ProjectStatus.Active)
        {
            throw new DomainConflictException($"Expenses can only be submitted to Active projects; this project is {project.Status}.");
        }

        var trimmedDescription = (description ?? string.Empty).Trim();
        if (trimmedDescription.Length is < DescriptionMinLength or > DescriptionMaxLength)
        {
            throw new DomainValidationException("description", $"Description must be {DescriptionMinLength}-{DescriptionMaxLength} characters.");
        }

        if (!Enum.IsDefined(category))
        {
            throw new DomainValidationException("category", "Category must be one of Materials, Equipment, Travel, Other.");
        }

        if (amountUsd <= 0 || amountUsd > maxAmountUsd || decimal.Round(amountUsd, 2) != amountUsd)
        {
            throw new DomainValidationException("amountUsd", $"Amount must be greater than 0 and at most {maxAmountUsd:N2}, with at most 2 decimal places.");
        }

        if (expenseDate > DateOnly.FromDateTime(nowUtc.UtcDateTime))
        {
            throw new DomainValidationException("expenseDate", "Expense date cannot be in the future (UTC).");
        }

        var trimmedReference = externalReference?.Trim();
        if (trimmedReference?.Length > ExternalReferenceMaxLength)
        {
            throw new DomainValidationException("externalReference", $"External reference can be at most {ExternalReferenceMaxLength} characters.");
        }

        return new Expense(
            project.Id,
            trimmedDescription,
            category,
            amountUsd,
            expenseDate,
            string.IsNullOrEmpty(trimmedReference) ? null : trimmedReference,
            submittedBySubject,
            nowUtc);
    }
}
