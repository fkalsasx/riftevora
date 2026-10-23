using System.IO.Compression;
using Serilog;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Application.Services;

public class BackupRestoreService : IBackupService
{
    private readonly string _databaseFilePath;
    private readonly string _libraryDirectory;

    public BackupRestoreService(string databaseFilePath, string libraryDirectory)
    {
        _databaseFilePath = databaseFilePath;
        _libraryDirectory = libraryDirectory;
    }

    public async Task<OperationResult<string>> CreateBackupAsync(string targetFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(targetFilePath))
            {
                File.Delete(targetFilePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);

            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(targetFilePath, ZipArchiveMode.Create);

                // 1. Database file
                if (File.Exists(_databaseFilePath))
                {
                    archive.CreateEntryFromFile(_databaseFilePath, "skinforge.db", CompressionLevel.Optimal);
                }

                // 2. Backup Metadata
                var metadataEntry = archive.CreateEntry("backup-info.txt");
                using var writer = new StreamWriter(metadataEntry.Open());
                writer.WriteLine($"SkinForge Backup");
                writer.WriteLine($"Created: {DateTime.UtcNow:O}");
                writer.WriteLine($"Version: 1.0.0");
            }, cancellationToken);

            Log.Information("Backup successfully created at '{Target}'.", targetFilePath);
            return OperationResult<string>.Success(targetFilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create backup at '{Target}'.", targetFilePath);
            return OperationResult<string>.Failure($"Backup creation failed: {ex.Message}");
        }
    }

    public async Task<OperationResult> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFilePath))
        {
            return OperationResult.Failure($"Backup file not found: {backupFilePath}");
        }

        try
        {
            // Step 1: Automatic pre-restore snapshot
            var autoBackupPath = Path.Combine(
                Path.GetDirectoryName(_databaseFilePath)!,
                $"skinforge_prerestore_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");

            if (File.Exists(_databaseFilePath))
            {
                File.Copy(_databaseFilePath, autoBackupPath, overwrite: true);
                Log.Information("Created automatic pre-restore safety snapshot at '{Path}'.", autoBackupPath);
            }

            // Step 2: Unpack archive to verify
            var tempDir = Path.Combine(Path.GetTempPath(), "SkinForge_Restore_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(backupFilePath, tempDir, overwriteFiles: true);

                var restoredDb = Path.Combine(tempDir, "skinforge.db");
                if (!File.Exists(restoredDb))
                {
                    return OperationResult.Failure("Invalid backup archive: missing 'skinforge.db'.");
                }

                // Overwrite active database
                File.Copy(restoredDb, _databaseFilePath, overwrite: true);
                Log.Information("Database restored successfully from '{Backup}'.", backupFilePath);

                return OperationResult.Success();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore backup from '{Backup}'.", backupFilePath);
            return OperationResult.Failure($"Restore failed: {ex.Message}");
        }
    }
}
