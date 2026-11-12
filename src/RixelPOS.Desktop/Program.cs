using Avalonia;
using RixelPOS.Infrastructure;

namespace RixelPOS.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppPaths.EnsureDirectories();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppPaths.DataDirectory, "startup-error.txt"),
                    $"{DateTime.Now:O}{Environment.NewLine}{ex}");
            }
            catch
            {
                // Last-resort diagnostics only.
            }

            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
