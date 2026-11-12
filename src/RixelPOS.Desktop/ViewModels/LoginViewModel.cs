using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Cash;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    private readonly SettingsService _settings;
    private readonly AppSession _session;
    private readonly CashRegisterService _cash;
    private readonly IClock _clock;
    private int _failedAttempts;

    [ObservableProperty] private string _pinEntry = "";
    [ObservableProperty] private string _pinDots = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _isPasswordMode;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _businessName = "";
    [ObservableProperty] private string _initial = "R";
    [ObservableProperty] private string _clockText = "";

    public LoginViewModel(
        AuthService auth,
        SettingsService settings,
        AppSession session,
        CashRegisterService cash,
        IClock clock)
    {
        _auth = auth;
        _settings = settings;
        _session = session;
        _cash = cash;
        _clock = clock;

        BusinessName = string.IsNullOrWhiteSpace(settings.Current.BusinessName)
            ? "RixelPOS"
            : settings.Current.BusinessName;
        Initial = string.IsNullOrWhiteSpace(BusinessName) ? "R" : BusinessName[..1].ToUpperInvariant();
        ClockText = clock.LocalNow.ToString("HH:mm");
    }

    public event EventHandler? LoginSucceeded;

    public bool CanSubmitPin => PinEntry.Length >= 4 && !IsBusy;

    public bool IsPinMode => !IsPasswordMode;

    partial void OnIsPasswordModeChanged(bool value) => OnPropertyChanged(nameof(IsPinMode));

    partial void OnPinEntryChanged(string value)
    {
        if (value.Length > 8)
        {
            PinEntry = value[..8];
            return;
        }

        PinDots = string.Join(" ", Enumerable.Repeat("●", PinEntry.Length));
        HasError = false;
        OnPropertyChanged(nameof(CanSubmitPin));
    }

    [RelayCommand]
    private void AddDigit(string? digit)
    {
        if (!string.IsNullOrEmpty(digit) && PinEntry.Length < 8)
        {
            PinEntry += digit;
        }
    }

    [RelayCommand]
    private void Backspace()
    {
        if (PinEntry.Length > 0)
        {
            PinEntry = PinEntry[..^1];
        }
    }

    [RelayCommand]
    private void ClearPin()
    {
        PinEntry = "";
        HasError = false;
    }

    [RelayCommand]
    private async Task SubmitPinAsync()
    {
        if (PinEntry.Length < 4)
        {
            HasError = true;
            ErrorMessage = Loc.Get("login.wrongPin");
            return;
        }

        IsBusy = true;
        OnPropertyChanged(nameof(CanSubmitPin));
        try
        {
            await Task.Delay(Math.Min(_failedAttempts * 250, 1500));
            var pin = PinEntry;
            var user = await Task.Run(() => _auth.AuthenticatePin(pin));
            if (user is null)
            {
                _failedAttempts++;
                HasError = true;
                ErrorMessage = Loc.Get("login.wrongPin");
                PinEntry = "";
                PinDots = "";
                return;
            }

            CompleteLogin(user);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanSubmitPin));
        }
    }

    [RelayCommand]
    private async Task SubmitPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            HasError = true;
            ErrorMessage = Loc.Get("dialogs.required");
            return;
        }

        IsBusy = true;
        OnPropertyChanged(nameof(CanSubmitPin));
        try
        {
            await Task.Delay(150);
            var user = await Task.Run(() => _auth.AuthenticatePassword(Username.Trim(), Password));
            if (user is null)
            {
                HasError = true;
                ErrorMessage = Loc.Get("login.wrongPin");
                Password = "";
                return;
            }

            CompleteLogin(user);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanSubmitPin));
        }
    }

    [RelayCommand]
    private void TogglePasswordMode()
    {
        IsPasswordMode = !IsPasswordMode;
        HasError = false;
        PinEntry = "";
        Password = "";
    }

    private void CompleteLogin(User user)
    {
        _auth.SignIn(user);
        _session.CashSession = _cash.Current;
        LoginSucceeded?.Invoke(this, EventArgs.Empty);
    }
}
