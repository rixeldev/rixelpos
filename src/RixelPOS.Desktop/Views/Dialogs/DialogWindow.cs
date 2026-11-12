using Avalonia.Controls;
using RixelPOS.Desktop.Services;
using RixelPOS.Desktop.ViewModels;

namespace RixelPOS.Desktop.Views.Dialogs;

/// <summary>Base window that closes itself when its dialog view-model raises CloseRequested.</summary>
public class DialogWindow : Window
{
    private DialogViewModelBase? _viewModel;

    public DialogWindow()
    {
        DataContextChanged += (_, _) => AttachViewModel();
        Opened += (_, _) => AttachViewModel();
        WindowSizing.ClampOnOpened(this);
    }

    private void AttachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = DataContext as DialogViewModelBase;
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, bool result)
    {
        try
        {
            Close(result);
        }
        catch
        {
            // window may already be closing
        }
    }
}
