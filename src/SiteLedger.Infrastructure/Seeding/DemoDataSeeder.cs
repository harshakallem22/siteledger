using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiteLedger.Domain.Entities;
using SiteLedger.Domain.Enums;
using SiteLedger.Infrastructure.Persistence;

namespace SiteLedger.Infrastructure.Seeding;

// Deterministic 10,000-line dataset built through the real domain methods; dev-only `seed` command, safe to re-run.
public static class DemoDataSeeder
{
    public const int Seed = 42;
    public const int EmployeeCount = 200;
    public const int ProjectCount = 100;
    public const int TimesheetCount = 6_000;
    public const int ExpenseCount = 4_000;
    public const string ProjectCodePrefix = "SL-2026-";

    // Projects 1–90 Active, 91–95 Completed, 96–100 Draft.
    public const int FirstCompletedProject = 91;
    public const int FirstDraftProject = 96;

    // Budgets deliberately set below approved actuals.
    public static readonly IReadOnlyList<int> OverBudgetProjects = [2, 5, 9];

    private const int HeavyProjectCount = 10;   // projects 1–10 get 4× the submissions
    private const decimal MaxExpenseUsd = 1_000_000m;

    private static readonly DateTimeOffset SeedTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ProjectsCreatedAt = new(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BudgetsReviewedAt = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FirstWorkDay = new(2026, 1, 5);
    private static readonly DateOnly LastWorkDay = new(2026, 9, 30);

    private static readonly string[] FirstNames =
        ["Alex", "Priya", "Jordan", "Mei", "Carlos", "Fatima", "Liam", "Aisha", "Noah", "Elena", "Ravi", "Sofia", "Omar", "Hana", "Diego", "Grace"];

    private static readonly string[] LastNames =
        ["Nguyen", "Patel", "Garcia", "Kim", "Okafor", "Silva", "Chen", "Muller", "Haddad", "Rossi", "Tanaka", "Johnson", "Kowalski", "Reddy", "Lopez", "Ali"];

    private static readonly string[] Sites =
        ["North Campus", "Building 7", "East Warehouse", "Central Plant", "West Yard", "Distribution Center", "Test Track", "South Annex"];

    private static readonly string[] Scopes =
        ["Equipment Bay Upgrade", "Battery Line Expansion", "Paint Shop Retrofit", "HVAC Replacement", "Office Fit-out", "Substation Upgrade", "Roof Solar Install", "Loading Dock Rebuild"];

    private static readonly Dictionary<ExpenseCategory, (int MinUsd, int MaxUsd, string[] Descriptions)> ExpenseProfiles = new()
    {
        [ExpenseCategory.Materials] = (50, 25_000, ["Electrical conduit and fittings", "Structural steel", "Concrete delivery", "Drywall and framing", "Copper cabling"]),
        [ExpenseCategory.Equipment] = (200, 60_000, ["Scissor lift rental", "Industrial chiller", "Welding equipment", "Forklift rental", "Air handling unit"]),
        [ExpenseCategory.Travel] = (20, 3_000, ["Site visit airfare", "Contractor hotel stay", "Mileage reimbursement", "Rental car"]),
        [ExpenseCategory.Other] = (10, 5_000, ["Permit fees", "Safety training", "Waste disposal", "Inspection fees"]),
    };

    private static readonly string[] RejectionReasons =
        ["Hours do not match the work order", "Supporting invoice is missing", "Charged to the wrong project", "Duplicate of an earlier submission", "Exceeds the approved scope"];

    public sealed record SeedData(
        IReadOnlyList<Project> Projects,
        IReadOnlyList<Timesheet> Timesheets,
        IReadOnlyList<Expense> Expenses,
        IReadOnlyList<ApprovalAudit> Audits);

    public static async Task SeedAsync(SiteLedgerDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var employees = BuildEmployees();
        var existingIds = await db.Employees.Select(e => e.Id).ToHashSetAsync(ct);
        var missingEmployees = employees.Where(e => !existingIds.Contains(e.Id)).ToList();
        db.Employees.AddRange(missingEmployees);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Inserted} employees inserted, {Skipped} already present", missingEmployees.Count, employees.Count - missingEmployees.Count);

        // One all-or-nothing transaction, so a retry is safe: each attempt first checks whether the data already landed.
        for (var attempt = 1; ; attempt++)
        {
            if (await db.Projects.AnyAsync(p => p.ProjectCode.StartsWith(ProjectCodePrefix), ct))
            {
                logger.LogInformation("Seed: financial data present, projects {Prefix}* exist (attempt {Attempt})", ProjectCodePrefix, attempt);
                return;
            }

            var data = BuildFinancialData(employees, (entity, id) => db.Entry(entity).Property("Id").CurrentValue = id);
            db.Projects.AddRange(data.Projects);
            db.Timesheets.AddRange(data.Timesheets);
            db.Expenses.AddRange(data.Expenses);
            db.ApprovalAudits.AddRange(data.Audits);

            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Seed: inserted {Projects} projects, {Timesheets} timesheets, {Expenses} expenses, {Audits} approval audits (attempt {Attempt})",
                    data.Projects.Count,
                    data.Timesheets.Count,
                    data.Expenses.Count,
                    data.Audits.Count,
                    attempt);
                return;
            }
            catch (Exception ex) when (attempt < MaxSeedAttempts && IsConnectionFailure(ex))
            {
                logger.LogWarning(ex, "Seed: attempt {Attempt} lost its database connection; re-checking and retrying", attempt);
                db.ChangeTracker.Clear();
            }
        }
    }

    private const int MaxSeedAttempts = 3;

    // Network-level failures (e.g. connection reset, SQL error 35), not data errors.
    private static bool IsConnectionFailure(Exception ex) =>
        ex is DbUpdateException { InnerException: SqlException { InnerException: IOException } }
        or SqlException { InnerException: IOException }
        or DbUpdateException { InnerException: SqlException { Number: 35 or -2 } };

    // Raw SQL bypasses EF's retry strategy, so it opts in explicitly (a paused Azure DB answers 40613 while waking).
    public static Task ResetAsync(SiteLedgerDbContext db, CancellationToken ct = default) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(
            token => db.Database.ExecuteSqlRawAsync(
                "DELETE FROM ApprovalAudits; DELETE FROM Timesheets; DELETE FROM Expenses; DELETE FROM Projects; DELETE FROM Employees;", token),
            ct);

    public static IReadOnlyList<Employee> BuildEmployees()
    {
        var random = new Random(Seed);
        var employees = new List<Employee>(EmployeeCount);

        for (var i = 1; i <= EmployeeCount; i++)
        {
            var name = $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}";
            var hourlyRateUsd = random.Next(2_500, 9_501) / 100m; // $25.00 – $95.00
            var isActive = i % 20 != 0;                           // every 20th employee has left

            employees.Add(new Employee(EmployeeId(i), name, $"employee{i:D3}@siteledger.example", hourlyRateUsd, isActive, SeedTimestamp));
        }

        return employees;
    }

    // assignId swaps the domain's random Guid for a stable one; the domain deliberately offers no Id setter.
    public static SeedData BuildFinancialData(IReadOnlyList<Employee> employees, Action<object, Guid> assignId)
    {
        var random = new Random(Seed + 1);
        var projects = new List<Project>(ProjectCount);
        var projectNumber = new Dictionary<Guid, int>();

        for (var i = 1; i <= ProjectCount; i++)
        {
            var status = i >= FirstDraftProject ? ProjectStatus.Draft : ProjectStatus.Active;
            var name = $"{Sites[random.Next(Sites.Length)]} {Scopes[random.Next(Scopes.Length)]}";
            var project = Project.Create($"{ProjectCodePrefix}{i:D3}", name, 1_000_000m, status, ProjectsCreatedAt); // budget set below
            assignId(project, StableId('a', i));
            projects.Add(project);
            projectNumber[project.Id] = i;
        }

        // Draft projects take no submissions; the 10 busiest projects are 4× as likely to be picked.
        var submissionTargets = projects
            .Take(FirstDraftProject - 1)
            .SelectMany((p, index) => Enumerable.Repeat(p, index < HeavyProjectCount ? 4 : 1))
            .ToList();
        var activeEmployees = employees.Where(e => e.IsActive).ToList();
        var audits = new List<ApprovalAudit>();
        var workDays = LastWorkDay.DayNumber - FirstWorkDay.DayNumber + 1;

        ApprovalAudit? Decide(Submission submission, DateTimeOffset submittedAt)
        {
            var mustBeDecided = projectNumber[submission.ProjectId] >= FirstCompletedProject; // completed projects have no pending items
            var roll = random.Next(100);
            var decidedAt = submittedAt.AddHours(random.Next(4, 120));
            var approver = $"seed-approver-{random.Next(1, 6)}";

            ApprovalAudit? audit = roll switch
            {
                < 60 => submission.Approve(approver, random.Next(3) == 0 ? "Verified against site records" : null, decidedAt),
                < 85 when mustBeDecided => submission.Approve(approver, null, decidedAt),
                < 85 => null, // stays Pending
                _ => submission.Reject(approver, RejectionReasons[random.Next(RejectionReasons.Length)], decidedAt),
            };

            if (audit is not null)
            {
                assignId(audit, StableId('d', audits.Count + 1));
                audits.Add(audit);
            }

            return audit;
        }

        var timesheets = new List<Timesheet>(TimesheetCount);
        var usedDays = new HashSet<(Guid ProjectId, Guid EmployeeId, DateOnly Day)>();
        for (var i = 1; i <= TimesheetCount; i++)
        {
            Project project;
            Employee employee;
            DateOnly day;
            do
            {
                project = submissionTargets[random.Next(submissionTargets.Count)];
                employee = activeEmployees[random.Next(activeEmployees.Count)];
                day = FirstWorkDay.AddDays(random.Next(workDays));
            }
            while (!usedDays.Add((project.Id, employee.Id, day))); // respects the unique (project, employee, day) index

            var hours = random.Next(4, 49) * 0.25m; // 1.00–12.00 in quarter hours
            var submittedAt = EndOfDay(day, random);
            var timesheet = Timesheet.Submit(project, employee, day, hours, $"seed-pm-{random.Next(1, 11):D2}", submittedAt);
            assignId(timesheet, StableId('b', i));
            Decide(timesheet, submittedAt);
            timesheets.Add(timesheet);
        }

        var categories = ExpenseProfiles.Keys.ToArray();
        var expenses = new List<Expense>(ExpenseCount);
        for (var i = 1; i <= ExpenseCount; i++)
        {
            var project = submissionTargets[random.Next(submissionTargets.Count)];
            var category = categories[random.Next(categories.Length)];
            var (minUsd, maxUsd, descriptions) = ExpenseProfiles[category];
            var amount = random.Next(minUsd * 100, (maxUsd * 100) + 1) / 100m;
            var day = FirstWorkDay.AddDays(random.Next(workDays));
            var reference = category is ExpenseCategory.Materials or ExpenseCategory.Equipment && random.Next(10) < 7
                ? $"PO-{random.Next(1_000, 10_000)}"
                : null;
            var submittedAt = EndOfDay(day, random);
            var expense = Expense.Submit(
                project, descriptions[random.Next(descriptions.Length)], category, amount, day, reference, MaxExpenseUsd, $"seed-pm-{random.Next(1, 11):D2}", submittedAt);
            assignId(expense, StableId('c', i));
            Decide(expense, submittedAt);
            expenses.Add(expense);
        }

        SetBudgetsAndCompleteProjects(projects, timesheets, expenses, random);
        return new SeedData(projects, timesheets, expenses, audits);
    }

    public static Guid EmployeeId(int index) => StableId('e', index);

    // a = project, b = timesheet, c = expense, d = audit, e = employee.
    public static Guid StableId(char kind, int index) => Guid.Parse($"{kind}0000000-0000-4000-8000-{index:D12}");

    private static void SetBudgetsAndCompleteProjects(List<Project> projects, List<Timesheet> timesheets, List<Expense> expenses, Random random)
    {
        var amounts = timesheets.Select(t => (t.ProjectId, t.Status, Amount: t.LaborCostUsd))
            .Concat(expenses.Select(e => (e.ProjectId, e.Status, Amount: e.AmountUsd)))
            .ToList();

        for (var i = 1; i <= projects.Count; i++)
        {
            var project = projects[i - 1];
            var approved = amounts.Where(a => a.ProjectId == project.Id && a.Status == SubmissionStatus.Approved).Sum(a => a.Amount);
            var pending = amounts.Where(a => a.ProjectId == project.Id && a.Status == SubmissionStatus.Pending).Sum(a => a.Amount);

            decimal budget;
            if (OverBudgetProjects.Contains(i))
            {
                budget = Math.Max(1_000m, Math.Floor(approved * 0.85m / 100m) * 100m); // ~15 % overrun
            }
            else if (approved + pending == 0)
            {
                budget = random.Next(50, 501) * 1_000m;
            }
            else
            {
                var headroom = 1.3m + (random.Next(0, 271) / 100m); // 1.30× – 4.00× of committed spend
                budget = Math.Max(25_000m, Math.Ceiling((approved + pending) * headroom / 1_000m) * 1_000m);
            }

            project.ChangeBudget(budget, BudgetsReviewedAt);

            if (i >= FirstCompletedProject && i < FirstDraftProject)
            {
                project.ChangeStatus(ProjectStatus.Completed, hasPendingSubmissions: false, BudgetsReviewedAt);
            }
        }
    }

    private static DateTimeOffset EndOfDay(DateOnly day, Random random) =>
        new DateTimeOffset(day.ToDateTime(new TimeOnly(17, 0)), TimeSpan.Zero).AddMinutes(random.Next(0, 240));
}
