namespace RixelPOS.Application.Inventory;

public static class BarcodeGenerator
{
    /// <summary>Builds a valid EAN-13 from a numeric prefix (e.g. "200") and a sequence.</summary>
    public static string BuildEan13(string prefix, long sequence)
    {
        prefix = new string(prefix.Where(char.IsDigit).ToArray());
        if (prefix.Length == 0)
        {
            prefix = "200";
        }

        var body = (sequence % 1_000_000_000L).ToString("000000000");
        var digits = (prefix + body);
        if (digits.Length < 12)
        {
            digits = digits.PadLeft(12, '0');
        }
        else if (digits.Length > 12)
        {
            digits = digits[^12..];
        }

        return digits + CheckDigit(digits);
    }

    public static int CheckDigit(string twelveDigits)
    {
        if (twelveDigits.Length != 12 || twelveDigits.Any(c => !char.IsDigit(c)))
        {
            throw new ArgumentException("EAN-13 requires exactly 12 digits.", nameof(twelveDigits));
        }

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = twelveDigits[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        return (10 - sum % 10) % 10;
    }

    public static bool IsValidEan13(string code)
    {
        if (code.Length != 13 || code.Any(c => !char.IsDigit(c)))
        {
            return false;
        }

        return CheckDigit(code[..12]) == code[12] - '0';
    }

    public static bool LooksLikeBarcode(string code) =>
        code.Length >= 6 && code.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}
