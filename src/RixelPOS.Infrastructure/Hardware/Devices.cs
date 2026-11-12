using System.IO.Ports;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Enums;

namespace RixelPOS.Infrastructure.Hardware;

public sealed class EscPosPrinter : IReceiptPrinter, ILabelPrinter
{
    private readonly SettingsService _settings;
    private readonly IAppLogger _logger;

    public EscPosPrinter(SettingsService settings, IAppLogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.Current.PrinterConnection);

    public string Description => string.IsNullOrWhiteSpace(_settings.Current.PrinterConnection)
        ? "Sin impresora"
        : _settings.Current.PrinterConnection;

    public async Task<PrintResult> PrintAsync(ReceiptData data, CancellationToken cancellationToken = default)
    {
        var connection = _settings.Current.PrinterConnection;
        try
        {
            var transport = EscPosTransportFactory.Create(connection);
            var bytes = ReceiptFormatter.Format(data);
            await transport.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            return PrintResult.Ok("Recibo enviado a la impresora.");
        }
        catch (Exception ex)
        {
            var (message, code) = HardwareErrors.Describe(ex, connection);
            _logger.Error($"Receipt print failed ({code})", ex);
            return PrintResult.Fail(message, code);
        }
    }

    public async Task<PrintResult> PrintTestAsync(CancellationToken cancellationToken = default)
    {
        var s = _settings.Current;
        try
        {
            var transport = EscPosTransportFactory.Create(s.PrinterConnection);
            var b = new EscPosBuilder();
            b.Initialize();

            if (!string.IsNullOrWhiteSpace(s.LogoPath))
            {
                b.LogoRaster(s.LogoPath, s.ReceiptWidth == ReceiptWidth.Mm58 ? 384 : 576);
            }

            b.Align(EscPosBuilder.TextAlign.Center)
                .Bold().DoubleSize().Line(Branding.ProductName).DoubleSize(false).Bold(false)
                .Line("Prueba de impresora")
                .Line(DateTime.Now.ToString("dd/MM/yyyy HH:mm"))
                .Separator('-', s.ReceiptWidth == ReceiptWidth.Mm58 ? 32 : 48)
                .Line("Si lees esto, la impresora")
                .Line("está configurada correctamente.")
                .Line(s.BusinessName)
                .Line(s.PrinterConnection)
                .QrRaster($"RixelPOS test {DateTime.UtcNow:O}", 4)
                .Feed(2)
                .Cut();

            await transport.WriteAsync(b.Build(), cancellationToken).ConfigureAwait(false);
            return PrintResult.Ok("Prueba enviada. Revisa tu impresora.");
        }
        catch (Exception ex)
        {
            var (message, code) = HardwareErrors.Describe(ex, s.PrinterConnection);
            _logger.Error($"Test print failed ({code})", ex);
            return PrintResult.Fail(message, code);
        }
    }

    public async Task<PrintResult> OpenCashDrawerAsync(CancellationToken cancellationToken = default)
    {
        var s = _settings.Current;
        if (!s.CashDrawerEnabled)
        {
            return PrintResult.Fail("El cajón de dinero está desactivado en Ajustes → Hardware.", "drawer.disabled");
        }

        try
        {
            var transport = EscPosTransportFactory.Create(s.PrinterConnection);
            var bytes = new EscPosBuilder().Initialize().KickDrawer().Build();
            await transport.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            return PrintResult.Ok("Pulso de apertura enviado.");
        }
        catch (Exception ex)
        {
            var (message, code) = HardwareErrors.Describe(ex, s.PrinterConnection);
            _logger.Error($"Drawer open failed ({code})", ex);
            return PrintResult.Fail(message, code);
        }
    }

    public async Task<PrintResult> PrintLabelAsync(LabelData label, CancellationToken cancellationToken = default)
    {
        var s = _settings.Current;
        var width = s.ReceiptWidth == ReceiptWidth.Mm58 ? 32 : 48;
        try
        {
            var transport = EscPosTransportFactory.Create(s.PrinterConnection);
            var b = new EscPosBuilder();
            b.Initialize().Align(EscPosBuilder.TextAlign.Center);
            b.Bold().Line(Truncate(label.BusinessName, width)).Bold(false);
            foreach (var line in ReceiptFormatter.Wrap(label.ProductName, width))
            {
                b.Line(line);
            }

            b.Bold().DoubleSize().Line(label.PriceText).DoubleSize(false).Bold(false);
            if (!string.IsNullOrWhiteSpace(label.Barcode))
            {
                if (BarcodeLooksEan13(label.Barcode))
                {
                    b.BarcodeEan13(label.Barcode);
                }
                else
                {
                    b.BarcodeCode128(label.Barcode, 60);
                }
            }
            else if (!string.IsNullOrWhiteSpace(label.Sku))
            {
                b.BarcodeCode128(label.Sku, 60);
            }

            b.Line($"SKU: {label.Sku}").Feed(1).Cut();
            await transport.WriteAsync(b.Build(), cancellationToken).ConfigureAwait(false);
            return PrintResult.Ok("Etiqueta enviada.");
        }
        catch (Exception ex)
        {
            var (message, code) = HardwareErrors.Describe(ex, s.PrinterConnection);
            _logger.Error($"Label print failed ({code})", ex);
            return PrintResult.Fail(message, code);
        }
    }

    private static bool BarcodeLooksEan13(string code) =>
        code.Length == 13 && code.All(char.IsDigit);

    private static string Truncate(string text, int width)
    {
        text = EscPosBuilder.AsciiText(text);
        return text.Length <= width ? text : text[..width];
    }
}

