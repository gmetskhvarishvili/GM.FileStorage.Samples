using GM.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Globalization;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// Answers <see cref="RateLimitExceededException"/> (from <see cref="UploadQuota"/>) with 429 Too Many Requests and a
/// Retry-After header, the way the gateway answers its own limits.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TooManyUploadsAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not RateLimitExceededException limited)
            return;

        if (limited.RetryAfter is { } retryAfter)
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many uploads.",
            Detail = $"At most {limited.Result.Limit} files a minute can be uploaded. Nothing from this request was kept.",
            Instance = context.HttpContext.Request.Path,
        })
        { StatusCode = StatusCodes.Status429TooManyRequests };
        context.ExceptionHandled = true;
    }
}
