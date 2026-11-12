namespace RixelPOS.Domain.Common;

/// <summary>Formats sequential invoice numbers, e.g. prefix "A" + 123 + padding 6 => "A-000123".</summary>
public static class InvoiceNumberFormatter
{
    public static string Format(string? prefix, long number, int padding = Defaults.InvoicePadding)
    {
        padding = Math.Clamp(padding, 1, 12);
        var digits = number.ToString(new string('0', padding));
        return string.IsNullOrWhiteSpace(prefix) ? digits : $"{prefix.Trim()}-{digits}";
    }
}
