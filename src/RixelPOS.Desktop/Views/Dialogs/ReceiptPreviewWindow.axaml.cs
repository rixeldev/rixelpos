using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RixelPOS.Desktop.ViewModels;

namespace RixelPOS.Desktop.Views.Dialogs;

public partial class ReceiptPreviewWindow : DialogWindow
{
    public ReceiptPreviewWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            if (DataContext is ReceiptPreviewViewModel vm)
            {
                vm.StartAutoPrint();
            }

            Focus();
        };
        Closed += (_, _) =>
        {
            if (DataContext is ReceiptPreviewViewModel vm)
            {
                vm.CancelAutoPrint();
            }
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ReceiptPreviewViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.PrintCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.ClosePreviewCommand.Execute(null);
            e.Handled = true;
        }
    }
}
