using GM.Exceptions;
using GM.FileStorage.Sample.Application.Files.Commands.UploadFiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GM.FileStorage.Sample.API.Files.Multipart;

/// <summary>
/// Reads the files of a <c>multipart/form-data</c> request straight off the request body, one at a time, without
/// buffering them. Each file's <see cref="UploadFilesItem.Content"/> is the network stream for that part: it must be
/// read (or left) before moving to the next file, after which it's no longer readable. Parts that aren't files
/// (plain form fields) are skipped.
/// </summary>
public static class MultipartFiles
{
    // RFC 2046 caps a boundary at 70 characters.
    private const int MaxBoundaryLength = 70;

    public static async IAsyncEnumerable<UploadFilesItem> ReadAsync(
        HttpRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("Send the files as multipart/form-data.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > MaxBoundaryLength)
            throw new BadRequestException("The multipart boundary is missing or too long.");

        var reader = new MultipartReader(boundary, request.Body);
        // Reading the next section first skips whatever is left of the previous one.
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = HeaderUtilities.RemoveQuotes(
                disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
            if (string.IsNullOrEmpty(fileName))
                continue; // a plain form field, not a file

            yield return new UploadFilesItem
            {
                Content = section.Body,
                FileName = fileName,
                ContentType = string.IsNullOrWhiteSpace(section.ContentType) ? "application/octet-stream" : section.ContentType,
            };
        }
    }
}
