using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using RixelPOS.Application.Abstractions;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Settings;

namespace RixelPOS.Infrastructure.Data;

public sealed partial class SqliteStore : IPosStore
{
    private static readonly AsyncLocal<DbSession?> AmbientSession = new();
    private readonly SqliteDb _db;
    private readonly DbSession? _session;

    public SqliteStore(SqliteDb db)
    {
        _db = db;
    }

    private SqliteStore(SqliteDb db, DbSession session)
    {
        _db = db;
        _session = session;
    }

    private DbSession? Session => _session ?? AmbientSession.Value;

    public T ExecuteInTransaction<T>(Func<IPosStore, T> work)
    {
        if (Session is not null)
        {
            return work(this);
        }

        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction();
        AmbientSession.Value = new DbSession(connection, transaction, ownsConnection: false);
        try
        {
            var result = work(this);
            transaction.Commit();
            return result;
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
                // ignore rollback failures
            }

            throw;
        }
        finally
        {
            AmbientSession.Value = null;
        }
    }

    public void ExecuteInTransaction(Action<IPosStore> work) =>
        ExecuteInTransaction(store =>
        {
            work(store);
            return true;
        });

    private T WithConnection<T>(Func<SqliteConnection, SqliteTransaction?, T> action)
    {
        var session = Session;
        if (session is not null)
        {
            return action(session.Connection, session.Transaction);
        }

        using var connection = _db.Open();
        return action(connection, null);
    }

    private void WithConnection(Action<SqliteConnection, SqliteTransaction?> action)
    {
        var session = Session;
        if (session is not null)
        {
            action(session.Connection, session.Transaction);
            return;
        }

        using var connection = _db.Open();
        action(connection, null);
    }

    private T WithConnection<T>(Func<SqliteConnection, T> action)
    {
        var session = Session;
        if (session is not null)
        {
            return action(session.Connection);
        }

        using var connection = _db.Open();
        return action(connection);
    }

    private void WithConnection(Action<SqliteConnection> action)
    {
        var session = Session;
        if (session is not null)
        {
            action(session.Connection);
            return;
        }

        using var connection = _db.Open();
        action(connection);
    }

    private T InTransaction<T>(Func<SqliteConnection, SqliteTransaction, T> action)
    {
        var session = Session;
        if (session?.Transaction is not null)
        {
            return action(session.Connection, session.Transaction);
        }

        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction();
        var result = action(connection, transaction);
        transaction.Commit();
        return result;
    }

    // ---------------------------------------------------------------- settings

    public BusinessSettings LoadSettings()
    {
        var values = WithConnection(c => c.Query<SettingRow>("SELECT key, value FROM settings")
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase));
        var s = new BusinessSettings();

        string Str(string key, string fallback) => values.TryGetValue(key, out var v) ? v : fallback;

        int Int(string key, int fallback)
        {
            if (values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                return i;
            }

            return fallback;
        }

        bool Bool(string key, bool fallback)
        {
            if (values.TryGetValue(key, out var v) && bool.TryParse(v, out var b))
            {
                return b;
            }

            return fallback;
        }

        T Enum<T>(string key, T fallback) where T : struct
        {
            if (values.TryGetValue(key, out var v) && System.Enum.TryParse<T>(v, true, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        s.BusinessName = Str("business.name", "");
        s.LegalName = Str("business.legal_name", "");
        s.TaxId = Str("business.tax_id", "");
        s.Address = Str("business.address", "");
        s.Phone = Str("business.phone", "");
        s.Email = Str("business.email", "");
        s.Website = Str("business.website", Branding.Website);
        s.LogoPath = Str("business.logo", "");
        s.CurrencyCode = Str("currency.code", Branding.DefaultCurrencyCode);
        s.CurrencySymbol = Str("currency.symbol", Branding.DefaultCurrencySymbol);
        s.Locale = Str("currency.locale", Branding.DefaultLocale);
        s.TaxName = Str("tax.name", "ITBIS");
        s.DefaultTaxRateBasisPoints = Int("tax.default_bp", Defaults.DefaultTaxRateBasisPoints);
        s.PricesIncludeTax = Bool("tax.inclusive", true);
        s.InvoicePrefix = Str("invoice.prefix", Defaults.InvoicePrefix);
        s.InvoicePadding = Int("invoice.padding", Defaults.InvoicePadding);
        s.ReceiptFooter = Str("receipt.footer", "¡Gracias por su compra!");
        s.ShowQrOnReceipt = Bool("receipt.qr", true);
        s.ReceiptWidth = Enum("receipt.width", ReceiptWidth.Mm80);
        s.AutoPrintReceipt = Bool("receipt.autoprint", true);
        s.PreviewReceiptBeforePrint = Bool("receipt.preview", true);
        s.PrinterConnection = Str("printer.connection", "");
        s.PrinterName = Str("printer.name", "");
        s.CashDrawerEnabled = Bool("drawer.enabled", true);
        s.AutoOpenDrawerOnCash = Bool("drawer.auto", true);
        s.CustomerDisplayEnabled = Bool("display.enabled", false);
        s.CustomerDisplayConnection = Str("display.connection", "");
        s.ScannerDebounceMs = Int("scanner.debounce", Defaults.ScannerDebounceMs);
        s.ScannerConnection = Str("scanner.connection", "");
        s.AllowNegativeStock = Bool("stock.allow_negative", false);
        s.LowStockThresholdMilli = Int("stock.low_threshold", Defaults.LowStockDefaultMilli);
        s.DiscountApprovalPercent = Int("discount.approval_percent", Defaults.DiscountApprovalPercent);
        s.RequireManagerForVoids = Bool("void.manager", true);
        s.RequireManagerForRefunds = Bool("refund.manager", true);
        s.AutoLockMinutes = Int("security.autolock_minutes", 5);
        s.Language = Enum("ui.language", AppLanguage.Spanish);
        s.Theme = Enum("ui.theme", AppTheme.Dark);
        s.UiScalePercent = Int("ui.scale", 100);
        s.BackupFolder = Str("backup.folder", "");
        s.AutoBackupOnClose = Bool("backup.on_close", true);
        s.BackupKeepCount = Int("backup.keep", Defaults.BackupKeepCount);
        s.FiscalMode = Enum("fiscal.mode", FiscalMode.None);
        s.FiscalRnc = Str("fiscal.rnc", "");
        s.NcfPrefix = Str("fiscal.ncf_prefix", "B02");
        s.StoreCreditEnabled = Bool("sales.store_credit", true);
        s.CreditSalesEnabled = Bool("sales.credit", false);
        s.Configured = Bool("app.configured", false);
        s.OnboardingCompleted = Bool("app.onboarding_done", false);
        return s;
    }

    public void SaveSettings(BusinessSettings s)
    {
        InTransaction((connection, transaction) =>
        {
            var values = new Dictionary<string, string>
            {
                ["business.name"] = s.BusinessName,
                ["business.legal_name"] = s.LegalName,
                ["business.tax_id"] = s.TaxId,
                ["business.address"] = s.Address,
                ["business.phone"] = s.Phone,
                ["business.email"] = s.Email,
                ["business.website"] = s.Website,
                ["business.logo"] = s.LogoPath,
                ["currency.code"] = s.CurrencyCode,
                ["currency.symbol"] = s.CurrencySymbol,
                ["currency.locale"] = s.Locale,
                ["tax.name"] = s.TaxName,
                ["tax.default_bp"] = s.DefaultTaxRateBasisPoints.ToString(CultureInfo.InvariantCulture),
                ["tax.inclusive"] = s.PricesIncludeTax.ToString(),
                ["invoice.prefix"] = s.InvoicePrefix,
                ["invoice.padding"] = s.InvoicePadding.ToString(CultureInfo.InvariantCulture),
                ["receipt.footer"] = s.ReceiptFooter,
                ["receipt.qr"] = s.ShowQrOnReceipt.ToString(),
                ["receipt.width"] = s.ReceiptWidth.ToString(),
                ["receipt.autoprint"] = s.AutoPrintReceipt.ToString(),
                ["receipt.preview"] = s.PreviewReceiptBeforePrint.ToString(),
                ["printer.connection"] = s.PrinterConnection,
                ["printer.name"] = s.PrinterName,
                ["drawer.enabled"] = s.CashDrawerEnabled.ToString(),
                ["drawer.auto"] = s.AutoOpenDrawerOnCash.ToString(),
                ["display.enabled"] = s.CustomerDisplayEnabled.ToString(),
                ["display.connection"] = s.CustomerDisplayConnection,
                ["scanner.debounce"] = s.ScannerDebounceMs.ToString(CultureInfo.InvariantCulture),
                ["scanner.connection"] = s.ScannerConnection,
                ["stock.allow_negative"] = s.AllowNegativeStock.ToString(),
                ["stock.low_threshold"] = s.LowStockThresholdMilli.ToString(CultureInfo.InvariantCulture),
                ["discount.approval_percent"] = s.DiscountApprovalPercent.ToString(CultureInfo.InvariantCulture),
                ["void.manager"] = s.RequireManagerForVoids.ToString(),
                ["refund.manager"] = s.RequireManagerForRefunds.ToString(),
                ["security.autolock_minutes"] = s.AutoLockMinutes.ToString(CultureInfo.InvariantCulture),
                ["ui.language"] = s.Language.ToString(),
                ["ui.theme"] = s.Theme.ToString(),
                ["ui.scale"] = s.UiScalePercent.ToString(CultureInfo.InvariantCulture),
                ["backup.folder"] = s.BackupFolder,
                ["backup.on_close"] = s.AutoBackupOnClose.ToString(),
                ["backup.keep"] = s.BackupKeepCount.ToString(CultureInfo.InvariantCulture),
                ["fiscal.mode"] = s.FiscalMode.ToString(),
                ["fiscal.rnc"] = s.FiscalRnc,
                ["fiscal.ncf_prefix"] = s.NcfPrefix,
                ["sales.store_credit"] = s.StoreCreditEnabled.ToString(),
                ["sales.credit"] = s.CreditSalesEnabled.ToString(),
                ["app.configured"] = s.Configured.ToString(),
                ["app.onboarding_done"] = s.OnboardingCompleted.ToString()
            };

            foreach (var (key, value) in values)
            {
                connection.Execute(
                    "INSERT INTO settings(key,value) VALUES(@key,@value) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                    new { key, value }, transaction);
            }

            return true;
        });
    }

    public string? GetSetting(string key) =>
        WithConnection(c => c.QueryFirstOrDefault<string>(
            "SELECT value FROM settings WHERE key = @key", new { key }));

    public long NextSequence(string name) =>
        WithConnection((connection, transaction) => connection.ExecuteScalar<long>(
            """
            INSERT INTO sequences(name, value) VALUES(@name, 1)
            ON CONFLICT(name) DO UPDATE SET value = value + 1
            RETURNING value;
            """,
            new { name }, transaction));

    private sealed class SettingRow
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
