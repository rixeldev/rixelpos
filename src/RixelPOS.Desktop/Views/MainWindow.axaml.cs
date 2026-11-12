using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using RixelPOS.Desktop.Services;
using RixelPOS.Desktop.ViewModels;

namespace RixelPOS.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly LockService? _lockService;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _lockService = App.Services?.GetService<LockService>();
        if (_lockService is not null)
        {
            _lockService.LogoutRequested += (_, _) =>
                (DataContext as MainViewModel)?.LogoutCommand.Execute(null);
            _lockService.Attach(this);
        }
        RixelPOS.Desktop.Services.WindowSizing.ClampOnOpened(this);
        Closing += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ShutdownScanner();
            }
        };
    }

    private void OnShortcutsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ShowShortcuts();

    private void ShowShortcuts()
    {
        var window = new RixelPOS.Desktop.Views.Dialogs.ShortcutsWindow();
        _ = window.ShowDialog(this);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.L)
        {
            _ = _lockService?.LockAsync();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            vm.FocusCheckoutSearch();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.L)
        {
            vm.LogoutCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F9)
        {
            _ = vm.ChargeAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12)
        {
            ShowShortcuts();
            e.Handled = true;
            return;
        }

        var key = e.Key switch
        {
            Key.F1 => "dashboard",
            Key.F2 => "checkout",
            Key.F3 => "inventory",
            Key.F4 => "customers",
            Key.F5 => "cash",
            Key.F6 => "reports",
            Key.F7 => "settings",
            Key.F8 => "suppliers",
            _ => null
        };

        if (key is not null)
        {
            vm.Navigate(key);
            e.Handled = true;
        }
    }
}
