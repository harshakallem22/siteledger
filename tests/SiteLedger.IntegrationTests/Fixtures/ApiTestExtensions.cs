using System.Net;
using System.Net.Http.Json;
using SiteLedger.Application.Contracts.Expenses;
using SiteLedger.Application.Contracts.Projects;
using SiteLedger.Application.Contracts.Timesheets;
using SiteLedger.Domain.Entities;

namespace SiteLedger.IntegrationTests.Fixtures;

internal static class ApiTestExtensions
{
    public static async Task<ProjectResponse> CreateProjectAsync(this HttpClient client, string code, string status = "Active")
    {
        var response = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            projectCode = code,
            name = "Fremont Equipment Bay Upgrade",
            budgetUsd = 250000.00m,
            status,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ProjectResponse>();
    }

    public static async Task<TimesheetResponse> SubmitTimesheetAsync(this HttpClient client, Guid projectId, Guid employeeId, DateOnly workDate, decimal hours = 8m)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/timesheets", new { employeeId, workDate, hours });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<TimesheetResponse>();
    }

    public static async Task<ExpenseResponse> SubmitExpenseAsync(this HttpClient client, Guid projectId, decimal amountUsd = 1250.00m)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/expenses", new
        {
            description = "Electrical conduit and mounting hardware",
            category = "Materials",
            amountUsd,
            expenseDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            externalReference = "PO-1873",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ExpenseResponse>();
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(SiteLedgerApiFactory.Json)
        ?? throw new InvalidOperationException("Empty response body.");

    // Employees have no API (seed-managed), so tests insert them directly.
    public static async Task<Employee> AddEmployeeAsync(this SiteLedgerApiFactory factory, decimal hourlyRateUsd, bool isActive = true)
    {
        var id = Guid.NewGuid();
        var employee = new Employee(id, "Test Employee", $"{id:N}@siteledger.example", hourlyRateUsd, isActive, DateTimeOffset.UtcNow);
        await factory.QueryDbAsync(async db =>
        {
            db.Employees.Add(employee);
            return await db.SaveChangesAsync();
        });
        return employee;
    }
}
