using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using RixelPOS.Domain.Common;

namespace RixelPOS.Infrastructure.Updates;

public enum UpdateKind
{
    LinuxPackage,
    LinuxDeb,
    WindowsInstaller
}

public sealed record UpdateInfo(Version Version, Uri PackageUri, string FileName, string Sha256, UpdateKind Kind);

public sealed record UpdateResult(bool Updated, string Message, bool Restart, bool ExitApplication);

/// <summary>
/// Startup update checker. It only runs once at launch, uses a short timeout, and never
/// blocks the application when there is no internet or the manifest is unavailable.
/// </summary>
public sealed class UpdateService
{
    private const string ManifestUrl = "https://rixeldev.github.io/rixelpos/updates/update.json";
    private static readonly HttpClient CheckClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return null;
            }

            using var response = await CheckClient.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var manifest = JsonSerializer.Deserialize<Manifest>(json, JsonOptions);
            if (manifest is null || !Version.TryParse(manifest.Version, out var remote))
            {
                return null;
            }

            if (!IsNewer(remote, Branding.AssemblyVersion))
            {
                return null;
            }

            var (asset, kind) = SelectAsset(manifest);
            if (asset is null || string.IsNullOrWhiteSpace(asset.File) || string.IsNullOrWhiteSpace(asset.Sha256))
            {
                return null;
            }

            var fileName = Path.GetFileName(asset.File);
            if (!string.Equals(fileName, asset.File, StringComparison.Ordinal))
            {
                return null;
            }

            var uri = new Uri(new Uri(ManifestUrl), fileName);
            return new UpdateInfo(remote, uri, fileName, asset.Sha256.ToLowerInvariant(), kind);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "rixelpos-update");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, info.FileName);

            using var response = await DownloadClient.GetAsync(info.PackageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(path);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                readTotal += read;
                if (total > 0)
                {
                    progress?.Report((double)readTotal / total);
                }
            }

            await target.FlushAsync(cancellationToken);
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                return null;
            }

            return path;
        }
        catch
        {
            return null;
        }
    }

    public async Task<UpdateResult> InstallAsync(UpdateInfo info, string packagePath,
        CancellationToken cancellationToken = default)
    {
        if (info.Kind == UpdateKind.WindowsInstaller)
        {
            try
            {
                Process.Start(new ProcessStartInfo(packagePath)
                {
                    UseShellExecute = true,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
                });
                return new UpdateResult(true, "Instalador de actualización iniciado.", false, true);
            }
            catch (Exception ex)
            {
                return new UpdateResult(false, $"No se pudo iniciar el instalador: {ex.Message}", false, false);
            }
        }

        var process = Environment.ProcessPath ?? "";
        if (!process.Contains("/usr/lib/rixelpos", StringComparison.Ordinal) &&
            !process.Contains("/usr/bin/rixelpos", StringComparison.Ordinal))
        {
            return new UpdateResult(false,
                "La actualización automática solo está disponible para la instalación del sistema (pacman).", false, false);
        }

        var tool = info.Kind == UpdateKind.LinuxDeb ? "dpkg" : "pacman";
        var fileName = info.Kind == UpdateKind.LinuxDeb ? "dpkg" : "pacman";
        if (!File.Exists($"/usr/bin/{fileName}"))
        {
            return new UpdateResult(false, $"No se encontró {tool}. Actualiza manualmente.", false, false);
        }

        var pkexec = FindExecutable("pkexec");
        var sudo = FindExecutable("sudo");
        if (pkexec is null && sudo is null)
        {
            return new UpdateResult(false,
                "No se encontró pkexec ni sudo para instalar la actualización.", false, false);
        }

        try
        {
            var start = pkexec is not null
                ? new ProcessStartInfo(pkexec)
                : new ProcessStartInfo(sudo!);

            if (pkexec is null)
            {
                start.ArgumentList.Add("-n");
            }

            start.ArgumentList.Add(tool);
            start.ArgumentList.Add(info.Kind == UpdateKind.LinuxDeb ? "-i" : "-U");
            if (info.Kind == UpdateKind.LinuxDeb)
            {
                start.ArgumentList.Add("--force-confdef");
                start.ArgumentList.Add("--force-confold");
            }
            else
            {
                start.ArgumentList.Add("--noconfirm");
            }

            start.ArgumentList.Add(packagePath);
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;

            using var child = Process.Start(start);
            if (child is null)
            {
                return new UpdateResult(false, "No se pudo iniciar la instalación.", false, false);
            }

            await child.WaitForExitAsync(cancellationToken);
            if (child.ExitCode != 0)
            {
                return new UpdateResult(false,
                    "La actualización no se completó. Puedes actualizar manualmente con pacman -Syu.", false, false);
            }

            return new UpdateResult(true, "Actualización instalada.", true, false);
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, $"No se pudo instalar la actualización: {ex.Message}", false, false);
        }
    }

    public static bool IsNewer(Version remote, Version current) => remote > current;

    private static (UpdateAsset? Asset, UpdateKind Kind) SelectAsset(Manifest manifest)
    {
        if (OperatingSystem.IsWindows())
        {
            return (manifest.Windows, UpdateKind.WindowsInstaller);
        }

        if (OperatingSystem.IsLinux())
        {
            if (File.Exists("/usr/bin/pacman"))
            {
                return (manifest.Linux, UpdateKind.LinuxPackage);
            }

            if (File.Exists("/usr/bin/dpkg"))
            {
                return (manifest.LinuxDeb, UpdateKind.LinuxDeb);
            }
        }

        return (null, UpdateKind.LinuxPackage);
    }

    private static string? FindExecutable(string name)
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class Manifest
    {
        public string Version { get; set; } = "";
        public UpdateAsset? Linux { get; set; }
        public UpdateAsset? LinuxDeb { get; set; }
        public UpdateAsset? Windows { get; set; }
    }

    private sealed class UpdateAsset
    {
        public string File { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }
}
