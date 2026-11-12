using RixelPOS.Application.Abstractions;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Settings;

namespace RixelPOS.Application.Services;

public sealed class SettingsService
{
    private readonly IPosStore _store;
    private readonly IAppLogger _logger;
    private BusinessSettings _current;

    public SettingsService(IPosStore store, IAppLogger logger)
    {
        _store = store;
        _logger = logger;
        _current = store.LoadSettings();
    }

    public BusinessSettings Current => _current;

    public event EventHandler? Changed;

    public void Reload()
    {
        _current = _store.LoadSettings();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save(BusinessSettings settings)
    {
        Validate(settings);
        _store.SaveSettings(settings);
        _current = _store.LoadSettings();
        _logger.Info("Settings saved");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string FormatMoney(long cents) => $"{_current.CurrencySymbol} {cents / 100m:N2}";

    public string FormatMoney(Money money) => FormatMoney(money.Cents);

    public string FormatQuantity(decimal quantity) =>
        quantity == Math.Truncate(quantity) ? quantity.ToString("0") : quantity.ToString("0.###");

    public string FormatPercent(int basisPoints) => $"{basisPoints / 100m:0.##}%";

    private static void Validate(BusinessSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.CurrencySymbol))
        {
            throw new BusinessRuleException("settings.currency_required", "Currency symbol is required.");
        }

        if (s.DefaultTaxRateBasisPoints is < 0 or > 10_000)
        {
            throw new BusinessRuleException("settings.tax_range", "Default tax rate must be between 0% and 100%.");
        }

        if (s.InvoicePadding is < 1 or > 12)
        {
            throw new BusinessRuleException("settings.invoice_padding", "Invoice padding must be between 1 and 12.");
        }

        if (s.DiscountApprovalPercent is < 0 or > 100)
        {
            throw new BusinessRuleException("settings.discount_range", "Discount approval limit must be between 0 and 100.");
        }

        if (s.ScannerDebounceMs is < 0 or > 5000)
        {
            throw new BusinessRuleException("settings.debounce_range", "Scanner debounce must be between 0 and 5000 ms.");
        }

        if (s.BackupKeepCount < 1)
        {
            throw new BusinessRuleException("settings.backup_keep", "Keep at least one backup.");
        }
    }
}
