using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteLedger.Api.Authentication;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Projects;
using SiteLedger.Application.Services;

namespace SiteLedger.Api.Controllers;

[ApiController]
[Route("api/v1/projects")]
[Produces("application/json")]
[Authorize(Policy = Policies.Read)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ProjectsController(IProjectService projects) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Policies.ManageProjects)]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectResponse>> Create(CreateProjectRequest request, CancellationToken ct)
    {
        var project = await projects.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { projectId = project.Id }, project);
    }

    [HttpGet]
    [ProducesResponseType<PagedResponse<ProjectResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<ProjectResponse>>> List([FromQuery] ProjectQuery query, CancellationToken ct) =>
        await projects.ListAsync(query, ct);

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> Get(Guid projectId, CancellationToken ct) =>
        await projects.GetAsync(projectId, ct);

    [HttpPatch("{projectId:guid}")]
    [Authorize(Policy = Policies.ManageProjects)]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectResponse>> Update(Guid projectId, UpdateProjectRequest request, CancellationToken ct) =>
        await projects.UpdateAsync(projectId, request, ct);
}
