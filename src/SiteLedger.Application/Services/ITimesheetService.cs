using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Timesheets;

namespace SiteLedger.Application.Services;

public interface ITimesheetService
{
    Task<TimesheetResponse> SubmitAsync(Guid projectId, SubmitTimesheetRequest request, CancellationToken ct);

    Task<PagedResponse<TimesheetResponse>> ListAsync(Guid projectId, SubmissionQuery query, CancellationToken ct);

    Task<TimesheetResponse> ApproveAsync(Guid id, string? note, CancellationToken ct);

    Task<TimesheetResponse> RejectAsync(Guid id, string? reason, CancellationToken ct);
}
