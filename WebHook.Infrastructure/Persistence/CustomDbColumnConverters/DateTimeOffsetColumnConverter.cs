using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WebHook.Infrastructure.Data_Persistence.CustomDbColumnConverters;

internal class DateTimeOffsetColumnConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public DateTimeOffsetColumnConverter() : base(c => c.ToUniversalTime(), c => c)
    {
        
    }
}
