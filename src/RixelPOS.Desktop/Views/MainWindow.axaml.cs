using Avalonia.Controls;
using Avalonia.Input;
using RixelPOS.Desktop.ViewModels;

namespace RixelPOS.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        RixelPOS.Desktop.Services.WindowSizing.ClampOnOpened(this);
        Closing += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ShutdownScanner();
            }
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.L)
        {
            vm.LogoutCommand.Execute(null);
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
