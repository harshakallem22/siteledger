using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.UnitTests.Domain;

// Expenses get the shared Submission state machine (tested in ApprovalRulesTests) with Expense-typed audits.
public sealed class ExpenseDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 16, 0, 0, TimeSpan.Zero);

    private static Expense PendingExpense()
    {
        var project = Project.Create("CON-2026-001", "Fremont Equipment Bay Upgrade", 250_000m, ProjectStatus.Active, Now);
        return Expense.Submit(project, "Electrical conduit", ExpenseCategory.Materials, 1_250m, new DateOnly(2026, 10, 27), "PO-1873", 1_000_000m, "pm-1", Now);
    }

    [Fact]
    public void Approve_ProducesExpenseTypedAudit()
    {
        var expense = PendingExpense();

        var audit = expense.Approve("approver-1", "Invoice matched purchase order", Now.AddHours(1));

        Assert.Equal(SubmissionStatus.Approved, expense.Status);
        Assert.Equal(SubmissionType.Expense, audit.SubmissionType);
        Assert.Equal(expense.Id, audit.SubmissionId);
        Assert.Equal(AuditAction.Approved, audit.Action);
        Assert.Equal("Invoice matched purchase order", audit.Reason);
    }

    [Fact]
    public void Reject_RequiresReason_AndIsFinal()
    {
        var expense = PendingExpense();

        Assert.Equal("reason", Assert.Throws<DomainValidationException>(() => expense.Reject("approver-1", "no", Now)).Field);
        var audit = expense.Reject("approver-1", "Supporting invoice is missing", Now);

        Assert.Equal(SubmissionType.Expense, audit.SubmissionType);
        Assert.Equal(AuditAction.Rejected, audit.Action);
        Assert.Throws<DomainConflictException>(() => expense.Approve("approver-2", null, Now));
        Assert.Equal(SubmissionStatus.Rejected, expense.Status);
    }
}
