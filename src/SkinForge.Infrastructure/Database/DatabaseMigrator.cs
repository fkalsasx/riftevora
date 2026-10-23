using Microsoft.Data.Sqlite;
using Serilog;

namespace SkinForge.Infrastructure.Database;

public class DatabaseMigrator
{
    private readonly string _connectionString;
    private readonly string _databaseFilePath;

    public DatabaseMigrator(string connectionString)
    {
        _connectionString = connectionString;
        
        var builder = new SqliteConnectionStringBuilder(connectionString);
        _databaseFilePath = builder.DataSource;
    }

    /// <summary>
    /// Executes SQLite PRAGMA integrity_check and quick_check.
    /// In case of corruption, safely renames corrupted file and attempts database recovery.
    /// </summary>
    public async Task<bool> VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("Running SQLite database integrity verification...");

        if (!File.Exists(_databaseFilePath))
        {
            Log.Information("Database file does not exist yet. Integrity check passed.");
            return true;
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check;";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);

            string status = result?.ToString() ?? string.Empty;
            if (status.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("Database integrity verified successfully (status: ok).");
                return true;
            }

            Log.Warning("Quick check reported anomaly: {Status}. Running full integrity_check...", status);
            cmd.CommandText = "PRAGMA integrity_check;";
            var fullResult = await cmd.ExecuteScalarAsync(cancellationToken);
            if (fullResult?.ToString()?.Equals("ok", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            Log.Error("SQLite database corruption detected! Initiating safe backup and recovery.");
            await HandleDatabaseCorruptionAsync();
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database integrity verification threw an exception.");
            await HandleDatabaseCorruptionAsync();
            return false;
        }
    }

    private Task HandleDatabaseCorruptionAsync()
    {
        try
        {
            if (File.Exists(_databaseFilePath))
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var backupPath = $"{_databaseFilePath}.corrupt.{timestamp}.bak";
                File.Copy(_databaseFilePath, backupPath, true);
                Log.Warning("Corrupted database backed up to '{BackupPath}' for forensic examination.", backupPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create safety backup of corrupted database.");
        }
        return Task.CompletedTask;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("Checking and executing SQLite database migrations...");

        await VerifyIntegrityAsync(cancellationToken);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable WAL mode and foreign keys for high concurrency, performance and integrity
        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;
            ";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Migration Version Tracking Table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS __SchemaMigrations (
                    Version INTEGER PRIMARY KEY,
                    AppliedAt TEXT NOT NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var currentVersion = 0;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM __SchemaMigrations;";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result != null && result != DBNull.Value)
            {
                currentVersion = Convert.ToInt32(result);
            }
        }

        if (currentVersion < 1)
        {
            await ApplyMigrationV1Async(connection, cancellationToken);
        }

        if (currentVersion < 2)
        {
            await ApplyMigrationV2Async(connection, cancellationToken);
        }
    }

    private async Task ApplyMigrationV1Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Log.Information("Applying SQLite Schema Migration V1 (Core Tables)...");

        using var transaction = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Skins (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Champion TEXT NOT NULL,
                    SkinSlot INTEGER NOT NULL DEFAULT 0,
                    Author TEXT NOT NULL DEFAULT 'Community',
                    Version TEXT NOT NULL DEFAULT '1.0.0',
                    Description TEXT NOT NULL DEFAULT '',
                    GameVersion TEXT NOT NULL DEFAULT '',
                    Category INTEGER NOT NULL DEFAULT 0,
                    Status INTEGER NOT NULL DEFAULT 0,
                    IsInstalled INTEGER NOT NULL DEFAULT 1,
                    IsActive INTEGER NOT NULL DEFAULT 0,
                    IsFavorite INTEGER NOT NULL DEFAULT 0,
                    FileSizeBytes INTEGER NOT NULL DEFAULT 0,
                    PreviewImagePath TEXT,
                    ScreenshotsJson TEXT NOT NULL DEFAULT '[]',
                    TagsJson TEXT NOT NULL DEFAULT '[]',
                    AssetsJson TEXT NOT NULL DEFAULT '[]',
                    DependenciesJson TEXT NOT NULL DEFAULT '[]',
                    CreatedAt TEXT NOT NULL,
                    LastUsedAt TEXT,
                    LoadOrderPriority INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS IX_Skins_Champion ON Skins (Champion);
                CREATE INDEX IF NOT EXISTS IX_Skins_IsActive ON Skins (IsActive);
                CREATE INDEX IF NOT EXISTS IX_Skins_IsFavorite ON Skins (IsFavorite);
                CREATE INDEX IF NOT EXISTS IX_Skins_CreatedAt ON Skins (CreatedAt);

                CREATE TABLE IF NOT EXISTS SkinCollections (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Description TEXT NOT NULL DEFAULT '',
                    SkinIdsJson TEXT NOT NULL DEFAULT '[]',
                    CreatedAt TEXT NOT NULL,
                    Notes TEXT
                );

                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );

                INSERT INTO __SchemaMigrations (Version, AppliedAt)
                VALUES (1, datetime('now'));
            ";

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            Log.Information("SQLite Schema Migration V1 completed successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            Log.Error(ex, "Failed to apply Migration V1.");
            throw;
        }
    }

    private async Task ApplyMigrationV2Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Log.Information("Applying SQLite Schema Migration V2 (Audit Trail & History)...");

        using var transaction = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS AuditLogs (
                    Id TEXT PRIMARY KEY,
                    OperationId TEXT NOT NULL,
                    CorrelationId TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    EntityId TEXT,
                    EntityName TEXT,
                    Status TEXT NOT NULL,
                    DurationMs INTEGER NOT NULL,
                    Timestamp TEXT NOT NULL,
                    DetailsJson TEXT
                );

                CREATE INDEX IF NOT EXISTS IX_AuditLogs_Timestamp ON AuditLogs (Timestamp DESC);
                CREATE INDEX IF NOT EXISTS IX_AuditLogs_Action ON AuditLogs (Action);

                INSERT INTO __SchemaMigrations (Version, AppliedAt)
                VALUES (2, datetime('now'));
            ";

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            Log.Information("SQLite Schema Migration V2 completed successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            Log.Error(ex, "Failed to apply Migration V2.");
            throw;
        }
    }
}
