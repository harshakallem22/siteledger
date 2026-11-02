using System.ComponentModel.DataAnnotations;
using SiteLedger.Application.Contracts.Common;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Application.Contracts.Reports;

public sealed class PortfolioQuery
{
    [EnumDataType(typeof(ProjectStatus))]
    public ProjectStatus? Status { get; init; }

    [Range(1, Paging.MaxPage)]
    public int Page { get; init; } = 1;

    [Range(1, Paging.MaxPageSize)]
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
