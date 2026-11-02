using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Usterki.Api.Errors;

public static class ApiErrors
{
    public static ProblemDetails Create(
        HttpContext context,
        int status,
        string? code = null,
        string? detail = null)
    {
        var problem = new ProblemDetails
        {
            Type = "about:blank",
            Status = status,
            Title = GetTitle(status),
            Detail = detail
        };

        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        Enrich(context, problem);

        return problem;
    }

    public static void Enrich(
        HttpContext context,
        ProblemDetails problem)
    {
        var status = problem.Status
            ?? StatusCodes.Status500InternalServerError;

        problem.Status = status;
        problem.Type ??= "about:blank";
        problem.Title ??= GetTitle(status);
        problem.Instance ??= context.Request.Path.Value;

        if (!problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] = GetCode(status);
        }

        problem.Extensions["traceId"] =
            Activity.Current?.Id ?? context.TraceIdentifier;
    }

    public static async Task WriteAsync(
        HttpContext context,
        ProblemDetails problem)
    {
        Enrich(context, problem);

        context.Response.StatusCode =
            problem.Status ?? StatusCodes.Status500InternalServerError;

        context.Response.Headers["Cache-Control"] = "no-store";

        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }

    private static string GetTitle(int status) => status switch
    {
        400 => "Nieprawidłowe żądanie",
        401 => "Wymagane uwierzytelnienie",
        403 => "Brak uprawnień",
        404 => "Nie znaleziono zasobu",
        405 => "Niedozwolona metoda",
        409 => "Konflikt danych",
        413 => "Żądanie jest zbyt duże",
        415 => "Nieobsługiwany typ danych",
        429 => "Zbyt wiele żądań",
        _ => "Błąd obsługi żądania"
    };

    private static string GetCode(int status) => status switch
    {
        400 => "bad_request",
        401 => "unauthorized",
        403 => "forbidden",
        404 => "not_found",
        405 => "method_not_allowed",
        409 => "conflict",
        413 => "payload_too_large",
        415 => "unsupported_media_type",
        429 => "rate_limit_exceeded",
        >= 500 => "internal_error",
        _ => "request_failed"
    };
}