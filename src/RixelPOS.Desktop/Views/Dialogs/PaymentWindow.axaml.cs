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

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.AddPaymentCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 && e.KeyModifiers == KeyModifiers.None)
        {
            var method = e.Key switch
            {
                Key.F1 => "Cash",
                Key.F2 => "Card",
                Key.F3 => "Transfer",
                Key.F4 => "StoreCredit",
                _ => "Credit"
            };
            vm.SelectMethodCommand.Execute(method);
            e.Handled = true;
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
