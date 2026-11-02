using SiteLedger.Domain.Enums;

namespace SiteLedger.Application.Contracts.Expenses;

public sealed record ExpenseResponse(
    Guid Id,
    Guid ProjectId,
    string Description,
    ExpenseCategory Category,
    decimal AmountUsd,
    DateOnly ExpenseDate,
    string? ExternalReference,
    SubmissionStatus Status,
    string SubmittedBySubject,
    DateTimeOffset SubmittedAtUtc,
    string? DecidedBySubject,
    DateTimeOffset? DecidedAtUtc);
