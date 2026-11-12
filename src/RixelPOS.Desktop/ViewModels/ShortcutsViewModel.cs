using CommunityToolkit.Mvvm.Input;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class ShortcutsViewModel : DialogViewModelBase
{
    [RelayCommand]
    private void CloseDialog() => Close(true);
}
