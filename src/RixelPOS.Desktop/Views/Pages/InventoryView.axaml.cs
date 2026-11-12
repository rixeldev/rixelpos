using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using RixelPOS.Desktop.ViewModels;

namespace RixelPOS.Desktop.Views.Pages;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        FocusActiveScanner();
    }

    private void OnReceiveKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is InventoryViewModel { Receive: var receive } && e.Key == Key.Enter)
        {
            receive.CommitScanCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCountKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is InventoryViewModel { Count: var count } && e.Key == Key.Enter)
        {
            count.CommitScanCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void FocusActiveScanner()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not InventoryViewModel vm)
            {
                return;
            }

            var name = vm.SelectedTabIndex switch
            {
                1 => "ReceiveScanBox",
                2 => "CountScanBox",
                _ => null
            };
            if (name is not null)
            {
                this.FindControl<TextBox>(name)?.Focus();
            }
        }, DispatcherPriority.Background);
    }
}
