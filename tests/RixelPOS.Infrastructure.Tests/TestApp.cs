using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Cash;
using RixelPOS.Application.Checkout;
using RixelPOS.Application.Customers;
using RixelPOS.Application.Fiscal;
using RixelPOS.Application.Inventory;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Reports;
using RixelPOS.Application.Sales;
using RixelPOS.Application.Services;
using RixelPOS.Application.Sessions;
using RixelPOS.Application.Setup;
using RixelPOS.Application.Suppliers;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Settings;
using RixelPOS.Infrastructure.Backup;
using RixelPOS.Infrastructure.Data;
using RixelPOS.Infrastructure.Printing;
using RixelPOS.Infrastructure.Security;

namespace RixelPOS.Infrastructure.Tests;

public sealed class TestClock : IClock
{
    public TestClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; set; }
    public DateTime LocalNow => UtcNow.ToLocalTime();
    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    public void Advance(TimeSpan span) => UtcNow = UtcNow.Add(span);
}

public sealed class NullLogger : IAppLogger
{
    public string LogFilePath => "";
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

public sealed class FakeReceiptPrinter : IReceiptPrinter
{
    public List<ReceiptData> Printed { get; } = new();
    public bool IsConfigured { get; set; } = true;
    public string Description => "fake";

    public Task<PrintResult> PrintAsync(ReceiptData data, CancellationToken cancellationToken = default)
    {
        Printed.Add(data);
        return Task.FromResult(PrintResult.Ok());
    }

    public Task<PrintResult> PrintTestAsync(CancellationToken cancellationToken = default) => Task.FromResult(PrintResult.Ok());

    public Task<PrintResult> OpenCashDrawerAsync(CancellationToken cancellationToken = default) => Task.FromResult(PrintResult.Ok());
}

public sealed class TestApp : IDisposable
{
    public TestApp()
    {
        Directory = Path.Combine(Path.GetTempPath(), "rixelpos-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        Db = new SqliteDb(Path.Combine(Directory, "rixelpos-test.db"));
        Db.Initialize();
        Logger = new NullLogger();
        Clock = new TestClock(new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Utc));
        Store = new SqliteStore(Db);
        Session = new AppSession();
        Settings = new SettingsService(Store, Logger);
        Hasher = new BcryptPasswordHasher();
        Audit = new AuditService(Store, Clock);
        Auth = new AuthService(Store, Hasher, Session, Audit, Logger);
        Setup = new SetupService(Store, Hasher, Settings, Clock);
        Setup.CompleteSetup(new SetupRequest(
            "Minimarket Rixel", "Minimarket Rixel SRL", "1-01-00000-1", "Av. Principal 12", "809-000-0000",
            "DOP", "RD$", "ITBIS", 1800, true, "A", ReceiptWidth.Mm80,
            "admin", "Administrador", "Rixel1234", "1234", AppLanguage.Spanish, AppTheme.Dark, ""));
        var admin = Store.FindUserByUsername("admin")!;
        Session.SignIn(admin);

        Cash = new CashRegisterService(Store, Session, Audit, Clock);
        Inventory = new InventoryService(Store, Settings, Session, Audit, Clock);
        Customers = new CustomerService(Store, Session, Audit, Clock, Settings);
        Checkout = new CheckoutService(Store, Settings, Session, Clock, new FiscalProviderFactory(), Audit, Logger);
        ReceiptPrinter = new FakeReceiptPrinter();
        Receipt = new ReceiptService(Store, Settings, ReceiptPrinter, new PdfService(), Clock);
        Reports = new ReportService(Store, new PdfService(), Settings, Clock);
        Suppliers = new SupplierService(Store, Session, Audit, Clock);
        Csv = new ProductCsvService(Store, Audit, Clock);
        Backups = new SqliteBackupService(Db, Settings, Logger);
    }

    public string Directory { get; }
    public SqliteDb Db { get; }
    public SqliteStore Store { get; }
    public IAppLogger Logger { get; }
    public TestClock Clock { get; }
    public AppSession Session { get; }
    public SettingsService Settings { get; }
    public IPasswordHasher Hasher { get; }
    public AuditService Audit { get; }
    public AuthService Auth { get; }
    public SetupService Setup { get; }
    public CashRegisterService Cash { get; }
    public InventoryService Inventory { get; }
    public CustomerService Customers { get; }
    public CheckoutService Checkout { get; }
    public ReceiptService Receipt { get; }
    public FakeReceiptPrinter ReceiptPrinter { get; }
    public ReportService Reports { get; }
    public SupplierService Suppliers { get; }
    public ProductCsvService Csv { get; }
    public SqliteBackupService Backups { get; }

    public void OpenRegister(long openingCents = 0) => Cash.Open(openingCents);

    public Product CreateProduct(string name, long priceCents, decimal stock = 10, int taxBp = 1800,
        long costCents = 0, string? barcode = null, bool taxInclusive = false)
    {
        var product = new Product
        {
            Sku = "SKU-" + Guid.NewGuid().ToString("N")[..8],
            Name = name,
            PriceCents = priceCents,
            CostCents = costCents,
            TaxRateBasisPoints = taxBp,
            TaxInclusive = taxInclusive,
            Stock = stock,
            MinStock = 2,
            Unit = "unit"
        };
        if (!string.IsNullOrWhiteSpace(barcode))
        {
            product.Barcodes.Add(new ProductBarcode { ProductId = product.Id, Code = barcode, IsPrimary = true });
        }

        Inventory.SaveProduct(product, isNew: true);
        return Store.GetProduct(product.Id)!;
    }

    public Sale Sell(Cart cart, params Payment[] payments)
    {
        if (payments.Length == 0)
        {
            var total = cart.Totals.TotalCents;
            payments = new[] { new Payment { Method = PaymentMethod.Cash, AmountCents = total, TenderedCents = total } };
        }

        return Checkout.CompleteSale(cart, payments, cart.Sale.CustomerId is null ? null : Store.GetCustomer(cart.Sale.CustomerId.Value), null);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
