using GM.API.Application.Models;
using Mapster;
using System;
using System.Globalization;

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
    }
}
