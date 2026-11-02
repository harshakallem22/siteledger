using System.ComponentModel.DataAnnotations;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Application.Contracts.Common;

public sealed class SubmissionQuery
{
    [EnumDataType(typeof(SubmissionStatus))]
    public SubmissionStatus? Status { get; init; }

    [Range(1, Paging.MaxPage)]
    public int Page { get; init; } = 1;

    [Range(1, Paging.MaxPageSize)]
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
