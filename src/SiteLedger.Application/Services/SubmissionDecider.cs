using Microsoft.EntityFrameworkCore;
using SiteLedger.Application.Abstractions;
using SiteLedger.Application.Exceptions;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.Application.Services;

// The one approve/reject workflow for timesheets and expenses.
public sealed class SubmissionDecider(ISiteLedgerDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    // One SaveChanges = one transaction; rowversion and the unique audit index make a concurrent loser fail cleanly.
    public async Task<TSubmission> DecideAsync<TSubmission>(
        DbSet<TSubmission> submissions,
        string resourceName,
        Guid id,
        Func<TSubmission, string, DateTimeOffset, ApprovalAudit> decide,
        CancellationToken ct)
        where TSubmission : Submission
    {
        var submission = await submissions.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new ResourceNotFoundException(resourceName, id);

        var projectStatus = await db.Projects
            .Where(p => p.Id == submission.ProjectId)
            .Select(p => p.Status)
            .SingleAsync(ct);
        if (projectStatus != ProjectStatus.Active)
        {
            throw new DomainConflictException($"Submissions on a {projectStatus} project cannot be approved or rejected.");
        }

        var audit = decide(submission, currentUser.Subject, clock.GetUtcNow());
        db.ApprovalAudits.Add(audit);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DuplicateResourceException ex)
        {
            // The unique (SubmissionType, SubmissionId) audit index caught a concurrent decision.
            throw new DomainConflictException($"This {resourceName.ToLowerInvariant()} was already decided by another request.", ex);
        }

        return submission;
    }
}
