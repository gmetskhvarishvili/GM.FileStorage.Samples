using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Collections.Generic;
using System.Reflection;

namespace GM.FileStorage.Sample.API.Files.Multipart;

/// <summary>
/// Describes the file field of a <see cref="StreamedMultipartUploadAttribute"/> action in the OpenAPI document. Those
/// actions take no <c>IFormFile</c> (binding one would buffer the upload), so Swagger can't infer the field;
/// this adds it, so Swagger UI still offers a file picker (or several).
/// </summary>
public sealed class MultipartUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.GetCustomAttribute<StreamedMultipartUploadAttribute>() is not { } upload)
            return;

        var binary = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };
        IOpenApiSchema field = upload.Multiple
            ? new OpenApiSchema { Type = JsonSchemaType.Array, Items = binary }
            : binary;

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema> { [upload.FieldName] = field },
                        Required = new HashSet<string> { upload.FieldName },
                    },
                },
            },
        };
    }
}
