using System.Globalization;

namespace RixelPOS.Domain.Common;

/// <summary>
/// An exact monetary amount, always stored as integer cents (never floating point).
/// </summary>
public readonly record struct Money(long Cents) : IComparable<Money>
{
    public static readonly Money Zero = new(0);

    public decimal Amount => Cents / 100m;

    public static Money FromCents(long cents) => new(cents);

    public static Money FromAmount(decimal amount) => new(ToCents(amount));

    public static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    public static Money operator +(Money a, Money b) => new(checked(a.Cents + b.Cents));
    public static Money operator -(Money a, Money b) => new(a.Cents - b.Cents);
    public static Money operator -(Money a) => new(-a.Cents);
    public static Money operator *(Money a, decimal factor) => FromCents(ToCents(a.Amount * factor));
    public static bool operator >(Money a, Money b) => a.Cents > b.Cents;
    public static bool operator <(Money a, Money b) => a.Cents < b.Cents;
    public static bool operator >=(Money a, Money b) => a.Cents >= b.Cents;
    public static bool operator <=(Money a, Money b) => a.Cents <= b.Cents;

    public int CompareTo(Money other) => Cents.CompareTo(other.Cents);

    public string ToPlainString() => Amount.ToString("0.00", CultureInfo.InvariantCulture);

    public override string ToString() => Amount.ToString("N2", CultureInfo.InvariantCulture);
}
