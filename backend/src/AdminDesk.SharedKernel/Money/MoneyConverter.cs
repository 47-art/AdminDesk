namespace AdminDesk.SharedKernel.Money;

// The single place where rupees and paise are converted.
public static class MoneyConverter
{
    // The largest amount any money value may hold, in rupees and in paise.
    public const decimal MaxRupees = 1_000_000_000m;
    public const long MaxMinor = 100_000_000_000L;

    public const string TooLargeMessage = "Enter a smaller amount.";

    public static bool IsWithinLimit(decimal rupees) => Math.Abs(rupees) <= MaxRupees;

    public static long ToMinor(decimal rupees)
    {
        var rounded = Math.Round(rupees, 2, MidpointRounding.AwayFromZero);
        return checked((long)(rounded * 100m));
    }

    public static decimal ToRupees(long minor)
    {
        var whole = minor / 100;
        var fraction = minor % 100;
        return whole + fraction / 100m;
    }
}
