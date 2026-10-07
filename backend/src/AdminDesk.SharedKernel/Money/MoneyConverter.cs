namespace AdminDesk.SharedKernel.Money;

// The single place where rupees and paise are converted.
public static class MoneyConverter
{
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
