using RixelPOS.Application.Inventory;
using RixelPOS.Application.Sales;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Infrastructure.Hardware;

namespace RixelPOS.Infrastructure.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public void Initialize_Is_Idempotent_And_Creates_Schema()
    {
        using var app = new TestApp();
        app.Db.Initialize();
        Assert.True(File.Exists(app.Db.DatabasePath));
        Assert.NotNull(app.Store.GetProducts());
        Assert.NotNull(app.Store.LoadSettings());
    }

    [Fact]
    public void Products_RoundTrip_With_Barcodes()
    {
        using var app = new TestApp();
        var product = app.CreateProduct("Café molido", 22500, 12, 1800, 15000, "746000000001");

        var loaded = app.Store.GetProduct(product.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Café molido", loaded!.Name);
        Assert.Equal(22500, loaded.PriceCents);
        Assert.Equal(12, loaded.Stock);
        Assert.Single(loaded.Barcodes);
        Assert.Equal("746000000001", loaded.PrimaryBarcode);
        Assert.Equal(product.Id, app.Store.FindProductByCode("746000000001")!.Id);
        Assert.Single(app.Store.SearchProducts("café"));
    }

    [Fact]
    public void Default_Settings_Use_Tax_Inclusive_Prices()
    {
        using var app = new TestApp();
        Assert.True(app.Settings.Current.PricesIncludeTax);
        Assert.True(app.Store.LoadSettings().PricesIncludeTax);
    }

    [Fact]
    public void Migration_Removes_The_Implicit_Tax_Rate()
    {
        using var app = new TestApp();
        var product = app.CreateProduct("Producto con ITBIS", 11800, 1, taxBp: 1800);

        using (var connection = app.Db.Open())
        {
            using (var updateProduct = connection.CreateCommand())
            {
                updateProduct.CommandText = "UPDATE products SET tax_rate_bp = 1800 WHERE id = @id";
                updateProduct.Parameters.AddWithValue("@id", product.Id.ToString());
                updateProduct.ExecuteNonQuery();
            }

            using (var updateSetting = connection.CreateCommand())
            {
                updateSetting.CommandText = "UPDATE settings SET value = '1800' WHERE key = 'tax.default_bp'";
                updateSetting.ExecuteNonQuery();
            }

            using (var forgetMigration = connection.CreateCommand())
            {
                forgetMigration.CommandText = "DELETE FROM schema_migrations WHERE version = 3";
                forgetMigration.ExecuteNonQuery();
            }
        }

        app.Db.Initialize();

        var reloaded = app.Store.GetProduct(product.Id)!;
        Assert.Equal(0, reloaded.TaxRateBasisPoints);
        Assert.True(reloaded.TaxInclusive);
        Assert.Equal(0, app.Store.LoadSettings().DefaultTaxRateBasisPoints);
    }

    [Fact]
    public void Migration_Makes_Existing_Products_Tax_Inclusive()
    {
        using var app = new TestApp();
        var product = app.CreateProduct("Producto anterior", 1000, 1);

        using (var connection = app.Db.Open())
        {
            using (var updateProduct = connection.CreateCommand())
            {
                updateProduct.CommandText = "UPDATE products SET tax_inclusive = 0 WHERE id = @id";
                updateProduct.Parameters.AddWithValue("@id", product.Id.ToString());
                updateProduct.ExecuteNonQuery();
            }

            using (var updateSetting = connection.CreateCommand())
            {
                updateSetting.CommandText = "UPDATE settings SET value = 'False' WHERE key = 'tax.inclusive'";
                updateSetting.ExecuteNonQuery();
            }

            using (var forgetMigration = connection.CreateCommand())
            {
                forgetMigration.CommandText = "DELETE FROM schema_migrations WHERE version = 2";
                forgetMigration.ExecuteNonQuery();
            }
        }

        app.Db.Initialize();

        Assert.True(app.Store.GetProduct(product.Id)!.TaxInclusive);
        Assert.True(app.Store.LoadSettings().PricesIncludeTax);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        using var app = new TestApp();
        var settings = app.Settings.Current.Clone();
        settings.BusinessName = "Mi Negocio";
        settings.CurrencySymbol = "US$";
        settings.ReceiptWidth = ReceiptWidth.Mm58;
        settings.Language = AppLanguage.English;
        settings.DiscountApprovalPercent = 35;
        app.Settings.Save(settings);

        var reloaded = app.Store.LoadSettings();
        Assert.Equal("Mi Negocio", reloaded.BusinessName);
        Assert.Equal("US$", reloaded.CurrencySymbol);
        Assert.Equal(ReceiptWidth.Mm58, reloaded.ReceiptWidth);
        Assert.Equal(AppLanguage.English, reloaded.Language);
        Assert.Equal(35, reloaded.DiscountApprovalPercent);
    }
}

