namespace AdminDesk.SharedKernel.Time;

public static class IndiaTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    // The zone id differs between Windows and Linux, so try both and fall back to a fixed offset.
    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India Standard Time", "India Standard Time");
    }

    public static DateTimeOffset Now(TimeProvider clock) =>
        TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone);

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(Now(clock).DateTime);

    public static int Year(TimeProvider clock) => Today(clock).Year;
}
