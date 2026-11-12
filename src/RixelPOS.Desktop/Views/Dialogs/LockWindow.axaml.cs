using Avalonia.Controls;
using RixelPOS.Desktop.ViewModels;
using RixelPOS.Desktop.Views.Dialogs;

namespace RixelPOS.Desktop.Views.Dialogs;

public partial class LockWindow : DialogWindow
{
    public LockWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("PinBox")?.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing the window must not bypass the lock; only Unlock/Logout resolve it.
        if (DataContext is LockViewModel { Resolved: false })
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
