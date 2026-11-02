using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Infrastructure.Persistence.Configurations;

internal sealed class ApprovalAuditConfiguration : IEntityTypeConfiguration<ApprovalAudit>
{
    public void Configure(EntityTypeBuilder<ApprovalAudit> builder)
    {
        builder.ToTable("ApprovalAudits", table =>
        {
            table.HasCheckConstraint("CK_ApprovalAudits_SubmissionType", SqlCheck.InEnum<SubmissionType>("SubmissionType"));
            table.HasCheckConstraint("CK_ApprovalAudits_Action", SqlCheck.InEnum<AuditAction>("Action"));
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.ActorSubject).HasMaxLength(100);
        builder.Property(a => a.Reason).HasMaxLength(500);

        // One decision per submission: the database backstop against double approval.
        builder.HasIndex(a => new { a.SubmissionType, a.SubmissionId }).IsUnique();
    }
}
