using System.Security.Cryptography;

namespace RixelPOS.Infrastructure.Licensing;

/// <summary>
/// Vendor activation PIN. Only a PBKDF2 hash is stored here; the PIN never appears in
/// plain text in the source or the binary. The PIN is intentionally fixed and there is
/// no UI or setting to change or remove it.
/// </summary>
public static class VendorLicense
{
    public const int PinLength = 6;

    private const int Iterations = 210_000;
    private static readonly byte[] Salt = Convert.FromBase64String("bzyaHXsuT4Clxtnh8gOEdQ==");
    private static readonly byte[] ExpectedHash = Convert.FromBase64String("qcyJ2KSAMrF3yd4jsSXYxDCCreqeiDMmSNkt6hBCZzA=");

    public static bool VerifyPin(string? pin) => VerifyPin(pin, Salt, ExpectedHash);

    /// <summary>Overload used by tests to validate the algorithm without the vendor PIN.</summary>
    public static bool VerifyPin(string? pin, byte[] salt, byte[] expectedHash)
    {
        if (string.IsNullOrWhiteSpace(pin) || salt.Length == 0 || expectedHash.Length == 0)
        {
            return false;
        }

        pin = pin.Trim();
        if (pin.Length != PinLength || !pin.All(char.IsDigit))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
    }
}
