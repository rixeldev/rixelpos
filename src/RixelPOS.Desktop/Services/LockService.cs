using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.ViewModels;
using RixelPOS.Desktop.Views.Dialogs;

namespace RixelPOS.Desktop.Services;

/// <summary>
/// Locks the register after a period without keyboard or pointer activity. The active
/// user must enter their PIN (or sign out) to continue.
/// </summary>
public sealed class LockService
{
    private readonly SettingsService _settings;
    private readonly AuthService _auth;
    private readonly AppSession _session;
    private readonly LocalizationService _loc;
    private readonly DispatcherTimer _timer;
    private Window? _owner;
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _attached;

    public LockService(SettingsService settings, AuthService auth, AppSession session, LocalizationService loc)
    {
        _settings = settings;
        _auth = auth;
        _session = session;
        _loc = loc;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Check();
    }

    public event EventHandler? LogoutRequested;

    public bool IsLocked { get; private set; }

    public void Attach(Window owner)
    {
        if (ReferenceEquals(_owner, owner))
        {
            return;
        }

        _attached = true;
        _owner = owner;
        owner.AddHandler(InputElement.KeyDownEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        owner.AddHandler(InputElement.PointerMovedEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        owner.AddHandler(InputElement.PointerPressedEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        owner.Closed += (_, _) =>
        {
            if (ReferenceEquals(_owner, owner))
            {
                _timer.Stop();
                _owner = null;
                _attached = false;
            }
        };
        Touch();
        _timer.Start();
    }

    public void Touch()
    {
        if (!IsLocked)
        {
            _lastActivity = DateTime.UtcNow;
        }
    }

    private void OnActivity(object? sender, RoutedEventArgs e) => Touch();

    private async void Check()
    {
        if (IsLocked || _owner is null || _session.CurrentUser is null)
        {
            return;
        }

        var minutes = _settings.Current.AutoLockMinutes;
        if (minutes <= 0)
        {
            Touch();
            return;
        }

        // A modal dialog is active work; never cover it with the lock screen.
        if (_owner.OwnedWindows.Count > 0)
        {
            Touch();
            return;
        }

        if (DateTime.UtcNow - _lastActivity >= TimeSpan.FromMinutes(minutes))
        {
            await LockAsync();
        }
    }

    public async Task LockAsync()
    {
        if (IsLocked || _owner is null || _session.CurrentUser is null)
        {
            return;
        }

        IsLocked = true;
        try
        {
            var vm = new LockViewModel(_auth, _session, _loc);
            var window = new LockWindow { DataContext = vm };
            var unlocked = await window.ShowDialog<bool>(_owner);
            if (!unlocked && vm.LoggedOut)
            {
                LogoutRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            // Never leave the app permanently blocked because of a UI failure.
        }
        finally
        {
            IsLocked = false;
            Touch();
        }
    }
}
