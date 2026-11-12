using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Desktop.Localization;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class LockViewModel : DialogViewModelBase
{
    private readonly AuthService _auth;
    private readonly AppSession _session;
    private readonly LocalizationService _loc;

    [ObservableProperty] private string _pin = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    public LockViewModel(AuthService auth, AppSession session, LocalizationService loc)
    {
        _auth = auth;
        _session = session;
        _loc = loc;
        UserName = session.CurrentUser?.FullName ?? "";
    }

    public string UserName { get; }

    /// <summary>True once the user unlocked or chose to sign out; blocks Alt+F4 bypasses.</summary>
    public bool Resolved { get; private set; }

    public bool LoggedOut { get; private set; }

    public bool CanUnlock => Pin.Length >= 4;

    partial void OnPinChanged(string value)
    {
        var digits = new string(value.Where(char.IsDigit).Take(8).ToArray());
        if (!string.Equals(digits, value, StringComparison.Ordinal))
        {
            Pin = digits;
            return;
        }

        OnPropertyChanged(nameof(CanUnlock));
    }

    [RelayCommand]
    private void Unlock()
    {
        HasError = false;
        ErrorMessage = "";

        if (Pin.Length < 4)
        {
            HasError = true;
            ErrorMessage = _loc.Get("lock.wrongPin");
            return;
        }

        var user = _session.CurrentUser;
        if (user?.PinHash is null)
        {
            HasError = true;
            ErrorMessage = _loc.Get("lock.noPin");
            return;
        }

        var authenticated = _auth.AuthenticatePin(Pin);
        if (authenticated is null || authenticated.Id != user.Id)
        {
            HasError = true;
            ErrorMessage = _loc.Get("lock.wrongPin");
            Pin = "";
            return;
        }

        Resolved = true;
        Close(true);
    }

    [RelayCommand]
    private void Logout()
    {
        Resolved = true;
        LoggedOut = true;
        Close(false);
    }
}
