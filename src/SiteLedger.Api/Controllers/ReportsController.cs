using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteLedger.Api.Authentication;
using SiteLedger.Application.Contracts.Reports;
using SiteLedger.Application.Services;

namespace SiteLedger.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize(Policy = Policies.Read)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ReportsController(IReportService reports) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/reports/budget-vs-actual")]
    [ProducesResponseType<ProjectBudgetReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectBudgetReportResponse>> BudgetVsActual(Guid projectId, CancellationToken ct) =>
        await reports.GetProjectReportAsync(projectId, ct);

    [HttpGet("reports/portfolio-summary")]
    [ProducesResponseType<PortfolioReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortfolioReportResponse>> PortfolioSummary([FromQuery] PortfolioQuery query, CancellationToken ct) =>
        await reports.GetPortfolioReportAsync(query, ct);
}
