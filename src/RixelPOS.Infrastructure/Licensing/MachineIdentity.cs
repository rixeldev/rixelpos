using System.Security.Cryptography;
using System.Text;

namespace RixelPOS.Infrastructure.Licensing;

/// <summary>
/// Stable, privacy-preserving machine fingerprint used to bind the activation file.
/// </summary>
public static class MachineIdentity
{
    public static string GetId()
    {
        var parts = new List<string> { Environment.MachineName };

        if (OperatingSystem.IsLinux())
        {
            foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var value = File.ReadAllText(path).Trim();
                    if (value.Length > 0)
                    {
                        parts.Add(value);
                        break;
                    }
                }
                catch
                {
                    // Fall back to the machine name.
                }
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            parts.Add(Environment.GetEnvironmentVariable("COMPUTERNAME") ?? "");
        }

        var raw = string.Join("|", parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}
