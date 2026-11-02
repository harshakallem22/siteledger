using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Infrastructure.Persistence.Configurations;

internal sealed class TimesheetConfiguration : IEntityTypeConfiguration<Timesheet>
{
    public void Configure(EntityTypeBuilder<Timesheet> builder)
    {
        builder.ToTable("Timesheets", table =>
        {
            table.HasCheckConstraint("CK_Timesheets_Hours_Range", "[Hours] > 0 AND [Hours] <= 24");
            table.HasCheckConstraint("CK_Timesheets_HourlyRateSnapshotUsd_Positive", "[HourlyRateSnapshotUsd] > 0");
            table.HasCheckConstraint("CK_Timesheets_LaborCostUsd_NonNegative", "[LaborCostUsd] >= 0");
            table.HasCheckConstraint("CK_Timesheets_Status", SqlCheck.InEnum<SubmissionStatus>("Status"));
            table.HasCheckConstraint("CK_Timesheets_Decision", SqlCheck.DecisionMatchesStatus);
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Hours).HasPrecision(5, 2);
        builder.Property(t => t.SubmittedBySubject).HasMaxLength(100);
        builder.Property(t => t.DecidedBySubject).HasMaxLength(100);
        builder.Property(t => t.RowVersion).IsRowVersion();

        // Financial history is never cascade-deleted.
        builder.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(t => t.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        // One timesheet per employee, project and day, enforced by the database (race-proof).
        builder.HasIndex(t => new { t.ProjectId, t.EmployeeId, t.WorkDate }).IsUnique();
    }
}
