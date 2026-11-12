using Avalonia.Controls;
using Avalonia.Input;
using RixelPOS.Desktop.ViewModels;
using RixelPOS.Desktop.Views.Dialogs;

namespace RixelPOS.Desktop.Views.Dialogs;

public partial class SaleCompletedWindow : DialogWindow
{
    public SaleCompletedWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SaleCompletedViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Escape)
        {
            vm.DoneCommand.Execute(null);
            e.Handled = true;
        }
    }
}
