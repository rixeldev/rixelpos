using CommunityToolkit.Mvvm.ComponentModel;
using RixelPOS.Desktop.Localization;
using RixelPOS.Domain.Common;
using RixelPOS.Infrastructure.Updates;

namespace RixelPOS.Desktop.ViewModels;

public sealed record UpdateOutcome(
    bool UpdateFound,
    bool Updated,
    bool Restart,
    bool ExitApplication,
    string Message);

public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updates;
    private readonly LocalizationService _loc;
    private readonly bool _manual;

    [ObservableProperty] private string _message = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isIndeterminate = true;
    [ObservableProperty] private bool _hasProgress = true;

    public UpdateViewModel(UpdateService updates, bool manual)
    {
        _updates = updates;
        _loc = LocalizationService.Instance;
        _manual = manual;
        Message = _loc.Get("update.checking");
    }

    public event EventHandler<UpdateOutcome>? Completed;

    public async Task RunAsync()
    {
        var info = await _updates.CheckAsync();
        if (info is null)
        {
            Message = _loc.Format("update.upToDate", Branding.Version);
            await Task.Delay(_manual ? 1200 : 700);
            Completed?.Invoke(this, new UpdateOutcome(false, false, false, false, Message));
            return;
        }

        Message = _loc.Format("update.downloading", info.Version);
        IsIndeterminate = false;
        Progress = 0;

        var progress = new Progress<double>(value =>
        {
            Progress = Math.Clamp(value * 100, 0, 100);
        });

        var file = await _updates.DownloadAsync(info, progress);
        if (file is null)
        {
            Message = _loc.Get("update.downloadFailed");
            await Task.Delay(2500);
            Completed?.Invoke(this, new UpdateOutcome(true, false, false, false, Message));
            return;
        }

        Message = _loc.Format("update.installing", info.Version);
        IsIndeterminate = true;
        var result = await _updates.InstallAsync(info, file);
        if (!result.Updated)
        {
            Message = result.Message;
            await Task.Delay(2500);
        }

        Completed?.Invoke(this, new UpdateOutcome(true, result.Updated, result.Restart, result.ExitApplication, result.Message));
    }
}
