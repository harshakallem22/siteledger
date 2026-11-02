using SiteLedger.Application.Contracts.Common;
using SiteLedger.Application.Contracts.Projects;

namespace SiteLedger.Application.Services;

public interface IProjectService
{
    Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken ct);

    Task<PagedResponse<ProjectResponse>> ListAsync(ProjectQuery query, CancellationToken ct);

    Task<ProjectResponse> GetAsync(Guid id, CancellationToken ct);

    Task<ProjectResponse> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken ct);
}
