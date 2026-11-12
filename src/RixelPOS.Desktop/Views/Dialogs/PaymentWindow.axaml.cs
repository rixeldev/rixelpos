using Avalonia.Controls;
using Avalonia.Input;
using RixelPOS.Desktop.ViewModels;
using RixelPOS.Desktop.Views.Dialogs;

namespace RixelPOS.Desktop.Views.Dialogs;

public partial class PaymentWindow : DialogWindow
{
    public PaymentWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += (_, _) => this.FindControl<TextBox>("AmountBox")?.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not PaymentViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (vm.CanConfirm)
            {
                vm.ConfirmCommand.Execute(null);
            }
            else
            {
                vm.AddPaymentCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
