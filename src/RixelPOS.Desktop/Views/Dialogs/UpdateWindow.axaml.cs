using Avalonia.Controls;
using RixelPOS.Desktop.Services;
using RixelPOS.Desktop.ViewModels;

namespace RixelPOS.Desktop.Views.Dialogs;

public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            WindowSizing.Clamp(this);
            if (DataContext is UpdateViewModel vm)
            {
                _ = vm.RunAsync();
            }
        };
    }
}
