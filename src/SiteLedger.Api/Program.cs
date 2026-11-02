using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Formatting.Compact;
using SiteLedger.Api.Authentication;
using SiteLedger.Api.Errors;
using SiteLedger.Api.Middleware;
using SiteLedger.Application;
using SiteLedger.Application.Abstractions;
using SiteLedger.Application.Options;
using SiteLedger.Infrastructure;
using SiteLedger.Infrastructure.Persistence;
using SiteLedger.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs on stdout; levels come from the "Serilog" config section.
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// Never stored in source: user-secrets locally, environment variables / App Service settings elsewhere.
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured. For local development set it with " +
        "'dotnet user-secrets set ConnectionStrings:Default <value> --project src/SiteLedger.Api'.");
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSiteLedgerAuthentication(builder.Configuration);

// Bad configuration (e.g. a negative maximum) stops the app at startup instead of failing requests later.
builder.Services.AddOptions<ExpenseOptions>()
    .Bind(builder.Configuration.GetSection(ExpenseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Enums travel as names ("Active"), never numbers; unknown names are a 400.
var enumConverter = new JsonStringEnumConverter(allowIntegerValues: false);
builder.Services
    .AddControllers(options =>
    {
        // Validation error keys use JSON names ("budgetUsd"), matching domain errors.
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());

        // Stops a bad JSON value from also adding a misleading "field is required" error.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(enumConverter);

        // Hide parser internals: "$.status: The input was not valid."
        options.AllowInputFormatterExceptionMessages = false;
    });
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(enumConverter));

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
        if (context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    });
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

// /health/ready checks SQL Server; /health/live runs no checks.
builder.Services.AddHealthChecks().AddDbContextCheck<SiteLedgerDbContext>("database", tags: ["ready"]);

// Bounded: a database still resuming from auto-pause reports 503 after 10 s.
builder.Services.Configure<HealthCheckServiceOptions>(options =>
    options.Registrations.Single(registration => registration.Name == "database").Timeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

// Dev-only: `dotnet run --project src/SiteLedger.Api -- seed [--reset]` seeds and exits (--reset DELETES all rows).
if (args.Contains("seed"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("The seed command only runs in the Development environment.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SiteLedgerDbContext>();
    if (args.Contains("--reset"))
    {
        app.Logger.LogWarning("Seed --reset: deleting all rows in database {Database}", db.Database.GetDbConnection().Database);
        await DemoDataSeeder.ResetAsync(db);
    }

    await DemoDataSeeder.SeedAsync(db, app.Logger);
    return;
}

// Order matters: each middleware wraps everything registered after it.
app.UseMiddleware<CorrelationIdMiddleware>();   // 1. id first, so every later log line carries it
app.UseSerilogRequestLogging(options =>         // 2. one summary line per request: method, path, status, ms
    options.EnrichDiagnosticContext = (diagnostics, http) =>
    {
        if (http.User.FindFirst("sub")?.Value is { } subject)
        {
            diagnostics.Set("UserSubject", subject);
        }
    });
app.UseExceptionHandler();                      // 3. exceptions → ProblemDetails (logged inside step 2)
app.UseStatusCodePages();                       // 4. bodies for bare 401/403/404

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();                        // 5. who are you? (validates the JWT)
app.UseAuthorization();                         // 6. may you do this? (role policies)

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapControllers();

app.Run();

// Lets WebApplicationFactory<Program> host the API in integration tests.
public partial class Program;
