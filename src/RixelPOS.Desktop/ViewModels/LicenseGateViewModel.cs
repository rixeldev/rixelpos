using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RixelPOS.Desktop.Localization;
using RixelPOS.Infrastructure.Licensing;

namespace RixelPOS.Desktop.ViewModels;

public sealed partial class LicenseGateViewModel : ViewModelBase
{
    private readonly LicenseService _license;
    private readonly LocalizationService _loc;
    private int _failedAttempts;

    [ObservableProperty] private string _pin = "";
    [ObservableProperty] private bool _isLocked;

    public LicenseGateViewModel(LicenseService license)
    {
        _license = license;
        _loc = LocalizationService.Instance;
    }

    public event EventHandler? Activated;

    public bool CanSubmit => !IsLocked && Pin.Length == VendorLicense.PinLength;

    partial void OnPinChanged(string value)
    {
        var digits = new string(value.Where(char.IsDigit).Take(VendorLicense.PinLength).ToArray());
        if (!string.Equals(digits, value, StringComparison.Ordinal))
        {
            Pin = digits;
            return;
        }

        OnPropertyChanged(nameof(CanSubmit));
    }

    partial void OnIsLockedChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));

    [RelayCommand]
    private void Unlock()
    {
        ErrorMessage = null;
        if (IsLocked || Pin.Length != VendorLicense.PinLength)
        {
            return;
        }

        if (_license.TryActivate(Pin))
        {
            Activated?.Invoke(this, EventArgs.Empty);
            return;
        }

        _failedAttempts++;
        Pin = "";
        if (_failedAttempts >= 5)
        {
            IsLocked = true;
            ErrorMessage = _loc.Get("license.tooManyAttempts");
        }
        else
        {
            ErrorMessage = _loc.Get("license.wrongPin");
        }
    }
}
