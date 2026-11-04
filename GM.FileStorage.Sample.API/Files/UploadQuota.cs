using GM.FileStorage.Sample.Application.Files.Commands.UploadFiles;
using GM.RateLimiting;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// How many files a client may upload: <c>RateLimiting:Policies:uploaded-files</c> (30 a minute), counted per file
/// rather than per request, since one batch holds up to 20. Single and batch uploads share it. The count lives in
/// GM.RateLimiting's store, so with Redis it holds across every API instance. Over the limit,
/// <see cref="RateLimitExceededException"/> is thrown (answered with 429 and Retry-After); a batch it stops is refused
/// whole.
/// </summary>
public sealed class UploadQuota(IRateLimiterService limiter)
{
    public const string Policy = "uploaded-files";

    /// <summary>Takes one file's permit for the client, or throws.</summary>
    public async Task TakeAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var result = await limiter.TryAcquireAsync(RateLimitKey.ForIp(ClientIp(http)), Policy, cancellationToken);
        if (!result.IsAllowed)
            throw new RateLimitExceededException(result);
    }

    /// <summary>The same files, each taking a permit as it arrives (before it's stored).</summary>
    public async IAsyncEnumerable<UploadFilesItem> CountAsync(
        IAsyncEnumerable<UploadFilesItem> files, HttpContext http, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var file in files.WithCancellation(cancellationToken))
        {
            await TakeAsync(http, cancellationToken);
            yield return file;
        }
    }

    // Behind the gateway the connection is the gateway's, so the client is the first X-Forwarded-For entry (YARP adds
    // it). The API is only meant to be reached through the gateway; called directly, a client could claim any address.
    private static string ClientIp(HttpContext http) =>
        http.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() is { Length: > 0 } forwarded
            ? forwarded
            : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
