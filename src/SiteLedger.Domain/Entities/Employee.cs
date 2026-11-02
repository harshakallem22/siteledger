namespace SiteLedger.Domain.Entities;

// Reference data created by the seeder; there is no employee API.
public sealed class Employee
{
    private Employee()
    {
    }

    public Employee(Guid id, string displayName, string email, decimal hourlyRateUsd, bool isActive, DateTimeOffset createdAtUtc)
    {
        Id = id;
        DisplayName = displayName;
        Email = email;
        HourlyRateUsd = hourlyRateUsd;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    // Timesheets copy this at submission, so later rate changes never rewrite history.
    public decimal HourlyRateUsd { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
