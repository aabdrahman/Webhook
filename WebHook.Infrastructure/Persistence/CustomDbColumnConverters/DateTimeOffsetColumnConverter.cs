using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

/// <summary>
/// Converts <see cref="DateTimeOffset"/> values to UTC before they are written, as PostgreSQL
/// <c>timestamptz</c> columns require. Applied to every <see cref="DateTimeOffset"/> property by convention.
/// </summary>
internal class DateTimeOffsetColumnConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    /// <summary>Initialises a new instance of the <see cref="DateTimeOffsetColumnConverter"/> class.</summary>
    public DateTimeOffsetColumnConverter() : base(c => c.ToUniversalTime(), c => c)
    {

    }
}