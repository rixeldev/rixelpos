namespace RixelPOS.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateTime LocalNow { get; }
    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime LocalNow => DateTime.Now;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}

public interface IPasswordHasher
{
    string HashPassword(string password);
    string HashPin(string pin);
    bool VerifyPassword(string password, string? hash);
    bool VerifyPin(string pin, string? hash);
}

public interface IAppLogger
{
    string LogFilePath { get; }
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