public sealed class CheckoutTests
{
    [Fact]
    public void Sale_Computes_Totals_Deducts_Stock_And_Numbers_Invoices()
    {
        using var app = new TestApp();
        app.OpenRegister(50000);
        var product = app.CreateProduct("Arroz 1 lb", 10000, 10);

        var cart = new Cart();
        cart.AddProduct(product, 2);
        Assert.Equal(23600, cart.Totals.TotalCents);

        var sale = app.Sell(cart, new Payment
        {
            Method = PaymentMethod.Cash,
            AmountCents = 23600,
            TenderedCents = 25000
        });

        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(1, sale.Number);
        Assert.Equal("A-000001", sale.Invoice!.Number);
        Assert.Equal(23600, sale.TotalCents);
        Assert.Equal(1400, sale.ChangeCents);
        Assert.Equal(8, app.Store.GetProduct(product.Id)!.Stock);

        var loaded = app.Store.GetSaleByInvoiceNumber("A-000001");
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Lines);
        Assert.Single(loaded.Payments);
        Assert.Equal(23600, loaded.TotalCents);

        var secondCart = new Cart();
        secondCart.AddProduct(app.Store.GetProduct(product.Id)!, 1);
        var second = app.Sell(secondCart);
        Assert.Equal("A-000002", second.Invoice!.Number);
        Assert.Equal(7, app.Store.GetProduct(product.Id)!.Stock);
    }

    [Fact]
    public void Fresh_Product_Without_Tax_Charges_Exactly_Its_Price_And_Returns_Change()
    {
        using var app = new TestApp();
        app.OpenRegister(100000);
        var product = app.CreateProduct("Precio plano", 20000, 5, taxBp: 0, taxInclusive: true);

        var cart = new Cart();
        cart.AddProduct(product, 1);
        Assert.Equal(20000, cart.Totals.TotalCents);
        Assert.Equal(0, cart.Totals.TaxCents);

        var sale = app.Sell(cart, new Payment
        {
            Method = PaymentMethod.Cash,
            AmountCents = 20000,
            TenderedCents = 50000
        });

        Assert.Equal(20000, sale.TotalCents);
        Assert.Equal(30000, sale.ChangeCents);
    }

    [Fact]
    public void Inclusive_Product_Charges_The_Entered_Price()
    {
        using var app = new TestApp();
        app.OpenRegister(50000);
        var product = app.CreateProduct("Precio final", 10000, 5, taxInclusive: true);

        var cart = new Cart();
        cart.AddProduct(product, 2);

        Assert.Equal(20000, cart.Totals.TotalCents);
        Assert.Equal(3051, cart.Totals.TaxCents); // 18% extracted from 200.00

        var sale = app.Sell(cart, new Payment
        {
            Method = PaymentMethod.Cash,
            AmountCents = 20000,
            TenderedCents = 20000
        });

        Assert.Equal(20000, sale.TotalCents);
        Assert.Equal(0, sale.ChangeCents);
    }

    [Fact]
    public void Insufficient_Stock_Rolls_Back_The_Whole_Sale()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var product = app.CreateProduct("Galletas", 5000, 1);

        var cart = new Cart();
        cart.AddProduct(product, 3);
        var payments = new[] { new Payment { Method = PaymentMethod.Cash, AmountCents = cart.Totals.TotalCents, TenderedCents = cart.Totals.TotalCents } };

        Assert.Throws<BusinessRuleException>(() => app.Checkout.CompleteSale(cart, payments, null, null));
        Assert.Equal(1, app.Store.GetProduct(product.Id)!.Stock);
        Assert.Empty(app.Store.GetRecentSales(10));
    }

    [Fact]
    public void Cash_Register_Is_Required_To_Sell()
    {
        using var app = new TestApp();
        var product = app.CreateProduct("Producto", 1000, 5);
        var cart = new Cart();
        cart.AddProduct(product, 1);

        Assert.Throws<BusinessRuleException>(() => app.Sell(cart));
    }

    [Fact]
    public void Hold_And_Resume_Does_Not_Deduct_Stock()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var product = app.CreateProduct("Refresco", 5000, 5);

        var cart = new Cart();
        cart.AddProduct(product, 2);
        var held = app.Checkout.HoldSale(cart, "Cliente mostrador");

        Assert.Equal(SaleStatus.Held, held.Status);
        Assert.Equal(5, app.Store.GetProduct(product.Id)!.Stock);
        Assert.Single(app.Checkout.GetHeldSales());
        Assert.Empty(app.Store.GetRecentSales(10));

        var resumed = app.Checkout.ResumeSale(held.Id);
        Assert.Equal(2, resumed.Lines.Single().Quantity);
        var sale = app.Sell(resumed);
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(3, app.Store.GetProduct(product.Id)!.Stock);
    }

    [Fact]
    public void Void_Restores_Stock_And_Marks_Invoice_Voided()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var product = app.CreateProduct("Leche", 7000, 5);
        var cart = new Cart();
        cart.AddProduct(product, 2);
        var sale = app.Sell(cart);

        Assert.Equal(3, app.Store.GetProduct(product.Id)!.Stock);
        app.Checkout.VoidSale(sale.Id, "Error de cobro", app.Session.CurrentUser!);

        Assert.Equal(5, app.Store.GetProduct(product.Id)!.Stock);
        var reloaded = app.Store.GetSale(sale.Id)!;
        Assert.Equal(SaleStatus.Voided, reloaded.Status);
        Assert.Equal(InvoiceStatus.Voided, reloaded.Invoice!.Status);
    }

    [Fact]
    public void Store_Credit_And_Customer_Credit_Are_Applied_And_Restored_On_Void()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var settings = app.Settings.Current.Clone();
        settings.CreditSalesEnabled = true;
        settings.StoreCreditEnabled = true;
        app.Settings.Save(settings);

        var customer = app.Customers.Save(new Customer
        {
            Name = "Juana Pérez",
            CreditLimitCents = 100000,
            StoreCreditCents = 1000
        }, isNew: true);

        var product = app.CreateProduct("Aceite", 10000, 5);
        var cart = new Cart();
        cart.AddProduct(product, 2);
        cart.SetCustomer(customer);
        var total = cart.Totals.TotalCents;

        var payments = new[]
        {
            new Payment { Method = PaymentMethod.StoreCredit, AmountCents = 1000 },
            new Payment { Method = PaymentMethod.Credit, AmountCents = total - 1000 }
        };
        var sale = app.Sell(cart, payments);

        var updated = app.Store.GetCustomer(customer.Id)!;
        Assert.Equal(0, updated.StoreCreditCents);
        Assert.Equal(total - 1000, updated.BalanceCents);

        app.Checkout.VoidSale(sale.Id, "Devolución", app.Session.CurrentUser!);
        var restored = app.Store.GetCustomer(customer.Id)!;
        Assert.Equal(1000, restored.StoreCreditCents);
        Assert.Equal(0, restored.BalanceCents);
        Assert.Equal(5, app.Store.GetProduct(product.Id)!.Stock);
    }

    [Fact]
    public void Payment_Above_Total_Is_Rejected_When_Not_Cash()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var product = app.CreateProduct("Producto", 1000, 5);
        var cart = new Cart();
        cart.AddProduct(product, 1);
        var payments = new[] { new Payment { Method = PaymentMethod.Card, AmountCents = 1500, TenderedCents = 1500 } };

        Assert.Throws<BusinessRuleException>(() => app.Checkout.CompleteSale(cart, payments, null, null));
    }
}

