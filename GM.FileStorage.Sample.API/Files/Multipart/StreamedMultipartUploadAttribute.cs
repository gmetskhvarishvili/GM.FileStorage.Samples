using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;

namespace GM.FileStorage.Sample.API.Files.Multipart;

/// <summary>
/// Marks an action that reads its multipart body itself (<see cref="MultipartFiles"/>). It removes the form value
/// providers, so model binding never reads the form — which would buffer every file (in memory, or on disk past
/// 64 KB) before the action runs. Bind only from the route and query string on such an action, never an
/// <c>IFormFile</c>. Swagger still shows the file fields (see <see cref="MultipartUploadOperationFilter"/>).
/// <para>
/// Since the action reads the body itself, the server's complaints about it (a body over
/// <c>Kestrel:Limits:MaxRequestBodySize</c>, a client sending too slowly) surface as exceptions in the action. They
/// are answered with their own status (413, 408, …) instead of falling through to a 500.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StreamedMultipartUploadAttribute(string fieldName) : Attribute, IResourceFilter, IExceptionFilter
{
    /// <summary>The multipart field the files are sent in, as shown in Swagger.</summary>
    public string FieldName { get; } = fieldName;

    /// <summary>Whether the field holds several files.</summary>
    public bool Multiple { get; init; }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    public void OnException(ExceptionContext context) => RequestBodyErrorsAttribute.Handle(context);
}
