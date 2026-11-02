using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SiteLedger.Api.Authentication;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Timesheets;
using SiteLedger.Application.Services;

namespace SiteLedger.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize(Policy = Policies.Read)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class TimesheetsController(ITimesheetService timesheets) : ControllerBase
{
    [HttpPost("projects/{projectId:guid}/timesheets")]
    [Authorize(Policy = Policies.ManageProjects)]
    [ProducesResponseType<TimesheetResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TimesheetResponse>> Submit(Guid projectId, SubmitTimesheetRequest request, CancellationToken ct)
    {
        var timesheet = await timesheets.SubmitAsync(projectId, request, ct);

        // No single-timesheet GET in the 14-endpoint contract, so Location points at the project's list.
        return Created($"/api/v1/projects/{projectId}/timesheets", timesheet);
    }

    [HttpGet("projects/{projectId:guid}/timesheets")]
    [ProducesResponseType<PagedResponse<TimesheetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<TimesheetResponse>>> List(Guid projectId, [FromQuery] SubmissionQuery query, CancellationToken ct) =>
        await timesheets.ListAsync(projectId, query, ct);

    [HttpPost("timesheets/{timesheetId:guid}/approve")]
    [Authorize(Policy = Policies.Decide)]
    [ProducesResponseType<TimesheetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TimesheetResponse>> Approve(
        Guid timesheetId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveRequest? request,
        CancellationToken ct) =>
        await timesheets.ApproveAsync(timesheetId, request?.Note, ct);

    [HttpPost("timesheets/{timesheetId:guid}/reject")]
    [Authorize(Policy = Policies.Decide)]
    [ProducesResponseType<TimesheetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TimesheetResponse>> Reject(Guid timesheetId, RejectRequest request, CancellationToken ct) =>
        await timesheets.RejectAsync(timesheetId, request.Reason, ct);
}
