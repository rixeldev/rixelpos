using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace RixelPOS.Desktop.Services;

/// <summary>Small wrapper so view models can copy text without knowing about windows.</summary>
public sealed class ClipboardService
{
    public async Task<bool> SetTextAsync(string text)
    {
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard is null)
            {
                return false;
            }

            await clipboard.SetTextAsync(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
