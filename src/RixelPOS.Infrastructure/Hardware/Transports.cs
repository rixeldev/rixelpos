using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using RixelPOS.Domain.Common;

namespace RixelPOS.Infrastructure.Hardware;

public interface IEscPosTransport
{
    string Description { get; }
    Task WriteAsync(byte[] data, CancellationToken cancellationToken = default);
    Task TestAsync(CancellationToken cancellationToken = default);
}

public static class EscPosTransportFactory
{
    public static IEscPosTransport Create(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new BusinessRuleException("printer.not_configured", "No printer connection is configured.");
        }

        var value = connection.Trim();

        if (value.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("network://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("net://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(value.Replace("network://", "tcp://", StringComparison.OrdinalIgnoreCase)
                .Replace("net://", "tcp://", StringComparison.OrdinalIgnoreCase));
            return new TcpPrinterTransport(uri.Host, uri.IsDefaultPort ? 9100 : uri.Port);
        }

        if (value.StartsWith("serial:", StringComparison.OrdinalIgnoreCase))
        {
            var (path, baud) = ParseSerial(value[7..]);
            return new SerialPrinterTransport(path, baud);
        }

        if (value.StartsWith("usb:", StringComparison.OrdinalIgnoreCase))
        {
            var target = value[4..].TrimStart('/');
            if (!target.StartsWith('/') && (target.StartsWith("dev/", StringComparison.OrdinalIgnoreCase) || OperatingSystem.IsLinux()))
            {
                return new UnixDeviceTransport("/" + target.TrimStart('/'));
            }

            if (OperatingSystem.IsWindows())
            {
                return new WindowsRawPrinterTransport(target);
            }

            return new UnixDeviceTransport(value[4..]);
        }

        if (value.StartsWith("cups:", StringComparison.OrdinalIgnoreCase))
        {
            return new CupsPrinterTransport(value[5..]);
        }

        if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return new FilePrinterTransport(value[5..]);
        }

        if (value.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return new UnixDeviceTransport(value);
        }

        if (value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || value.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
        {
            return new SerialPrinterTransport(value, 9600);
        }

        return OperatingSystem.IsWindows()
            ? new WindowsRawPrinterTransport(value)
            : new CupsPrinterTransport(value);
    }

    private static (string Path, int Baud) ParseSerial(string value)
    {
        var baud = 9600;
        var path = value;
        var at = value.LastIndexOf('@');
        if (at > 0 && int.TryParse(value[(at + 1)..], out var parsed))
        {
            baud = parsed;
            path = value[..at];
        }

        var query = path.IndexOf('?');
        if (query >= 0)
        {
            path = path[..query];
        }

        return (path.Trim(), baud);
    }
}

public static class HardwareErrors
{
    public static (string Message, string Code) Describe(Exception exception, string connection)
    {
        return exception switch
        {
            BusinessRuleException br => (br.Message, br.Code),
            SocketException or TcpException or TimeoutException =>
                ($"No se pudo conectar con la impresora ({connection}). Verifica que esté encendida, conectada a la red y que la dirección IP sea correcta.", "printer.network"),
            UnauthorizedAccessException =>
                ($"El sistema no permite acceder a la impresora ({connection}). En Linux agrega tu usuario al grupo 'lp' y cierra sesión: sudo usermod -aG lp $USER. En Windows ejecuta RixelPOS como administrador una vez.", "printer.permission"),
            IOException io when io.Message.Contains("busy", StringComparison.OrdinalIgnoreCase) =>
                ($"La impresora está ocupada o sin papel. Revisa el papel y vuelve a intentar.", "printer.busy"),
            IOException =>
                ($"No se pudo escribir en la impresora ({connection}). Revisa el cable USB, el puerto y que el equipo esté encendido.", "printer.io"),
            Win32Exception =>
                ($"Windows no encontró el controlador de la impresora '{connection}'. Instálala en Configuración → Impresoras y usa su nombre exacto.", "printer.driver"),
            InvalidOperationException =>
                ($"La impresora no está disponible ({connection}). Revisa la configuración en Ajustes → Hardware.", "printer.unavailable"),
            _ => ($"Error al imprimir: {exception.Message}", "printer.error")
        };
    }
}

