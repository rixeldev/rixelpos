using Avalonia.Controls;

namespace RixelPOS.Desktop.Views;

public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow()
    {
        InitializeComponent();
        RixelPOS.Desktop.Services.WindowSizing.ClampOnOpened(this);
    }
}
