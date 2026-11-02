using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiteLedger.Application.Abstractions;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Expenses;
using SiteLedger.Application.Exceptions;
using SiteLedger.Application.Options;
using SiteLedger.Domain.Entities;

namespace SiteLedger.Application.Services;

public sealed class ExpenseService(
    ISiteLedgerDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<ExpenseOptions> options,
    SubmissionDecider decider,
    ILogger<ExpenseService> logger) : IExpenseService
{
    private static readonly Expression<Func<Expense, ExpenseResponse>> ToResponseExpression = e =>
        new ExpenseResponse(
            e.Id,
            e.ProjectId,
            e.Description,
            e.Category,
            e.AmountUsd,
            e.ExpenseDate,
            e.ExternalReference,
            e.Status,
            e.SubmittedBySubject,
            e.SubmittedAtUtc,
            e.DecidedBySubject,
            e.DecidedAtUtc);

    private static readonly Func<Expense, ExpenseResponse> ToResponse = ToResponseExpression.Compile();

    public async Task<ExpenseResponse> SubmitAsync(Guid projectId, SubmitExpenseRequest request, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ResourceNotFoundException("Project", projectId);

        var expense = Expense.Submit(
            project,
            request.Description ?? string.Empty,
            request.Category ?? default,
            request.AmountUsd ?? 0m,
            request.ExpenseDate ?? DateOnly.MinValue,
            request.ExternalReference,
            options.Value.MaxAmountUsd,
            currentUser.Subject,
            clock.GetUtcNow());

        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Expense {ExpenseId} submitted for project {ProjectId}: {Category} {AmountUsd}",
            expense.Id,
            projectId,
            expense.Category,
            expense.AmountUsd);

        return ToResponse(expense);
    }

    public async Task<PagedResponse<ExpenseResponse>> ListAsync(Guid projectId, SubmissionQuery query, CancellationToken ct)
    {
        Paging.Validate(query.Page, query.PageSize);

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new ResourceNotFoundException("Project", projectId);
        }

        var expenses = db.Expenses.AsNoTracking().Where(e => e.ProjectId == projectId);
        if (query.Status is { } status)
        {
            expenses = expenses.Where(e => e.Status == status);
        }

        var totalCount = await expenses.CountAsync(ct);
        var items = await expenses
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.SubmittedAtUtc)
            .ThenBy(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToResponseExpression)
            .ToListAsync(ct);

        return new PagedResponse<ExpenseResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public Task<ExpenseResponse> ApproveAsync(Guid id, string? note, CancellationToken ct) =>
        DecideAsync(id, (expense, actor, now) => expense.Approve(actor, note, now), ct);

    public Task<ExpenseResponse> RejectAsync(Guid id, string? reason, CancellationToken ct) =>
        DecideAsync(id, (expense, actor, now) => expense.Reject(actor, reason, now), ct);

    private async Task<ExpenseResponse> DecideAsync(
        Guid id,
        Func<Expense, string, DateTimeOffset, ApprovalAudit> decide,
        CancellationToken ct)
    {
        var expense = await decider.DecideAsync(db.Expenses, "Expense", id, decide, ct);

        logger.LogInformation("Expense decision completed for {ExpenseId} with {Decision}", expense.Id, expense.Status);
        return ToResponse(expense);
    }
}
