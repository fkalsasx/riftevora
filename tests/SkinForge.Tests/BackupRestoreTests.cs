using SkinForge.Application.Services;
using Xunit;

namespace SkinForge.Tests;

public class BackupRestoreTests
{
    [Fact]
    public async Task CreateAndRestoreBackup_ShouldPackageAndRestore_DatabaseFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SkinForge_Test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbFile = Path.Combine(tempDir, "test_skinforge.db");
            var libraryDir = Path.Combine(tempDir, "library");
            var backupZip = Path.Combine(tempDir, "test_backup.zip");

            Directory.CreateDirectory(libraryDir);
            await File.WriteAllTextAsync(dbFile, "MOCK_SQLITE_DATABASE_DATA_V1");

            var service = new BackupRestoreService(dbFile, libraryDir);

            // Create Backup
            var createRes = await service.CreateBackupAsync(backupZip);
            Assert.True(createRes.IsSuccess);
            Assert.True(File.Exists(backupZip));

            // Mutate db to test restore
            await File.WriteAllTextAsync(dbFile, "CORRUPTED_DATA");

            // Restore Backup
            var restoreRes = await service.RestoreBackupAsync(backupZip);
            Assert.True(restoreRes.IsSuccess);

            var restoredContent = await File.ReadAllTextAsync(dbFile);
            Assert.Equal("MOCK_SQLITE_DATABASE_DATA_V1", restoredContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
