using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteLedger.Application.Exceptions;
using SiteLedger.Domain.Exceptions;

namespace SiteLedger.Api.Errors;

// The single place exceptions become RFC 7807 responses; unknown errors are a generic 500 with no internals.
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            DomainValidationException e => new ValidationProblemDetails(new Dictionary<string, string[]> { [e.Field] = [e.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            ResourceNotFoundException e => Problem(StatusCodes.Status404NotFound, "Resource not found", e.Message),
            DomainConflictException e => Problem(StatusCodes.Status409Conflict, "Conflict", e.Message),
            DuplicateResourceException e => Problem(StatusCodes.Status409Conflict, "Duplicate resource", e.Message),
            DbUpdateConcurrencyException => Problem(
                StatusCodes.Status409Conflict,
                "Concurrency conflict",
                "The record was changed by another request. Reload it and try again."),
            _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected with {StatusCode}: {Reason}", problem.Status, exception.Message);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string? detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
