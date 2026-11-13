using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RixelPOS.Desktop.Services;
using RixelPOS.Desktop.ViewModels;

namespace RixelPOS.Desktop.Views.Dialogs;

public partial class LicenseGateWindow : Window
{
    public LicenseGateWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            WindowSizing.Clamp(this);
            this.FindControl<TextBox>("PinBox")?.Focus();
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            (DataContext as LicenseGateViewModel)?.UnlockCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
