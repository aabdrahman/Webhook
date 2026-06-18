using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

internal class NullableDateTimeOffsetColumnConverter : ValueConverter<DateTimeOffset?, DateTimeOffset?>
{
    public NullableDateTimeOffsetColumnConverter() : base(c => c.HasValue ? c.Value.ToUniversalTime() : c, c => c)
    {
        
    }
}