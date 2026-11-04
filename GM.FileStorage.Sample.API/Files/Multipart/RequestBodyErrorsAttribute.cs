using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace GM.FileStorage.Sample.API.Files.Multipart;

/// <summary>
/// For actions that read the request body themselves: the server's complaints about it (a body over
/// <c>Kestrel:Limits:MaxRequestBodySize</c>, a client sending too slowly) surface as exceptions in the action. They are
/// answered with their own status (413, 408, …) instead of falling through to a 500.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequestBodyErrorsAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context) => Handle(context);

    internal static void Handle(ExceptionContext context)
    {
        if (context.Exception is not BadHttpRequestException badRequest)
            return;

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = badRequest.StatusCode,
            Title = "The request body couldn't be read.",
            Detail = badRequest.Message,
            Instance = context.HttpContext.Request.Path,
        })
        { StatusCode = badRequest.StatusCode };
        context.ExceptionHandled = true;
    }
}
