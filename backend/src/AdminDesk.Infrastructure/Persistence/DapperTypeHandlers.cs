using System.Data;
using System.Globalization;
using Dapper;

namespace AdminDesk.Infrastructure.Persistence;

public static class DapperTypeHandlers
{
    public const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    public const string DateFormat = "yyyy-MM-dd";

    private static int _registered;

    // Safe to call more than once.
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
    }

    private sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToUniversalTime().ToString(DateTimeFormat, CultureInfo.InvariantCulture);
        }

        public override DateTime Parse(object value)
        {
            if (value is DateTime dt)
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            return DateTime.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture)!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        public override DateOnly Parse(object value)
        {
            if (value is DateTime dt)
            {
                return DateOnly.FromDateTime(dt);
            }
            return DateOnly.ParseExact(
                Convert.ToString(value, CultureInfo.InvariantCulture)![..10],
                DateFormat,
                CultureInfo.InvariantCulture);
        }
    }
}
