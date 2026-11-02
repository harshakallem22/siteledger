using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SiteLedger.Api.Authentication;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Expenses;
using SiteLedger.Application.Services;

namespace SiteLedger.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize(Policy = Policies.Read)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ExpensesController(IExpenseService expenses) : ControllerBase
{
    [HttpPost("projects/{projectId:guid}/expenses")]
    [Authorize(Policy = Policies.ManageProjects)]
    [ProducesResponseType<ExpenseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExpenseResponse>> Submit(Guid projectId, SubmitExpenseRequest request, CancellationToken ct)
    {
        var expense = await expenses.SubmitAsync(projectId, request, ct);

        // No single-expense GET in the 14-endpoint contract, so Location points at the project's list.
        return Created($"/api/v1/projects/{projectId}/expenses", expense);
    }

    [HttpGet("projects/{projectId:guid}/expenses")]
    [ProducesResponseType<PagedResponse<ExpenseResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<ExpenseResponse>>> List(Guid projectId, [FromQuery] SubmissionQuery query, CancellationToken ct) =>
        await expenses.ListAsync(projectId, query, ct);

    [HttpPost("expenses/{expenseId:guid}/approve")]
    [Authorize(Policy = Policies.Decide)]
    [ProducesResponseType<ExpenseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExpenseResponse>> Approve(
        Guid expenseId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveRequest? request,
        CancellationToken ct) =>
        await expenses.ApproveAsync(expenseId, request?.Note, ct);

    [HttpPost("expenses/{expenseId:guid}/reject")]
    [Authorize(Policy = Policies.Decide)]
    [ProducesResponseType<ExpenseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExpenseResponse>> Reject(Guid expenseId, RejectRequest request, CancellationToken ct) =>
        await expenses.RejectAsync(expenseId, request.Reason, ct);
}