public sealed class SerialCustomerDisplay : ICustomerDisplay
{
    private readonly SettingsService _settings;
    private readonly IAppLogger _logger;

    public SerialCustomerDisplay(SettingsService settings, IAppLogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured =>
        _settings.Current.CustomerDisplayEnabled && !string.IsNullOrWhiteSpace(_settings.Current.CustomerDisplayConnection);

    public Task ShowLineAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Task.CompletedTask;
        }

        try
        {
            var connection = _settings.Current.CustomerDisplayConnection;
            var raw = connection;
            var baud = 9600;
            if (raw.StartsWith("serial:", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[7..];
            }

            var at = raw.LastIndexOf('@');
            if (at > 0 && int.TryParse(raw[(at + 1)..], out var parsed))
            {
                baud = parsed;
                raw = raw[..at];
            }

            using var port = new System.IO.Ports.SerialPort(raw, baud, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One)
            {
                WriteTimeout = 1500,
                DtrEnable = true,
                RtsEnable = true
            };
            port.Open();
            var payload = "\f" + EscPosBuilder.AsciiText(text).PadRight(20)[..20];
            port.Write(payload);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Customer display failed: {ex.Message}");
            return Task.CompletedTask;
        }
    }
}

public sealed class SerialScannerListener : IScannerListener
{
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private System.IO.Ports.SerialPort? _port;
    private string _buffer = "";

    public SerialScannerListener(IAppLogger logger)
    {
        _logger = logger;
    }

    public event EventHandler<ScanEventArgs>? Scanned;

    public bool IsRunning => _port?.IsOpen == true;

    public Task StartAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new BusinessRuleException("scanner.no_port", "No serial scanner port is configured.");
        }

        try
        {
            StopAsync().GetAwaiter().GetResult();
            var raw = connectionString.Trim();
            if (raw.StartsWith("serial:", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[7..];
            }

            var baud = 9600;
            var at = raw.LastIndexOf('@');
            if (at > 0 && int.TryParse(raw[(at + 1)..], out var parsed))
            {
                baud = parsed;
                raw = raw[..at];
            }

            var port = new System.IO.Ports.SerialPort(raw, baud, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One)
            {
                ReadTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };
            port.DataReceived += OnDataReceived;
            port.Open();
            lock (_gate)
            {
                _port = port;
            }

            _logger.Info($"Serial scanner listening on {raw} @ {baud}");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.Error("Serial scanner failed to start", ex);
            throw new BusinessRuleException("scanner.serial_error",
                $"No se pudo abrir el escáner serial. Revisa el puerto y los permisos. Detalle: {ex.Message}");
        }
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_port is not null)
            {
                _port.DataReceived -= OnDataReceived;
                try
                {
                    _port.Close();
                }
                catch
                {
                    // ignore
                }

                _port.Dispose();
                _port = null;
            }

            _buffer = "";
            return Task.CompletedTask;
        }
    }

    private void OnDataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
    {
        try
        {
            var data = _port?.ReadExisting();
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            lock (_gate)
            {
                _buffer += data;
                while (true)
                {
                    var index = _buffer.IndexOfAny(new[] { '\r', '\n' });
                    if (index < 0)
                    {
                        if (_buffer.Length > 512)
                        {
                            _buffer = "";
                        }

                        break;
                    }

                    var code = _buffer[..index].Trim();
                    _buffer = _buffer[(index + 1)..];
                    if (code.Length >= 2)
                    {
                        Scanned?.Invoke(this, new ScanEventArgs(code, DateTime.UtcNow));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Scanner read error: {ex.Message}");
        }
    }
}

public sealed class DeviceDiscovery : IDeviceDiscovery
{
    public async Task<IReadOnlyList<DeviceInfo>> DiscoverPrintersAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<DeviceInfo>();

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            foreach (var printer in await RunAsync("lpstat", "-a", cancellationToken).ConfigureAwait(false))
            {
                var name = printer.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new DeviceInfo($"cups:{name}", name, "CUPS (USB/Red)"));
                }
            }

            foreach (var port in SerialPort.GetPortNames().OrderBy(p => p))
            {
                list.Add(new DeviceInfo($"serial:{port}@9600", port, "Serial"));
            }

            foreach (var device in Directory.Exists("/dev/usb")
                         ? Directory.GetFiles("/dev/usb", "lp*")
                         : Array.Empty<string>())
            {
                list.Add(new DeviceInfo($"usb:{device}", device, "USB directo"));
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            foreach (var printer in await RunAsync("powershell.exe",
                         "-NoProfile -Command \"Get-Printer | Select-Object -ExpandProperty Name\"", cancellationToken)
                     .ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(printer))
                {
                    list.Add(new DeviceInfo($"usb:{printer.Trim()}", printer.Trim(), "Windows spooler"));
                }
            }

            foreach (var port in SerialPort.GetPortNames().OrderBy(p => p))
            {
                list.Add(new DeviceInfo($"serial:{port}@9600", port, "Serial"));
            }
        }

        return list;
    }

    public IReadOnlyList<string> DiscoverSerialPorts() => SerialPort.GetPortNames().OrderBy(p => p).ToList();

    public async Task<PrintResult> TestConnectionAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            var transport = EscPosTransportFactory.Create(connectionString);
            await transport.TestAsync(cancellationToken).ConfigureAwait(false);
            return PrintResult.Ok($"Conexión correcta: {transport.Description}");
        }
        catch (Exception ex)
        {
            var (message, code) = HardwareErrors.Describe(ex, connectionString);
            return PrintResult.Fail(message, code);
        }
    }

    private static async Task<IReadOnlyList<string>> RunAsync(string command, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(command, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return Array.Empty<string>();
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
