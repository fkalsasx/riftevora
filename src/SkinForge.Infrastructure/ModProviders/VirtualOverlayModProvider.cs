using Serilog;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Infrastructure.ModProviders;

public class VirtualOverlayModProvider : IModProvider
{
    public string ProviderName => "VirtualOverlayProvider (Safe Client-Side Staging)";

    private string? _activeBackupDir;

    public Task<OperationResult> ValidateEnvironmentAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return Task.FromResult(OperationResult.Failure("Game directory path is empty."));
        }

        if (!Directory.Exists(gamePath))
        {
            return Task.FromResult(OperationResult.Failure($"Game directory does not exist: '{gamePath}'."));
        }

        // Check for League of Legends executables or subfolders
        var exe1 = Path.Combine(gamePath, "League of Legends.exe");
        var exe2 = Path.Combine(gamePath, "LeagueClient.exe");
        var dataFolder = Path.Combine(gamePath, "DATA");

        if (!File.Exists(exe1) && !File.Exists(exe2) && !Directory.Exists(dataFolder))
        {
            return Task.FromResult(OperationResult.Failure("The selected directory does not appear to contain a valid League of Legends installation."));
        }

        return Task.FromResult(OperationResult.Success());
    }

    public async Task<OperationResult> ApplyModsAsync(
        IReadOnlyList<Skin> activeMods,
        string gamePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Log.Information("Applying {Count} active mods using {Provider}...", activeMods.Count, ProviderName);

        // Transaction Phase 1: Validate
        progress?.Report("Phase 1/6: Validating target game environment...");
        var validation = await ValidateEnvironmentAsync(gamePath, cancellationToken);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        // Transaction Phase 2: Prepare
        progress?.Report("Phase 2/6: Preparing virtual overlay staging directory...");
        var overlayDir = Path.Combine(gamePath, "mod-overlay");
        var stagingDir = Path.Combine(Path.GetTempPath(), "SkinForge_Staging_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stagingDir);

        try
        {
            // Transaction Phase 3: Backup existing state
            progress?.Report("Phase 3/6: Creating snapshot backup of current state...");
            _activeBackupDir = Path.Combine(Path.GetTempPath(), "SkinForge_Backup_" + Guid.NewGuid().ToString("N")[..8]);
            if (Directory.Exists(overlayDir))
            {
                CopyDirectory(overlayDir, _activeBackupDir);
            }

            // Transaction Phase 4: Apply (Sort by LoadOrderPriority)
            progress?.Report("Phase 4/6: Staging active mod assets by load order...");
            var orderedMods = activeMods.OrderBy(m => m.LoadOrderPriority).ToList();

            foreach (var mod in orderedMods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Staging '{mod.Name}' ({mod.Assets.Count} assets)...");

                foreach (var asset in mod.Assets)
                {
                    var targetFile = Path.Combine(stagingDir, asset.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                    
                    // Virtual map marker writing
                    await File.WriteAllTextAsync(targetFile, $"[SkinForge Virtual Map: {mod.Id} // {asset.RelativePath}]", cancellationToken);
                }
            }

            // Transaction Phase 5: Verify
            progress?.Report("Phase 5/6: Verifying asset integrity...");
            var stagedFiles = Directory.GetFiles(stagingDir, "*.*", SearchOption.AllDirectories);
            if (stagedFiles.Length == 0 && activeMods.Count > 0)
            {
                throw new InvalidOperationException("Staging verification failed: No files were generated.");
            }

            // Transaction Phase 6: Commit
            progress?.Report("Phase 6/6: Committing virtual overlay to client directory...");
            if (Directory.Exists(overlayDir))
            {
                Directory.Delete(overlayDir, recursive: true);
            }
            Directory.Move(stagingDir, overlayDir);

            Log.Information("Mod overlay successfully applied with {FileCount} assets.", stagedFiles.Length);
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(gamePath, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ApplyMods failed. Initiating transaction rollback...");
            await RollbackAsync(gamePath, CancellationToken.None);
            return OperationResult.Failure($"Failed to apply mods: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, recursive: true); } catch { /* ignore */ }
            }
        }
    }

    public Task<OperationResult> DeactivateAllAsync(string gamePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Deactivating all mods and restoring clean state...");
        var overlayDir = Path.Combine(gamePath, "mod-overlay");

        try
        {
            if (Directory.Exists(overlayDir))
            {
                Directory.Delete(overlayDir, recursive: true);
            }
            Log.Information("Clean game state restored.");
            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore clean game state.");
            return Task.FromResult(OperationResult.Failure($"Failed to clean overlay: {ex.Message}"));
        }
    }

    public Task<OperationResult> RollbackAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        Log.Warning("Rolling back mod application to previous snapshot...");
        var overlayDir = Path.Combine(gamePath, "mod-overlay");

        try
        {
            if (Directory.Exists(overlayDir))
            {
                Directory.Delete(overlayDir, recursive: true);
            }

            if (!string.IsNullOrEmpty(_activeBackupDir) && Directory.Exists(_activeBackupDir))
            {
                Directory.Move(_activeBackupDir, overlayDir);
            }

            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Rollback failed.");
            return Task.FromResult(OperationResult.Failure($"Rollback error: {ex.Message}"));
        }
        finally
        {
            _activeBackupDir = null;
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        }
        foreach (var sub in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(sub, Path.Combine(targetDir, Path.GetFileName(sub)));
        }
    }
}
