using GM.API.Controllers;
using GM.EntityFramework.Domain.Common;
using GM.FileStorage.Sample.Application.Files.Commands.UploadFiles;
using GM.RateLimiting;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.API.Files;

/// <summary>
/// How many files a client may upload: <c>RateLimiting:Policies:uploaded-files</c> (30 a minute), counted per file
/// rather than per request, since one batch holds up to 20. Single and batch uploads share it. The count lives in
/// GM.RateLimiting's store, so with Redis it holds across every API instance. Over the limit,
/// <see cref="RateLimitExceededException"/> is thrown (GM.API answers it with 429 and Retry-After); a batch it stops is
/// refused whole.
/// </summary>
public sealed class UploadQuota(IRateLimiterService limiter, ICurrentActor actor)
{
    public const string Policy = "uploaded-files";

    /// <summary>Takes one file's permit for the client, or throws.</summary>
    public async Task TakeAsync(CancellationToken cancellationToken)
    {
        // The client's address as GM.API reads it: behind the gateway, the first X-Forwarded-For entry. The API is only
        // meant to be reached through the gateway; called directly, a client could claim any address.
        var result = await limiter.TryAcquireAsync(RateLimitKey.ForIp(actor.IpAddress ?? "unknown"), Policy, cancellationToken);
        if (!result.IsAllowed)
        {
            throw new RateLimitExceededException(result,
                $"At most {result.Limit} files a minute can be uploaded. Nothing from this request was kept.");
        }
    }

    /// <summary>The files, each taking a permit as it arrives (before it's stored).</summary>
    public async IAsyncEnumerable<UploadFilesItem> CountAsync(
        IAsyncEnumerable<MultipartFile> files, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var file in files.WithCancellation(cancellationToken))
        {
            await TakeAsync(cancellationToken);
            yield return new UploadFilesItem { Content = file.Content, FileName = file.FileName, ContentType = file.ContentType };
        }
    }
}
