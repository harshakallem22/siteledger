using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiteLedger.Domain.Entities;

namespace SiteLedger.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees", table =>
            table.HasCheckConstraint("CK_Employees_HourlyRateUsd_Positive", "[HourlyRateUsd] > 0"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.DisplayName).HasMaxLength(100);
        builder.Property(e => e.Email).HasMaxLength(100);
        builder.HasIndex(e => e.Email).IsUnique();
    }
}
