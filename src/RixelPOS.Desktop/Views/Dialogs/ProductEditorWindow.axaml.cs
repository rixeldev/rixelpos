using Avalonia.Controls;
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
}
