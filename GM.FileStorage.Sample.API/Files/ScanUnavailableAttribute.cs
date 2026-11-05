using GM.FileStorage.MalwareScanner;
using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// Answers <see cref="MalwareScanFailedException"/> (malware scanning is on but ClamAV can't give a verdict) with 503
/// Service Unavailable and a Retry-After header: the upload was refused unscanned and nothing was kept, so the client
/// can simply try again.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ScanUnavailableAttribute : Attribute, IExceptionFilter
{
    private const int RetryAfterSeconds = 30;

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not MalwareScanFailedException)
            return;

        context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Uploads can't be scanned right now.",
            Detail = "Malware scanning is on, but the scanner didn't answer. Nothing was kept; try again shortly.",
            Instance = context.HttpContext.Request.Path,
        })
        { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }
}
