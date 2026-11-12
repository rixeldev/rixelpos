using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RixelPOS.Desktop.ViewModels;
using RixelPOS.Desktop.Views.Dialogs;

namespace RixelPOS.Desktop.Views.Dialogs;

public partial class ProductEditorWindow : DialogWindow
{
    public ProductEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            var box = this.FindControl<TextBox>("NameBox");
            box?.Focus();
        };
    }

    /// <summary>Enter in the barcode field registers the code without clicking Add.</summary>
    private void OnBarcodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ProductEditorViewModel vm)
        {
            return;
        }

        vm.AddBarcodeFromFieldCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Leaving the barcode field also registers it, so a scan or a typed code is never
    /// silently lost when the user goes straight to Save.
    /// </summary>
    private void OnBarcodeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProductEditorViewModel vm)
        {
            vm.AddBarcodeFromFieldCommand.Execute(null);
        }
    }
}
