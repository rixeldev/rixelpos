using RixelPOS.Application.Inventory;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Security;
using RixelPOS.Domain.Services;

namespace RixelPOS.Domain.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("10.00", 1000)]
    [InlineData("0.01", 1)]
    [InlineData("0.005", 1)]
    [InlineData("-2.50", -250)]
    public void FromAmount_Converts_To_Exact_Cents(string amount, long expected)
    {
        Assert.Equal(expected, Money.FromAmount(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)).Cents);
    }

    [Fact]
    public void Money_Arithmetic_Is_Exact()
    {
        var a = Money.FromCents(1999);
        var b = Money.FromCents(1);
        Assert.Equal(2000, (a + b).Cents);
        Assert.Equal(1998, (a - b).Cents);
        Assert.Equal(3998, (a * 2).Cents);
    }

    [Fact]
    public void Invoice_Number_Formatting_Uses_Prefix_And_Padding()
    {
        Assert.Equal("A-000123", InvoiceNumberFormatter.Format("A", 123, 6));
        Assert.Equal("000123", InvoiceNumberFormatter.Format("", 123, 6));
        Assert.Equal("F-123", InvoiceNumberFormatter.Format("F", 123, 3));
    }
}

public class DiscountTests
{
    [Fact]
    public void Percent_Discount_Computes_On_Cents()
    {
        Assert.Equal(1180, Discount.Percent(10).ComputeCents(11800));
        Assert.Equal(0, Discount.Percent(0).ComputeCents(11800));
    }

    [Fact]
    public void Fixed_Discount_Never_Exceeds_Base()
    {
        Assert.Equal(400, Discount.Fixed(Money.FromCents(500)).ComputeCents(400));
        Assert.Equal(500, Discount.Fixed(Money.FromCents(500)).ComputeCents(1000));
    }

    [Fact]
    public void Percent_Is_Clamped()
    {
        Assert.Equal(10000, Discount.Percent(150).Value);
        Assert.Equal(0, Discount.Percent(-5).Value);
    }
}

public class SaleCalculatorTests
{
    private static SaleLine Line(decimal qty, long price, int taxBp = 1800, bool inclusive = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        Quantity = qty,
        UnitPriceCents = price,
        TaxRateBasisPoints = taxBp,
        TaxInclusive = inclusive
    };

    [Fact]
    public void Tax_Exclusive_Adds_Tax()
    {
        var totals = SaleCalculator.Calculate(new[] { Line(2, 5000) }, Discount.None);
        Assert.Equal(10000, totals.SubtotalCents);
        Assert.Equal(1800, totals.TaxCents);
        Assert.Equal(11800, totals.TotalCents);
    }

    [Fact]
    public void Tax_Inclusive_Extracts_Tax()
    {
        var totals = SaleCalculator.Calculate(new[] { Line(2, 5900, inclusive: true) }, Discount.None);
        Assert.Equal(10000, totals.SubtotalCents);
        Assert.Equal(1800, totals.TaxCents);
        Assert.Equal(11800, totals.TotalCents);
    }

    [Fact]
    public void Inclusive_Price_Is_Exactly_What_The_Customer_Pays()
    {
        // A 100.00 sale price with 18% tax included must charge 100.00, not 118.00.
        var totals = SaleCalculator.Calculate(new[] { Line(1, 10000, inclusive: true) }, Discount.None);
        Assert.Equal(10000, totals.TotalCents);
        Assert.Equal(8475, totals.SubtotalCents);
        Assert.Equal(1525, totals.TaxCents);
    }

    [Fact]
    public void New_Products_Are_Tax_Inclusive_By_Default()
    {
        var product = new Product();
        Assert.True(product.TaxInclusive);
        Assert.Equal(0, product.TaxRateBasisPoints);
        Assert.True(new RixelPOS.Domain.Settings.BusinessSettings().PricesIncludeTax);
    }

    [Fact]
    public void Line_Discount_Reduces_Taxable_Base()
    {
        var line = Line(1, 10000);
        line.Discount = Discount.Percent(10);
        var totals = SaleCalculator.Calculate(new[] { line }, Discount.None);
        Assert.Equal(1000, totals.LineDiscountCents);
        Assert.Equal(9000, totals.SubtotalCents);
        Assert.Equal(1620, totals.TaxCents);
        Assert.Equal(10620, totals.TotalCents);
    }

    [Fact]
    public void Sale_Discount_Is_Allocated_Exactly()
    {
        var lines = new[] { Line(1, 3333), Line(1, 6667), Line(1, 1000) };
        var totals = SaleCalculator.Calculate(lines, Discount.Percent(10));
        Assert.Equal(1100, totals.SaleDiscountCents);
        Assert.Equal(totals.SaleDiscountCents, totals.Lines.Sum(l => l.SaleDiscountCents));
        Assert.Equal(totals.TotalCents, totals.Lines.Sum(l => l.TotalCents));
        Assert.Equal(totals.TaxCents, totals.Lines.Sum(l => l.TaxCents));
    }

    [Fact]
    public void Mixed_Tax_Rates_Are_Calculated_Per_Line()
    {
        var totals = SaleCalculator.Calculate(new[] { Line(1, 10000, 1800), Line(1, 10000, 0) }, Discount.None);
        Assert.Equal(20000, totals.SubtotalCents);
        Assert.Equal(1800, totals.TaxCents);
        Assert.Equal(21800, totals.TotalCents);
    }

    [Fact]
    public void Zero_Quantity_Is_Rejected()
    {
        Assert.Throws<BusinessRuleException>(() => SaleCalculator.Calculate(new[] { Line(0, 100) }, Discount.None));
    }

    [Fact]
    public void Allocate_Handles_Remainders()
    {
        var allocations = SaleCalculator.Allocate(100, new long[] { 1, 1, 1 });
        Assert.Equal(100, allocations.Sum());
        Assert.Equal(34, allocations.Max());
        Assert.Equal(33, allocations.Min());
    }

    [Fact]
    public void Apply_Writes_Totals_Back_To_Lines()
    {
        var sale = new Sale { Lines = { Line(1, 10000) } };
        var totals = SaleCalculator.Apply(sale);
        Assert.Equal(totals.TotalCents, sale.TotalCents);
        Assert.Equal(11800, sale.Lines[0].TotalCents);
        Assert.Equal(1800, sale.Lines[0].TaxCents);
    }
}

public class BarcodeTests
{
    [Fact]
    public void Generated_Ean13_Is_Valid()
    {
        var code = BarcodeGenerator.BuildEan13("200", 12345);
        Assert.Equal(13, code.Length);
        Assert.True(BarcodeGenerator.IsValidEan13(code));
    }

    [Fact]
    public void Invalid_Ean13_Is_Rejected()
    {
        Assert.False(BarcodeGenerator.IsValidEan13("1234567890123"));
        Assert.False(BarcodeGenerator.IsValidEan13("abc"));
    }
}

public class PermissionTests
{
    [Theory]
    [InlineData(UserRole.Admin, Permission.ManageUsers, true)]
    [InlineData(UserRole.Manager, Permission.VoidSale, true)]
    [InlineData(UserRole.Cashier, Permission.VoidSale, false)]
    [InlineData(UserRole.Cashier, Permission.Sell, true)]
    public void Roles_Have_Expected_Permissions(UserRole role, Permission permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Can(role, permission));
    }
}
