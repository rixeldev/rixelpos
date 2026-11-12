using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Services;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Settings;

namespace RixelPOS.Application.Setup;

public sealed record SetupRequest(
    string BusinessName,
    string LegalName,
    string TaxId,
    string Address,
    string Phone,
    string CurrencyCode,
    string CurrencySymbol,
    string TaxName,
    int TaxBasisPoints,
    bool PricesIncludeTax,
    string InvoicePrefix,
    ReceiptWidth ReceiptWidth,
    string AdminUsername,
    string AdminFullName,
    string AdminPassword,
    string AdminPin,
    AppLanguage Language,
    AppTheme Theme,
    string PrinterConnection);

public sealed class SetupService
{
    private readonly IPosStore _store;
    private readonly IPasswordHasher _hasher;
    private readonly SettingsService _settings;
    private readonly IClock _clock;

    public SetupService(IPosStore store, IPasswordHasher hasher, SettingsService settings, IClock clock)
    {
        _store = store;
        _hasher = hasher;
        _settings = settings;
        _clock = clock;
    }

    public BusinessSettings CompleteSetup(SetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessName))
        {
            throw new BusinessRuleException("setup.business_required", "Business name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AdminUsername) || request.AdminUsername.Trim().Length < 3)
        {
            throw new BusinessRuleException("setup.username_required", "The admin username needs at least 3 characters.");
        }

        if (request.AdminPassword.Length < 8)
        {
            throw new BusinessRuleException("user.password_short", "The password must have at least 8 characters.");
        }

        if (request.AdminPin.Length is < 4 or > 8 || !request.AdminPin.All(char.IsDigit))
        {
            throw new BusinessRuleException("user.invalid_pin", "The PIN must contain 4 to 8 digits.");
        }

        var settings = _settings.Current;
        settings.BusinessName = request.BusinessName.Trim();
        settings.LegalName = request.LegalName.Trim();
        settings.TaxId = request.TaxId.Trim();
        settings.Address = request.Address.Trim();
        settings.Phone = request.Phone.Trim();
        settings.CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? Branding.DefaultCurrencyCode : request.CurrencyCode.Trim().ToUpperInvariant();
        settings.CurrencySymbol = string.IsNullOrWhiteSpace(request.CurrencySymbol) ? Branding.DefaultCurrencySymbol : request.CurrencySymbol.Trim();
        settings.TaxName = string.IsNullOrWhiteSpace(request.TaxName) ? "ITBIS" : request.TaxName.Trim();
        settings.DefaultTaxRateBasisPoints = Math.Clamp(request.TaxBasisPoints, 0, 10_000);
        settings.PricesIncludeTax = true;
        settings.InvoicePrefix = string.IsNullOrWhiteSpace(request.InvoicePrefix) ? "A" : request.InvoicePrefix.Trim();
        settings.ReceiptWidth = request.ReceiptWidth;
        settings.Language = request.Language;
        settings.Theme = request.Theme;
        settings.PrinterConnection = request.PrinterConnection?.Trim() ?? "";
        settings.Configured = true;

        _store.ExecuteInTransaction(store =>
        {
            if (store.GetUsers().Count > 0)
            {
                throw new BusinessRuleException("setup.already_done", "This business is already configured.");
            }

            var admin = new User
            {
                Username = request.AdminUsername.Trim().ToLowerInvariant(),
                FullName = request.AdminFullName.Trim(),
                Role = UserRole.Admin,
                PasswordHash = _hasher.HashPassword(request.AdminPassword),
                PinHash = _hasher.HashPin(request.AdminPin),
                IsActive = true,
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow
            };
            store.SaveUser(admin);
            store.SaveSettings(settings);

            if (store.GetCategories().Count == 0)
            {
                foreach (var name in new[] { "General", "Alimentos", "Bebidas", "Limpieza" })
                {
                    store.SaveCategory(new Category { Name = name });
                }
            }

            if (store.GetTaxRates().Count == 0)
            {
                store.SaveTaxRate(new TaxRate
                {
                    Name = settings.TaxName,
                    RateBasisPoints = settings.DefaultTaxRateBasisPoints,
                    IsInclusive = settings.PricesIncludeTax,
                    IsDefault = true
                });
                store.SaveTaxRate(new TaxRate { Name = "Exento", RateBasisPoints = 0, IsDefault = false });
            }
        });

        _settings.Reload();
        return _settings.Current;
    }
}
