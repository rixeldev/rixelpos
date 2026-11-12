using RixelPOS.Domain.Common;

namespace RixelPOS.Infrastructure;

/// <summary>OS-specific application folders. Override with RIXELPOS_DATA_DIR (portable/USB installs).</summary>
public static class AppPaths
{
    private static readonly Lazy<string> DataDir = new(ResolveDataDirectory);

    public static string DataDirectory => DataDir.Value;

    public static string DatabasePath => Path.Combine(DataDirectory, "rixelpos.db");

    public static string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string PdfDirectory => Path.Combine(DataDirectory, "documents");

    public static string ConfigPath => Path.Combine(DataDirectory, "appsettings.json");

    public static string DocumentsDirectory
    {
        get
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(docs) ? DataDirectory : Path.Combine(docs, Branding.ProductName);
        }
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(PdfDirectory);
    }

    private static string ResolveDataDirectory()
    {
        var custom = Environment.GetEnvironmentVariable("RIXELPOS_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return Path.GetFullPath(custom);
        }

        var baseDir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDir, Branding.ProductCode);
    }
}
