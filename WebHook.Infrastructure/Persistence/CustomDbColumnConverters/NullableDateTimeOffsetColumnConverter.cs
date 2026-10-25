using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

/// <summary>
/// Nullable counterpart of <see cref="DateTimeOffsetColumnConverter"/>: converts non-null values to UTC
/// before they are written. Applied to every <see cref="Nullable{DateTimeOffset}"/> property by convention.
/// </summary>
internal class NullableDateTimeOffsetColumnConverter : ValueConverter<DateTimeOffset?, DateTimeOffset?>
{
    /// <summary>Initialises a new instance of the <see cref="NullableDateTimeOffsetColumnConverter"/> class.</summary>
    public NullableDateTimeOffsetColumnConverter() : base(c => c.HasValue ? c.Value.ToUniversalTime() : c, c => c)
    {

    }
}