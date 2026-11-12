using Avalonia.Controls;
using RixelPOS.Desktop.Views.Dialogs;

namespace RixelPOS.Desktop.Views.Dialogs;

public partial class ValueDialogWindow : DialogWindow
{
    public ValueDialogWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("ValueBox")?.Focus();
    }
}
