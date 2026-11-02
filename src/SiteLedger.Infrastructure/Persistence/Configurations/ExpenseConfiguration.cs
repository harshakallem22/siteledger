using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses", table =>
        {
            table.HasCheckConstraint("CK_Expenses_AmountUsd_Positive", "[AmountUsd] > 0");
            table.HasCheckConstraint("CK_Expenses_Category", SqlCheck.InEnum<ExpenseCategory>("Category"));
            table.HasCheckConstraint("CK_Expenses_Status", SqlCheck.InEnum<SubmissionStatus>("Status"));
            table.HasCheckConstraint("CK_Expenses_Decision", SqlCheck.DecisionMatchesStatus);
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Description).HasMaxLength(200);
        builder.Property(e => e.ExternalReference).HasMaxLength(80);
        builder.Property(e => e.SubmittedBySubject).HasMaxLength(100);
        builder.Property(e => e.DecidedBySubject).HasMaxLength(100);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);

        // Reports sum by project and status straight from this index; lists read a project's newest first.
        builder.HasIndex(e => new { e.ProjectId, e.Status }).IncludeProperties(e => e.AmountUsd);
        builder.HasIndex(e => new { e.ProjectId, e.ExpenseDate }).IsDescending(false, true);
    }
}
