using Microsoft.Extensions.DependencyInjection;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Cash;
using RixelPOS.Application.Checkout;
using RixelPOS.Application.Customers;
using RixelPOS.Application.Fiscal;
using RixelPOS.Application.Inventory;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Reports;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Application.Setup;
using RixelPOS.Application.Suppliers;
using RixelPOS.Desktop.Localization;
using RixelPOS.Desktop.Services;
using RixelPOS.Desktop.ViewModels;
using RixelPOS.Infrastructure;
using RixelPOS.Infrastructure.Backup;
using RixelPOS.Infrastructure.Data;
using RixelPOS.Infrastructure.Hardware;
using RixelPOS.Infrastructure.Logging;
using RixelPOS.Infrastructure.Printing;
using RixelPOS.Infrastructure.Security;

namespace RixelPOS.Desktop;

public static class AppServices
{
    public static IServiceProvider Build()
    {
        AppPaths.EnsureDirectories();
        var services = new ServiceCollection();

        services.AddSingleton<IAppLogger>(new FileLogger(AppPaths.LogsDirectory));

        var database = new SqliteDb(AppPaths.DatabasePath);
        database.Initialize();
        services.AddSingleton(database);
        services.AddSingleton<IPosStore, SqliteStore>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<AppSession>();

        services.AddSingleton<SettingsService>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<LockService>();
        services.AddSingleton<SetupService>();
        services.AddSingleton<CashRegisterService>();
        services.AddSingleton<InventoryService>();
        services.AddSingleton<CustomerService>();
        services.AddSingleton<SupplierService>();
        services.AddSingleton<CheckoutService>();
        services.AddSingleton<ReceiptService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<ProductCsvService>();
        services.AddSingleton<IFiscalProviderFactory, FiscalProviderFactory>();

        services.AddSingleton<EscPosPrinter>();
        services.AddSingleton<IReceiptPrinter>(sp => sp.GetRequiredService<EscPosPrinter>());
        services.AddSingleton<ILabelPrinter>(sp => sp.GetRequiredService<EscPosPrinter>());
        services.AddSingleton<IPdfService, PdfService>();
        services.AddSingleton<IDeviceDiscovery, DeviceDiscovery>();
        services.AddSingleton<IScannerListener, SerialScannerListener>();
        services.AddSingleton<ICustomerDisplay, SerialCustomerDisplay>();
        services.AddSingleton<IBackupService, SqliteBackupService>();

        services.AddSingleton(LocalizationService.Instance);
        services.AddSingleton<ThemeService>();
        services.AddSingleton<SoundService>();
        services.AddSingleton<ScanRouter>();
        services.AddSingleton<FileDialogService>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<DialogService>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CheckoutViewModel>();
        services.AddTransient<ProductsTabViewModel>();
        services.AddTransient<ReceiveTabViewModel>();
        services.AddTransient<StockCountTabViewModel>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<SuppliersViewModel>();
        services.AddTransient<CashRegisterViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MainViewModel>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<SetupWizardViewModel>();

        return services.BuildServiceProvider();
    }
}
