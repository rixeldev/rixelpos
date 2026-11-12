using System.Security.Cryptography;
using System.Text;
using RixelPOS.Infrastructure;

namespace RixelPOS.Desktop.Services;

/// <summary>
/// Ensures only one RixelPOS process uses a given data folder. Two windows selling on
/// the same database would share the cash session and the printer in confusing ways.
/// </summary>
public static class SingleInstance
{
    private static Mutex? _mutex;

    /// <summary>True when another RixelPOS process already owns this data folder.</summary>
    public static bool IsSecondary { get; private set; }

    public static bool TryAcquire()
    {
        try
        {
            var path = Path.GetFullPath(AppPaths.DataDirectory);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            var name = "RixelPOS-" + hash[..16];
            _mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
            IsSecondary = !createdNew;
            if (IsSecondary)
            {
                _mutex.Dispose();
                _mutex = null;
            }

            return createdNew;
        }
        catch
        {
            // If the OS cannot create a named mutex, never block the application.
            IsSecondary = false;
            return true;
        }
    }
}