public sealed class TcpException : Exception
{
    public TcpException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class TcpPrinterTransport : IEscPosTransport
{
    private readonly string _host;
    private readonly int _port;

    public TcpPrinterTransport(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public string Description => $"Red {_host}:{_port}";

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            await stream.WriteAsync(data, timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new TcpException($"Tiempo de espera agotado conectando a {_host}:{_port}.", ex);
        }
        catch (SocketException ex)
        {
            throw new TcpException($"No se pudo abrir la conexión con {_host}:{_port}.", ex);
        }
    }

    public async Task TestAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            throw new TcpException($"No hay respuesta de {_host}:{_port}.", ex);
        }
    }
}

public sealed class SerialPrinterTransport : IEscPosTransport
{
    private readonly string _path;
    private readonly int _baud;

    public SerialPrinterTransport(string path, int baud)
    {
        _path = path;
        _baud = baud;
    }

    public string Description => $"Serial {_path} @ {_baud}";

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        using var port = new SerialPort(_path, _baud, Parity.None, 8, StopBits.One)
        {
            WriteTimeout = 5000,
            ReadTimeout = 5000,
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true
        };

        port.Open();
        port.Write(data, 0, data.Length);
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
    }

    public Task TestAsync(CancellationToken cancellationToken = default)
    {
        using var port = new SerialPort(_path, _baud, Parity.None, 8, StopBits.One);
        port.Open();
        return Task.CompletedTask;
    }
}

public sealed class UnixDeviceTransport : IEscPosTransport
{
    private readonly string _devicePath;

    public UnixDeviceTransport(string devicePath)
    {
        _devicePath = devicePath;
    }

    public string Description => $"USB/LPT {_devicePath}";

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Direct device access is only available on Linux/macOS.");
        }

        await using var stream = new FileStream(_devicePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task TestAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_devicePath))
        {
            throw new FileNotFoundException($"No existe el dispositivo {_devicePath}.");
        }

        return Task.CompletedTask;
    }
}

public sealed class FilePrinterTransport : IEscPosTransport
{
    private readonly string _path;

    public FilePrinterTransport(string path)
    {
        _path = path;
    }

    public string Description => $"Archivo {_path}";

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(_path, data, cancellationToken).ConfigureAwait(false);
    }

    public Task TestAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return Task.CompletedTask;
    }
}

public sealed class CupsPrinterTransport : IEscPosTransport
{
    private readonly string _printerName;

    public CupsPrinterTransport(string printerName)
    {
        _printerName = printerName;
    }

    public string Description => $"CUPS {_printerName}";

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("CUPS is only available on Linux/macOS.");
        }

        var startInfo = new ProcessStartInfo("lp")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(_printerName);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("raw");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo iniciar el comando lp de CUPS.");

        await process.StandardInput.BaseStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new IOException($"CUPS rechazó el trabajo: {stderr.Trim()}");
        }
    }

    public async Task TestAsync(CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("lpstat")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(_printerName);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("CUPS no está instalado.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new IOException($"La impresora CUPS '{_printerName}' no existe o está detenida.");
        }
    }
}

public sealed class WindowsRawPrinterTransport : IEscPosTransport
{
    private readonly string _printerName;

    public WindowsRawPrinterTransport(string printerName)
    {
        _printerName = printerName;
    }

    public string Description => $"Windows spooler {_printerName}";

    public Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows raw printing is only available on Windows.");
        }

        NativeMethods.OpenPrinter(_printerName, out var handle, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var docInfo = new NativeMethods.DocInfo
            {
                DocName = "RixelPOS Receipt",
                DataType = "RAW"
            };

            if (!NativeMethods.StartDocPrinter(handle, 1, ref docInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                if (!NativeMethods.StartPagePrinter(handle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                try
                {
                    if (!NativeMethods.WritePrinter(handle, data, data.Length, out var written) || written != data.Length)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    NativeMethods.EndPagePrinter(handle);
                }
            }
            finally
            {
                NativeMethods.EndDocPrinter(handle);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(handle);
        }

        return Task.CompletedTask;
    }

    public Task TestAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows raw printing is only available on Windows.");
        }

        if (!NativeMethods.OpenPrinter(_printerName, out var handle, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        NativeMethods.ClosePrinter(handle);
        return Task.CompletedTask;
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DocInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string DocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool StartDocPrinter(IntPtr handle, int level, ref DocInfo docInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr handle, byte[] data, int count, out int written);
    }
}
