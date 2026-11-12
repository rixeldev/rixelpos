using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace RixelPOS.Desktop.Services;

public sealed class FileDialogService
{
    private static Window? Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> PickOpenAsync(string title, string description, params string[] extensions)
    {
        if (Owner is null)
        {
            return null;
        }

        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(description)
                {
                    Patterns = extensions.Select(e => "*." + e.TrimStart('.')).ToArray()
                }
            }
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveAsync(string title, string suggestedName, string description, params string[] extensions)
    {
        if (Owner is null)
        {
            return null;
        }

        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extensions.FirstOrDefault()?.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(description)
                {
                    Patterns = extensions.Select(e => "*." + e.TrimStart('.')).ToArray()
                }
            }
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (Owner is null)
        {
            return null;
        }

        var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
