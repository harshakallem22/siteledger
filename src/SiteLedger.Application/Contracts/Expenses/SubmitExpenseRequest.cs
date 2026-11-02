using System.ComponentModel.DataAnnotations;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Application.Contracts.Expenses;

// Client-owned fields only; status, submitter and timestamps are set by the server.
public sealed record SubmitExpenseRequest
{
    [Required]
    public string? Description { get; init; }

    [Required]
    [EnumDataType(typeof(ExpenseCategory))]
    public ExpenseCategory? Category { get; init; }

    [Required]
    public decimal? AmountUsd { get; init; }

    [Required]
    public DateOnly? ExpenseDate { get; init; }

    public string? ExternalReference { get; init; }
}
