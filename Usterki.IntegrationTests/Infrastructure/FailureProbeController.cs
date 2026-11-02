using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Usterki.IntegrationTests.Infrastructure;

[ApiController]
[Route("_tests/failure")]
public sealed class FailureProbeController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Fail()
    {
        throw new InvalidOperationException(
            "INTERNAL_TEST_MARKER_DO_NOT_EXPOSE");
    }
}