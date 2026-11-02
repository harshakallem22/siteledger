using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiteLedger.Application.Abstractions;
using SiteLedger.Application.Exceptions;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;

namespace SiteLedger.Infrastructure.Persistence;

public sealed class SiteLedgerDbContext(DbContextOptions<SiteLedgerDbContext> options)
    : DbContext(options), ISiteLedgerDbContext
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Timesheet> Timesheets => Set<Timesheet>();

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<ApprovalAudit> ApprovalAudits => Set<ApprovalAudit>();

    // SQL Server unique-key violations (2601/2627) → DuplicateResourceException, so services never see SQL error numbers.
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateResourceException("A record with the same unique key already exists.", ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SiteLedgerDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All money is decimal(18,2); Hours overrides this with decimal(5,2).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Enums are stored as readable strings, not integers.
        configurationBuilder.Properties<ProjectStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<SubmissionStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<SubmissionType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<AuditAction>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ExpenseCategory>().HaveConversion<string>().HaveMaxLength(30);
    }
}
