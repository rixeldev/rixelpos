using RixelPOS.Application.Abstractions;
using RixelPOS.Domain.Common;

namespace RixelPOS.Infrastructure.Security;

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int PasswordWorkFactor = 11;
    private const int PinWorkFactor = 8;

    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new DomainException("Password cannot be empty.");
        }

        return BCrypt.Net.BCrypt.HashPassword(password, PasswordWorkFactor);
    }

    public string HashPin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            throw new DomainException("PIN cannot be empty.");
        }

        return BCrypt.Net.BCrypt.HashPassword(pin, PinWorkFactor);
    }

    public bool VerifyPassword(string password, string? hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }

    public bool VerifyPin(string pin, string? hash)
    {
        if (string.IsNullOrEmpty(pin) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(pin, hash);
        }
        catch
        {
            return false;
        }
    }
}
