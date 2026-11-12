using Microsoft.Data.Sqlite;
using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Printing;
using RixelPOS.Application.Services;
using RixelPOS.Domain.Common;
using RixelPOS.Infrastructure.Data;

namespace RixelPOS.Infrastructure.Backup;

public sealed class SqliteBackupService : IBackupService
{
    private readonly SqliteDb _db;
    private readonly SettingsService _settings;
    private readonly IAppLogger _logger;

    public SqliteBackupService(SqliteDb db, SettingsService settings, IAppLogger logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public string BackupFolder
    {
        get
        {
            var configured = _settings.Current.BackupFolder;
            return string.IsNullOrWhiteSpace(configured) ? AppPaths.BackupsDirectory : configured;
        }
    }

    public BackupInfo CreateBackup(string? label = null)
    {
        try
        {
            var folder = BackupFolder;
            Directory.CreateDirectory(folder);
            var suffix = string.IsNullOrWhiteSpace(label) ? "" : "-" + Sanitize(label);
            var path = Path.Combine(folder, $"rixelpos-backup-{DateTime.Now:yyyyMMdd-HHmmss}{suffix}.db");
            var index = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, $"rixelpos-backup-{DateTime.Now:yyyyMMdd-HHmmss}-{index++}{suffix}.db");
            }

            _db.CreateSnapshot(path);
            _logger.Info($"Backup created: {path}");
            return new BackupInfo(path, File.GetCreationTime(path), new FileInfo(path).Length);
        }
        catch (Exception ex)
        {
            _logger.Error("Backup failed", ex);
            throw new BusinessRuleException("backup.failed",
                "No se pudo crear la copia de seguridad. Verifica que la carpeta de destino exista y tengas permisos de escritura.");
        }
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        try
        {
            if (!Directory.Exists(BackupFolder))
            {
                return Array.Empty<BackupInfo>();
            }

            return Directory.GetFiles(BackupFolder, "rixelpos-backup-*.db")
                .Select(p => new BackupInfo(p, File.GetLastWriteTime(p), new FileInfo(p).Length))
                .OrderByDescending(b => b.CreatedAt)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not list backups: {ex.Message}");
            return Array.Empty<BackupInfo>();
        }
    }

    public void Restore(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new BusinessRuleException("backup.not_found", "The selected backup file does not exist.");
        }

        try
        {
            var safety = CreateBackup("antes-de-restaurar");
            _db.Checkpoint();
            SqliteConnection.ClearAllPools();

            var wal = _db.DatabasePath + "-wal";
            var shm = _db.DatabasePath + "-shm";
            if (File.Exists(wal))
            {
                File.Delete(wal);
            }

            if (File.Exists(shm))
            {
                File.Delete(shm);
            }

            File.Copy(backupPath, _db.DatabasePath, overwrite: true);
            _logger.Info($"Database restored from {backupPath}. Safety backup: {safety.Path}");
        }
        catch (Exception ex) when (ex is not BusinessRuleException)
        {
            _logger.Error("Restore failed", ex);
            throw new BusinessRuleException("backup.restore_failed",
                "No se pudo restaurar la copia. Cierra RixelPOS, verifica permisos y vuelve a intentarlo.");
        }
    }

    public void Prune(int keepCount)
    {
        keepCount = Math.Max(1, keepCount);
        try
        {
            var files = Directory.Exists(BackupFolder)
                ? Directory.GetFiles(BackupFolder, "rixelpos-backup-*.db")
                    .OrderByDescending(File.GetLastWriteTime)
                    .Skip(keepCount)
                    .ToList()
                : new List<string>();

            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not prune backups: {ex.Message}");
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Where(c => !invalid.Contains(c) && !char.IsWhiteSpace(c)).Take(40).ToArray());
    }
}
