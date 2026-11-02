using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Contracts.Common;

public sealed class ApiProblemResponse
{
    public string? Type { get; set; }

    public string? Title { get; set; }

    public int Status { get; set; }

    public string? Detail { get; set; }

    public string? Instance { get; set; }

    public string Code { get; set; } = string.Empty;

    public string TraceId { get; set; } = string.Empty;

    public Dictionary<string, string[]>? Errors { get; set; }
}