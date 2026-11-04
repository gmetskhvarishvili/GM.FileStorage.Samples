using GM.Exceptions;
using GM.FileStorage.Sample.Common.Resources;
using GM.FileStorage.Sample.Domain.SeedWork;
using GM.Mediator.Contracts;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GM.FileStorage.Sample.Application.Files.Commands.ReceiveDirectUpload;

/// <summary>
/// The write end of a direct upload on local disk. S3 and Azure accept the presigned PUT themselves; local disk has
/// no server, so the API serves the link and stores the body here. Only a pending, unexpired direct upload's key can
/// be written. The file is recorded later, when the client completes the upload.
/// </summary>
public class ReceiveDirectUploadCommand : IRequest
{
    public string? StorageKey { get; set; }
    public Stream? Content { get; set; }
}

/// <remarks>
/// GM.FileStorage's upload rules apply as for any upload (FileStorage:MaxFileSize, AllowedContentTypes,
/// VerifyContentType), so a local direct upload is no larger than a normal one: on local disk the bytes come through
/// the API either way.
/// </remarks>
public class ReceiveDirectUploadCommandHandler(IUnitOfWork unitOfWork, IFileStorageService storage)
    : IRequestHandler<ReceiveDirectUploadCommand>
{
    public async Task Handle(ReceiveDirectUploadCommand request, CancellationToken cancellationToken)
    {
        var upload = await unitOfWork.DirectUploadRepository
            .FirstOrDefaultAsync(x => x.StorageKey == request.StorageKey && x.ExpiresAtUtc > DateTime.UtcNow, true, null, cancellationToken)
            ?? throw new NotFoundException(StringResource.File, StringResource.StorageKey, request.StorageKey!);

        try
        {
            await storage.UploadAsync(new FileUploadRequest(request.Content!, upload.FileName, upload.ContentType)
            {
                Key = upload.StorageKey,
                Visibility = FileVisibility.Private,
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is FileTooLargeException or FileContentTypeNotAllowedException or FileContentTypeMismatchException)
        {
            throw new BadRequestException(ex.Message);
        }
    }
}