public sealed class CashRegisterTests
{
    [Fact]
    public void Close_Computes_Expected_Cash_And_Difference()
    {
        using var app = new TestApp();
        app.OpenRegister(10000);
        var product = app.CreateProduct("Producto", 10000, 10);

        var cart = new Cart();
        cart.AddProduct(product, 1);
        app.Sell(cart, new Payment { Method = PaymentMethod.Cash, AmountCents = 11800, TenderedCents = 12000 });
        app.Cash.AddMovement(CashMovementType.In, 500, "Cambio adicional");

        var session = app.Cash.Current!;
        var expected = 10000 + 11800 + 500;
        var report = app.Cash.Close(expected, "Cuadre correcto");

        Assert.Equal(expected, report.ExpectedCashCents);
        Assert.Equal(expected, report.Session.ClosingCents);
        Assert.Equal(0, report.Session.DifferenceCents);
        Assert.Single(app.Cash.GetHistory());
        Assert.Null(app.Cash.Current);
    }
}

public sealed class ReportsTests
{
    [Fact]
    public void Sales_Report_Groups_By_Day_Product_Payment_And_Cashier()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var product = app.CreateProduct("Café", 10000, 20, costCents: 6000);
        var cart = new Cart();
        cart.AddProduct(product, 2);
        app.Sell(cart, new Payment { Method = PaymentMethod.Card, AmountCents = 23600, TenderedCents = 23600 });

