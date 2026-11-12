using Avalonia.Controls;
using Avalonia.Input;
using RixelPOS.Desktop.ViewModels;

namespace RixelPOS.Desktop.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        RixelPOS.Desktop.Services.WindowSizing.ClampOnOpened(this);
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not LoginViewModel vm || vm.IsPasswordMode)
        {
            return;
        }

        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            vm.AddDigitCommand.Execute(((int)(e.Key - Key.D0)).ToString());
            e.Handled = true;
        }
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
        {
            vm.AddDigitCommand.Execute(((int)(e.Key - Key.NumPad0)).ToString());
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            vm.BackspaceCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            vm.SubmitPinCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.ClearPinCommand.Execute(null);
            e.Handled = true;
        }
    }
}
