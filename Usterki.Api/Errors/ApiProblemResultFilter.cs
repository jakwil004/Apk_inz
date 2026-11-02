using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Usterki.Api.Errors;

public sealed class ApiProblemResultFilter : IAlwaysRunResultFilter
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public void OnResultExecuting(ResultExecutingContext context)
    {
        var status = context.Result switch
        {
            ObjectResult result =>
                result.StatusCode ??
                (result.Value as ProblemDetails)?.Status ??
                StatusCodes.Status200OK,

            StatusCodeResult result => result.StatusCode,

            _ => StatusCodes.Status200OK
        };

        if (status < 400)
        {
            return;
        }

        ProblemDetails problem;

        if (status >= 500)
        {
            
            problem = ApiErrors.Create(
                context.HttpContext,
                status,
                detail: "Wystąpił błąd serwera. Spróbuj ponownie później.");
        }
        else if (context.Result is ObjectResult
        {
            Value: ProblemDetails existingProblem
        })
        {
            problem = existingProblem;
            problem.Status = status;

            ApiErrors.Enrich(context.HttpContext, problem);
        }
        else
        {
            problem = ApiErrors.Create(context.HttpContext, status);

            if (context.Result is ObjectResult { Value: not null } result)
            {
                var body = JsonSerializer.SerializeToElement(
                    result.Value,
                    result.Value.GetType(),
                    JsonOptions);

                if (body.ValueKind == JsonValueKind.Object)
                {
                    if (body.TryGetProperty("message", out var message) &&
                        message.ValueKind == JsonValueKind.String)
                    {
                        problem.Detail = message.GetString();
                    }

                    //zmiana sprawdzania błędów walidacji, aby dopasować do struktury ApiProblemResponse
                    if (body.TryGetProperty("errors", out var errors) &&
                        errors.ValueKind == JsonValueKind.Array)
                    {
                        var descriptions = new List<string>();

                        foreach (var error in errors.EnumerateArray())
                        {
                            if (error.ValueKind == JsonValueKind.Object &&
                                error.TryGetProperty(
                                    "description",
                                    out var description) &&
                                description.ValueKind == JsonValueKind.String)
                            {
                                descriptions.Add(
                                    description.GetString()!);
                            }
                        }

                        if (descriptions.Count > 0)
                        {
                            problem.Extensions["code"] =
                                "validation_failed";

                            problem.Extensions["errors"] =
                                new Dictionary<string, string[]>
                                {
                                    ["request"] = descriptions.ToArray()
                                };
                        }
                    }
                }
            }
        }

        context.HttpContext.Response.Headers["Cache-Control"] = "no-store";

        var normalizedResult = new ObjectResult(problem)
        {
            StatusCode = status
        };

        normalizedResult.ContentTypes.Add("application/problem+json");

        context.Result = normalizedResult;
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}