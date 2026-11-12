using RixelPOS.Desktop.ViewModels;
using RixelPOS.Desktop.Views.Dialogs;

namespace RixelPOS.Desktop.Views.Dialogs;

public partial class ShortcutsWindow : DialogWindow
{
    public ShortcutsWindow()
    {
        InitializeComponent();
        DataContext = new ShortcutsViewModel();
    }
}
