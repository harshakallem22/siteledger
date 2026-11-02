using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects", table =>
        {
            table.HasCheckConstraint("CK_Projects_BudgetUsd_Positive", "[BudgetUsd] > 0");
            table.HasCheckConstraint("CK_Projects_Status", SqlCheck.InEnum<ProjectStatus>("Status"));
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.ProjectCode).HasMaxLength(24);
        builder.HasIndex(p => p.ProjectCode).IsUnique();

        // Status-filtered lists and the portfolio report, already in ProjectCode order.
        builder.HasIndex(p => new { p.Status, p.ProjectCode });

        builder.Property(p => p.Name).HasMaxLength(120);
        builder.Property(p => p.RowVersion).IsRowVersion();
    }
}
