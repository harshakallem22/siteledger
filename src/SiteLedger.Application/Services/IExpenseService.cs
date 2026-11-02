using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Expenses;

namespace SiteLedger.Application.Services;

public interface IExpenseService
{
    Task<ExpenseResponse> SubmitAsync(Guid projectId, SubmitExpenseRequest request, CancellationToken ct);

    Task<PagedResponse<ExpenseResponse>> ListAsync(Guid projectId, SubmissionQuery query, CancellationToken ct);

    Task<ExpenseResponse> ApproveAsync(Guid id, string? note, CancellationToken ct);

    Task<ExpenseResponse> RejectAsync(Guid id, string? reason, CancellationToken ct);
}
