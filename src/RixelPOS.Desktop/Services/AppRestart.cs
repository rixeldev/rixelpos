using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace RixelPOS.Desktop.Services;

public static class AppRestart
{
    public static void Restart()
    {
        try
        {
            // Let the new process acquire the single-instance mutex before this one exits.
            SingleInstance.Release();
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
            }
        }
        catch
        {
            // If the restart fails, the user can reopen the app manually.
        }

        Shutdown();
    }

    public static void Shutdown() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
