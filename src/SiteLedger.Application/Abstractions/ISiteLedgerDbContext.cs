using Microsoft.EntityFrameworkCore;
using SiteLedger.Domain.Entities;

namespace SiteLedger.Application.Abstractions;

// Minimal EF surface for services: a deliberate shortcut instead of a generic repository.
public interface ISiteLedgerDbContext
{
    DbSet<Project> Projects { get; }

    DbSet<Employee> Employees { get; }

    DbSet<Timesheet> Timesheets { get; }

    DbSet<Expense> Expenses { get; }

    DbSet<ApprovalAudit> ApprovalAudits { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
