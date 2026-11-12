using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Styling;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Desktop.Services;

public sealed class ThemeService
{
    public void Apply(AppTheme theme)
    {
        if (Avalonia.Application.Current is null)
        {
            return;
        }

        Avalonia.Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public void ApplyUiScale(int percent)
    {
        if (Avalonia.Application.Current is null)
        {
            return;
        }

        var scale = Math.Clamp(percent, 80, 150) / 100.0;
        Avalonia.Application.Current.Resources["AppFontSize"] = 14.0 * scale;
    }

    public bool IsDark => Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public void Toggle()
    {
        if (Avalonia.Application.Current is null)
        {
            return;
        }

        Avalonia.Application.Current.RequestedThemeVariant = Avalonia.Application.Current.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }
}

public enum ScanSound
{
    Found,
    NewProduct,
    Error,
    Success,
    Held
}

/// <summary>Cross-platform feedback sounds with a silent fallback when no player is available.</summary>
public sealed class SoundService
{
    private static readonly string[] LinuxPlayers = { "paplay", "aplay", "pw-play" };

    public void Play(ScanSound sound)
    {
        var file = sound switch
        {
            ScanSound.Found => "scan-ok.wav",
            ScanSound.NewProduct => "scan-new.wav",
            ScanSound.Error => "scan-error.wav",
            ScanSound.Success => "success.wav",
            _ => "scan-ok.wav"
        };

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", file);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                NativeMethods.PlaySound(path, IntPtr.Zero, 0x00020000 | 0x00000001); // ASYNC | FILENAME
                return;
            }

            foreach (var player in LinuxPlayers)
            {
                if (TryStart(player, path))
                {
                    return;
                }
            }
        }
        catch
        {
            // Audio is optional. Never interrupt a sale because a sound failed.
        }
    }

    private static bool TryStart(string command, string path)
    {
        try
        {
            var info = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            info.ArgumentList.Add(path);
            using var process = Process.Start(info);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool PlaySound(string sound, IntPtr module, uint flags);
    }
}

/// <summary>Routes scans from serial listeners or hardware to the active screen.</summary>
public sealed class ScanRouter
{
    public event EventHandler<string>? Scanned;

    public void Publish(string code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            Scanned?.Invoke(this, code.Trim());
        }
    }
}
