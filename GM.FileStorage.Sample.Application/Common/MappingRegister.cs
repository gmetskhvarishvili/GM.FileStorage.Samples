using GM.API.Application.Models;
using GM.FileStorage.Sample.Application.Files.Queries.GetFileDetails;
using GM.FileStorage.Sample.Application.Files.Queries.GetFilesList;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using Mapster;
using System;
using System.Globalization;
using System.Linq;

namespace GM.FileStorage.Sample.Application.Common;

/// <summary>
/// Mapster configuration for the application layer. Registered by scanning this assembly at
/// startup. Maps audit timestamps to the <see cref="AuditableDto.DateTimeFormat"/> string form so
/// every list/details DTO returns CreatedAt/UpdatedAt as "dd/MM/yyyy HH:mm:ss".
/// </summary>
public class MappingRegister : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<DateTime, string>()
            .MapWith(src => src.ToString(AuditableDto.DateTimeFormat, CultureInfo.InvariantCulture));

        config.NewConfig<DateTime?, string?>()
            .MapWith(src => src.HasValue
                ? src.Value.ToString(AuditableDto.DateTimeFormat, CultureInfo.InvariantCulture)
                : null);

        // The metadata rows flatten to the name/value map the file was uploaded with (deleted rows left out).
        config.NewConfig<StoredFile, StoredFileDto>()
            .Map(dest => dest.Metadata, src => src.Metadata
                .Where(x => !x.IsDeleted).ToDictionary(x => x.Name, x => x.Value));
        config.NewConfig<StoredFile, StoredFileDetailsDto>()
            .Map(dest => dest.Metadata, src => src.Metadata
                .Where(x => !x.IsDeleted).ToDictionary(x => x.Name, x => x.Value));
    }
}
