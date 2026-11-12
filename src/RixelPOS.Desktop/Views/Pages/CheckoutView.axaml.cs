using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using RixelPOS.Desktop.ViewModels;

namespace RixelPOS.Desktop.Views.Pages;

public partial class CheckoutView : UserControl
{
    private CheckoutViewModel? _viewModel;

    public CheckoutView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FocusSearch();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.FocusSearchRequested -= OnFocusSearchRequested;
        }

        _viewModel = DataContext as CheckoutViewModel;
        if (_viewModel is not null)
        {
            _viewModel.FocusSearchRequested += OnFocusSearchRequested;
        }
    }

    private void OnFocusSearchRequested(object? sender, EventArgs e) => FocusSearch();

    private void FocusSearch()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var box = this.FindControl<TextBox>("SearchBox");
            box?.Focus();
            box?.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CheckoutViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (string.IsNullOrWhiteSpace(vm.SearchText))
            {
                vm.ChargeCommand.Execute(null);
            }
            else
            {
                vm.CommitSearchCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CloseSearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