        var from = app.Clock.LocalNow.AddHours(-1);
        var report = app.Reports.Get(from, app.Clock.LocalNow.AddHours(1));

        Assert.Equal(1, report.SalesCount);
        Assert.Equal(23600, report.TotalCents);
        Assert.Equal(2, report.ItemCount);
        Assert.Equal(12000, report.CostCents);
        Assert.Contains(report.ByPayment, p => p.Method == PaymentMethod.Card && p.TotalCents == 23600);
        Assert.Contains(report.TopProducts, p => p.Name == "Café" && p.Quantity == 2);
        Assert.Contains(report.ByCashier, c => c.SalesCount == 1);
        Assert.NotEmpty(report.Daily);
    }

    [Fact]
    public void Dashboard_Reflects_Today_Sales_And_Low_Stock()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var product = app.CreateProduct("Poco stock", 1000, 1);
        var cart = new Cart();
        cart.AddProduct(product, 1);
        app.Sell(cart);

        var dashboard = app.Store.BuildDashboard(app.Clock.LocalNow);
        Assert.Equal(1, dashboard.TodaySalesCount);
        Assert.Equal(1180, dashboard.TodayTotalCents);
        Assert.True(dashboard.LowStockCount >= 1);
        Assert.NotNull(dashboard.OpenSession);
    }
}

public sealed class PrintingTests
{
    [Fact]
    public void EscPos_Formatter_Contains_Header_Item_Total_And_Cut()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var product = app.CreateProduct("Jabón de coco", 5000, 5);
        var cart = new Cart();
        cart.AddProduct(product, 2);
        var sale = app.Sell(cart);

        var data = app.Receipt.BuildData(sale);
        var bytes = ReceiptFormatter.Format(data);
        var text = System.Text.Encoding.ASCII.GetString(bytes);

        Assert.Contains("FACTURA", text);
        Assert.Contains("Jabon de coco", text);
        Assert.Contains("TOTAL", text);
        Assert.Contains("A-000001", text);
        Assert.Equal(0x1B, bytes[0]);
        Assert.Equal(0x40, bytes[1]);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void Receipt_Preview_Matches_The_Final_Total()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var product = app.CreateProduct("Producto sin impuesto", 20000, 5, taxBp: 0, taxInclusive: true);
        var cart = new Cart();
        cart.AddProduct(product, 1);
        var sale = app.Sell(cart, new Payment
        {
            Method = PaymentMethod.Cash,
            AmountCents = 20000,
            TenderedCents = 50000
        });

        var preview = ReceiptFormatter.FormatPreview(app.Receipt.BuildData(sale));

