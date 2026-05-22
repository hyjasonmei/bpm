using Bpm.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Common;

public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, problem) = exception switch
        {
            ValidationException ve => (StatusCodes.Status400BadRequest, BuildValidation(ve)),
            NotFoundException nf => (StatusCodes.Status404NotFound, Build("Not Found", nf.Message, 404)),
            ForbiddenException fe => (StatusCodes.Status403Forbidden, Build("Forbidden", fe.Message, 403)),
            ConflictException ce => (StatusCodes.Status409Conflict, Build("Conflict", ce.Message, 409)),
            _ => (0, (ProblemDetails?)null)
        };

        if (problem is null) return false;

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }

    private static ProblemDetails Build(string title, string detail, int status) => new()
    {
        Title = title,
        Detail = detail,
        Status = status,
    };

    private static ValidationProblemDetails BuildValidation(ValidationException ex) => new(ex.Errors.ToDictionary(kv => kv.Key, kv => kv.Value))
    {
        Title = "Validation failed",
        Status = StatusCodes.Status400BadRequest,
    };
}
