using RixelPOS.Application.Abstractions;

namespace RixelPOS.Infrastructure.Logging;

/// <summary>Small structured JSON-lines logger with daily files and size-based rotation.</summary>
public sealed class FileLogger : IAppLogger
{
    private const long MaxSizeBytes = 5 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly string _directory;

    public FileLogger(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public string LogFilePath => Path.Combine(_directory, $"{DateTime.Now:yyyy-MM-dd}.log");

    public void Debug(string message) => Write("DEBUG", message, null);
    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = System.Text.Json.JsonSerializer.Serialize(new
            {
                ts = DateTime.UtcNow.ToString("O"),
                level,
                message,
                exception = exception?.ToString()
            });

            lock (_gate)
            {
                var path = LogFilePath;
                RotateIfNeeded(path);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never break a sale.
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxSizeBytes)
        {
            return;
        }

        var rotated = path + ".1";
        if (File.Exists(rotated))
        {
            File.Delete(rotated);
        }

        File.Move(path, rotated);
    }
}