        Assert.Contains("Producto sin impuesto", preview);
        Assert.Contains("TOTAL", preview);
        Assert.Contains("200.00", preview);
        Assert.Contains("Recibido", preview);
        Assert.Contains("500.00", preview);
        Assert.Contains("CAMBIO", preview);
        Assert.Contains("300.00", preview);
        Assert.DoesNotContain("ITBIS", preview);
    }

    [Fact]
    public void Pdf_Invoice_Is_Generated()
    {
        using var app = new TestApp();
        app.OpenRegister();
        var product = app.CreateProduct("Producto PDF", 12345, 3);
        var cart = new Cart();
        cart.AddProduct(product, 1);
        var sale = app.Sell(cart);

        var path = Path.Combine(app.Directory, "factura.pdf");
        app.Receipt.ExportPdf(sale, path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [Fact]
    public void Bcrypt_Hasher_Verifies_And_Rejects()
    {
        using var app = new TestApp();
        var hash = app.Hasher.HashPin("4321");
        Assert.True(app.Hasher.VerifyPin("4321", hash));
        Assert.False(app.Hasher.VerifyPin("0000", hash));
        Assert.True(app.Auth.ApproveManager("1234", Permission.VoidSale));
        Assert.False(app.Auth.ApproveManager("0000", Permission.VoidSale));
    }

    [Fact]
    public void Backup_Snapshot_Can_Be_Created_And_Listed()
    {
        using var app = new TestApp();
        app.CreateProduct("Producto backup", 1000, 1);
        var settings = app.Settings.Current.Clone();
        settings.BackupFolder = Path.Combine(app.Directory, "backups");
        app.Settings.Save(settings);
        var backup = app.Backups.CreateBackup("test");

        Assert.True(File.Exists(backup.Path));
        Assert.True(new FileInfo(backup.Path).Length > 0);
        Assert.Contains(app.Backups.ListBackups(), b => b.Path == backup.Path);

        var afterBackup = app.CreateProduct("Producto posterior", 2000, 1);
        app.Backups.Restore(backup.Path);
        app.Db.Initialize();
        Assert.Null(app.Store.GetProduct(afterBackup.Id));
    }

    [Fact]
    public void Restore_Rejects_An_Invalid_Backup()
    {
        using var app = new TestApp();
        var bogus = Path.Combine(app.Directory, "not-a-backup.db");
        File.WriteAllText(bogus, "this is not a sqlite database");

        var ex = Assert.Throws<BusinessRuleException>(() => app.Backups.Restore(bogus));
        Assert.Equal("backup.invalid", ex.Code);
        Assert.NotNull(app.Store.LoadSettings()); // live database is untouched
    }

    [Fact]
    public void Csv_Import_Accepts_Currency_And_Thousands_Separators()
    {
        using var app = new TestApp();
        var path = Path.Combine(app.Directory, "currency.csv");
        File.WriteAllLines(path, new[]
        {
            "sku,name,barcode,category,cost,price,tax_percent,tax_inclusive,stock,min_stock,unit,favorite,active",
            "CSV-1,Producto importado,,,US$ 10.00,\"US$ 1,234.56\",18,1,5,1,unit,0,1"
        });

        var result = app.Csv.Import(path, updateExisting: false);

        Assert.Equal(1, result.Created);
        var product = app.Store.GetProducts().Single(p => p.Sku == "CSV-1");
        Assert.Equal(123456, product.PriceCents);
        Assert.True(product.TaxInclusive);
    }

    [Fact]
    public void Archived_Product_Frees_Its_Barcode()
    {
        using var app = new TestApp();
        var product = app.CreateProduct("Producto temporal", 1000, 1, barcode: "746000000123");

        app.Inventory.ArchiveProduct(product.Id);

        Assert.False(app.Store.BarcodeExists("746000000123"));
        var recreated = app.CreateProduct("Producto temporal 2", 1200, 1, barcode: "746000000123");
        Assert.NotNull(app.Store.GetProduct(recreated.Id));
    }

    [Fact]
    public void GenerateSku_Uses_Name_And_Barcode()
    {
        using var app = new TestApp();
        var sku = app.Inventory.GenerateSku("Café molido", "746000000001");
        Assert.Equal("CAFE-MOLIDO-00000001", sku);
        Assert.False(app.Store.SkuExists(sku));
    }

    [Fact]
    public void Csv_Export_And_Import_Round_Trip()
    {
        using var app = new TestApp();
        app.CreateProduct("Producto CSV", 4500, 7, barcode: "746000000099");

        var path = Path.Combine(app.Directory, "products.csv");
        app.Csv.Export(path);
        Assert.True(File.Exists(path));

        var lines = File.ReadAllLines(path);
        Assert.Contains(lines, l => l.Contains("Producto CSV"));

        var template = Path.Combine(app.Directory, "template.csv");
        ProductCsvService.Template(template);
        var result = app.Csv.Import(template, updateExisting: false);
        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Updated);
    }
}

public sealed class HardwareTests
{
    [Fact]
    public void Test_Printer_Connection_String_Is_Rejected_When_Empty()
    {
        Assert.Throws<BusinessRuleException>(() => EscPosTransportFactory.Create(""));
    }

    [Fact]
    public void File_Transport_Writes_Bytes()
    {
        var path = Path.Combine(Path.GetTempPath(), "rixelpos-tests", Guid.NewGuid().ToString("N") + ".bin");
        var transport = EscPosTransportFactory.Create("file:" + path);
        transport.WriteAsync(new byte[] { 0x1B, 0x40, 0x41 }).GetAwaiter().GetResult();
        Assert.Equal(new byte[] { 0x1B, 0x40, 0x41 }, File.ReadAllBytes(path));
        File.Delete(path);
    }

    [Fact]
    public void Qr_And_Barcodes_Produce_Commands()
    {
        var bytes = new EscPosBuilder().Initialize()
            .QrRaster("RixelPOS test")
            .BarcodeCode128("A-000001")
            .BarcodeEan13("7460000000010")
            .KickDrawer()
            .Cut()
            .Build();

        Assert.True(bytes.Length > 100);
        Assert.Contains((byte)0x1D, bytes);
    }
}
