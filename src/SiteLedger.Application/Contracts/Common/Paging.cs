using SiteLedger.Domain.Exceptions;

namespace SiteLedger.Application.Contracts.Common;

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    // Keeps (page - 1) * pageSize from overflowing an int.
    public const int MaxPage = 100_000;

    // Re-check for callers that bypass HTTP model validation.
    public static void Validate(int page, int pageSize)
    {
        if (page is < 1 or > MaxPage)
        {
            throw new DomainValidationException("page", $"Page must be between 1 and {MaxPage}.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new DomainValidationException("pageSize", $"Page size must be between 1 and {MaxPageSize}.");
        }
    }
}
