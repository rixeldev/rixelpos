using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RixelPOS.Application.Abstractions;

namespace RixelPOS.Infrastructure.Licensing;

/// <summary>
/// Vendor activation state. A valid license file is signed with an embedded key and
/// bound to the current machine, so editing or copying it to another computer invalidates
/// it. There is no in-app way to remove or replace the license.
/// </summary>
public sealed class LicenseService
{
    private const int FormatVersion = 1;
    private static readonly byte[] SigningKey = Convert.FromBase64String("obLD1OX2BxgpOktcbX6PkKGyw9Tl9gcYKTpLXG1+j5A=");
    private readonly IAppLogger _logger;
    private readonly string _licensePath;
    private readonly Func<string, bool> _pinVerifier;

    public LicenseService(IAppLogger logger)
        : this(logger, Path.Combine(AppPaths.DataDirectory, "license.key"))
    {
    }

    public LicenseService(IAppLogger logger, string licensePath, Func<string, bool>? pinVerifier = null)
    {
        _logger = logger;
        _licensePath = licensePath;
        _pinVerifier = pinVerifier ?? VendorLicense.VerifyPin;
    }

    public string LicensePath => _licensePath;

    public string MachineId => MachineIdentity.GetId();

    public bool IsActivated
    {
        get
        {
            try
            {
                if (!File.Exists(_licensePath))
                {
                    return false;
                }

                var model = JsonSerializer.Deserialize<LicenseFile>(File.ReadAllText(_licensePath));
                if (model is null ||
                    model.Version != FormatVersion ||
                    !string.Equals(model.Machine, MachineId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var expected = Sign(model.Machine, model.ActivatedAt);
                var stored = Convert.FromBase64String(model.Signature);
                return CryptographicOperations.FixedTimeEquals(expected, stored);
            }
            catch
            {
                return false;
            }
        }
    }

    public bool TryActivate(string? pin)
    {
        if (!_pinVerifier(pin ?? ""))
        {
            _logger.Warn("License activation failed: wrong PIN");
            return false;
        }

        try
        {
            var machine = MachineId;
            var activatedAt = DateTime.UtcNow.ToString("O");
            var model = new LicenseFile
            {
                Version = FormatVersion,
                Machine = machine,
                ActivatedAt = activatedAt,
                Signature = Convert.ToBase64String(Sign(machine, activatedAt))
            };

            var folder = Path.GetDirectoryName(Path.GetFullPath(_licensePath));
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(_licensePath, JsonSerializer.Serialize(model));
            _logger.Info("License activated for this machine");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("License activation could not be saved", ex);
            return false;
        }
    }

    private static byte[] Sign(string machine, string activatedAt) =>
        HMACSHA256.HashData(SigningKey, Encoding.UTF8.GetBytes($"{FormatVersion}|{machine}|{activatedAt}"));

    private sealed class LicenseFile
    {
        public int Version { get; set; }
        public string Machine { get; set; } = "";
        public string ActivatedAt { get; set; } = "";
        public string Signature { get; set; } = "";
    }
}
