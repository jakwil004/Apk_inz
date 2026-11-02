using Microsoft.AspNetCore.Diagnostics;

namespace Usterki.Api.Errors;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted ||
            httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        if (exception is BadHttpRequestException badRequest)
        {
            var requestProblem = ApiErrors.Create(
                httpContext,
                badRequest.StatusCode,
                detail: "Żądanie ma nieprawidłowy format lub rozmiar.");

            await ApiErrors.WriteAsync(httpContext, requestProblem);

            return true;
        }

        var problem = ApiErrors.Create(
            httpContext,
            StatusCodes.Status500InternalServerError,
            code: "internal_error",
            detail: "Wystąpił błąd serwera. Spróbuj ponownie później.");

        _logger.LogError(
            exception,
            "Nieobsłużony wyjątek API. TraceId: {TraceId}",
            problem.Extensions["traceId"]);

        await ApiErrors.WriteAsync(httpContext, problem);

        return true;
    }
}