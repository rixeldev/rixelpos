namespace RixelPOS.Domain.Common;

public enum DiscountType
{
    None = 0,
    Percent = 1,
    Fixed = 2
}

/// <summary>
/// A discount expressed either as basis points (1/100 of a percent, 10000 = 100%)
/// or as a fixed amount in cents. Stored as integers to keep arithmetic exact.
/// </summary>
public readonly record struct Discount(DiscountType Type, long Value)
{
    public static Discount None => new(DiscountType.None, 0);

    public static Discount Percent(decimal percent)
    {
        var bp = (long)Math.Round(percent * 100m, MidpointRounding.AwayFromZero);
        return new(DiscountType.Percent, Math.Clamp(bp, 0, 10_000));
    }

    public static Discount PercentBasisPoints(int basisPoints) =>
        new(DiscountType.Percent, Math.Clamp(basisPoints, 0, 10_000));

    public static Discount Fixed(Money amount) =>
        new(DiscountType.Fixed, Math.Max(0, amount.Cents));

    public bool IsNone => Type == DiscountType.None || Value <= 0;

    public decimal PercentValue => Value / 100m;

    public Money FixedValue => Money.FromCents(Value);

    /// <summary>Computes the discount in cents over a base amount, never exceeding the base.</summary>
    public long ComputeCents(long baseCents)
    {
        if (baseCents <= 0 || IsNone)
        {
            return 0;
        }

        return Type switch
        {
            DiscountType.Percent => Math.Min(baseCents, (long)Math.Round(baseCents * (decimal)Value / 10_000m, MidpointRounding.AwayFromZero)),
            DiscountType.Fixed => Math.Min(baseCents, Value),
            _ => 0
        };
    }

    public string Describe() => Type switch
    {
        DiscountType.Percent => $"{PercentValue:0.##}%",
        DiscountType.Fixed => $"$ {FixedValue.ToPlainString()}",
        _ => "-"
    };
}
