using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;

namespace RixelPOS.Domain.Services;

public sealed record LineTotals(
    Guid LineId,
    long GrossCents,
    long LineDiscountCents,
    long SaleDiscountCents,
    long NetCents,
    long TaxCents,
    long TotalCents);

public sealed record SaleTotals(
    long SubtotalCents,
    long LineDiscountCents,
    long SaleDiscountCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    IReadOnlyList<LineTotals> Lines)
{
    public Money Subtotal => Money.FromCents(SubtotalCents);
    public Money Discount => Money.FromCents(DiscountCents);
    public Money Tax => Money.FromCents(TaxCents);
    public Money Total => Money.FromCents(TotalCents);
}

/// <summary>
/// Pure, deterministic sale math. All amounts are integer cents and every
/// intermediate value is rounded away from zero, so totals are reproducible.
/// </summary>
public static class SaleCalculator
{
    public static SaleTotals Calculate(IEnumerable<SaleLine> lineSource, Discount saleDiscount)
    {
        var lines = lineSource.ToList();
        if (lines.Count == 0)
        {
            return new SaleTotals(0, 0, 0, 0, 0, 0, Array.Empty<LineTotals>());
        }

        var prepared = new List<(SaleLine Line, long Gross, long LineDiscount, long AfterLine)>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new BusinessRuleException("line.invalid_quantity", "Quantity must be greater than zero.");
            }

            if (line.UnitPriceCents < 0)
            {
                throw new BusinessRuleException("line.invalid_price", "Unit price cannot be negative.");
            }

            if (line.TaxRateBasisPoints is < 0 or > 10_000)
            {
                throw new BusinessRuleException("line.invalid_tax", "Tax rate must be between 0% and 100%.");
            }

            var gross = ToCents(line.Quantity * line.UnitPriceCents);
            var lineDiscount = Math.Min(line.Discount.ComputeCents(gross), gross);
            prepared.Add((line, gross, lineDiscount, gross - lineDiscount));
        }

        var baseAmount = prepared.Sum(p => p.AfterLine);
        var saleDiscountCents = Math.Min(saleDiscount.ComputeCents(baseAmount), baseAmount);
        var allocations = Allocate(saleDiscountCents, prepared.Select(p => p.AfterLine).ToArray());

        var results = new List<LineTotals>(lines.Count);
        long subtotal = 0, lineDiscountTotal = 0, taxTotal = 0, total = 0;

        for (var i = 0; i < prepared.Count; i++)
        {
            var (line, gross, lineDiscount, afterLine) = prepared[i];
            var alloc = allocations[i];
            var taxable = afterLine - alloc;

            long net, tax;
            if (line.TaxInclusive)
            {
                net = TaxInclusiveNet(taxable, line.TaxRateBasisPoints);
                tax = taxable - net;
            }
            else
            {
                net = taxable;
                tax = TaxExclusive(taxable, line.TaxRateBasisPoints);
            }

            var lineTotal = net + tax;
            results.Add(new LineTotals(line.Id, gross, lineDiscount, alloc, net, tax, lineTotal));
            subtotal += net;
            lineDiscountTotal += lineDiscount;
            taxTotal += tax;
            total += lineTotal;
        }

        return new SaleTotals(
            subtotal,
            lineDiscountTotal,
            saleDiscountCents,
            lineDiscountTotal + saleDiscountCents,
            taxTotal,
            total,
            results);
    }

    /// <summary>Calculates and writes totals back onto the sale and its lines.</summary>
    public static SaleTotals Apply(Sale sale)
    {
        var totals = Calculate(sale.Lines, sale.SaleDiscount);
        sale.SubtotalCents = totals.SubtotalCents;
        sale.DiscountCents = totals.DiscountCents;
        sale.TaxCents = totals.TaxCents;
        sale.TotalCents = totals.TotalCents;

        foreach (var line in sale.Lines)
        {
            var result = totals.Lines.FirstOrDefault(l => l.LineId == line.Id);
            if (result is null)
            {
                continue;
            }

            line.DiscountCents = result.LineDiscountCents + result.SaleDiscountCents;
            line.NetCents = result.NetCents;
            line.TaxCents = result.TaxCents;
            line.TotalCents = result.TotalCents;
        }

        return totals;
    }

    public static (long PaidCents, long ChangeCents) SumPayments(IEnumerable<Payment> payments, long totalCents)
    {
        var paid = payments.Sum(p => p.AmountCents);
        var change = Math.Max(0, paid - totalCents);
        return (paid, change);
    }

    public static long TaxExclusive(long netCents, int rateBasisPoints) =>
        rateBasisPoints <= 0 ? 0 : (long)Math.Round(netCents * (decimal)rateBasisPoints / 10_000m, MidpointRounding.AwayFromZero);

    public static long TaxInclusiveNet(long grossCents, int rateBasisPoints)
    {
        if (rateBasisPoints <= 0)
        {
            return grossCents;
        }

        return (long)Math.Round(grossCents * 10_000m / (10_000m + rateBasisPoints), MidpointRounding.AwayFromZero);
    }

    /// <summary>Splits a total across weighted buckets using the largest-remainder method.</summary>
    public static long[] Allocate(long total, IReadOnlyList<long> weights)
    {
        var allocations = new long[weights.Count];
        if (total <= 0 || weights.Count == 0)
        {
            return allocations;
        }

        var weightSum = weights.Sum();
        if (weightSum <= 0)
        {
            return allocations;
        }

        var exact = new decimal[weights.Count];
        for (var i = 0; i < weights.Count; i++)
        {
            exact[i] = (decimal)total * weights[i] / weightSum;
            allocations[i] = (long)Math.Floor(exact[i]);
        }

        var remainder = total - allocations.Sum();
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(i => exact[i] - allocations[i])
            .ToArray();

        for (var i = 0; i < remainder; i++)
        {
            allocations[order[i % order.Length]]++;
        }

        return allocations;
    }

    public static long ToCents(decimal cents) => (long)Math.Round(cents, MidpointRounding.AwayFromZero);
}
