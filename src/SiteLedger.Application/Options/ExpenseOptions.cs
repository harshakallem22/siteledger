using System.ComponentModel.DataAnnotations;

namespace SiteLedger.Application.Options;

// "Expenses" settings, validated at startup.
public sealed class ExpenseOptions
{
    public const string SectionName = "Expenses";

    // Guards against typos like 1250000 for 1250.00.
    [Range(typeof(decimal), "0.01", "1000000000")]
    public decimal MaxAmountUsd { get; init; } = 1_000_000.00m;
}
