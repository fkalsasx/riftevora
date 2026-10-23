using System.Diagnostics;
using Serilog;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.Packaging;

namespace SkinForge.Application.Services;

public class ImportService : ISkinImporter
{
    private readonly ISkinRepository _repository;
    private readonly IAuditLogRepository? _auditLogRepository;
    private readonly ZipPackageExtractor _extractor;

    public ImportService(ISkinRepository repository, IAuditLogRepository? auditLogRepository = null)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
        _extractor = new ZipPackageExtractor();
    }

    public async Task<OperationResult<Skin>> ImportFromPackageAsync(
        string packageFilePath,
        string libraryDirectory,
        IProgress<(int percentage, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N")[..8];

        // 1. Extension validation
        var ext = Path.GetExtension(packageFilePath).ToLowerInvariant();
        if (ext != ".zip" && ext != ".fantome")
        {
            return OperationResult<Skin>.Failure(ApplicationError.PackageInvalid(
                $"Unsupported extension '{ext}'. Only .zip and .fantome packages are accepted.", "ImportPackage"));
        }

        if (!File.Exists(packageFilePath))
        {
            return OperationResult<Skin>.Failure(ApplicationError.AssetMissing(packageFilePath, "ImportPackage"));
        }

        var tempExtractDir = Path.Combine(Path.GetTempPath(), "SkinForge_Import_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            progress?.Report((10, "Reading package & verifying archive integrity..."));

            var extractResult = await _extractor.ExtractAndReadManifestAsync(packageFilePath, tempExtractDir, cancellationToken);
            if (!extractResult.IsSuccess || extractResult.Data.Manifest == null)
            {
                var err = extractResult.Error ?? ApplicationError.PackageInvalid(
                    extractResult.ErrorMessage ?? "Archive corrupt or invalid format.", "ImportPackage");
                return OperationResult<Skin>.Failure(err);
            }

            var manifest = extractResult.Data.Manifest;

            // 2. Duplicate Detection
            progress?.Report((35, "Checking library for duplicate packages..."));
            var existing = await _repository.GetByIdAsync(manifest.Id, cancellationToken);
            if (existing != null)
            {
                return OperationResult<Skin>.Failure(ApplicationError.DuplicatePackage(manifest.Id, "ImportPackage"));
            }

            // 3. Staging to permanent storage
            progress?.Report((60, "Staging files to library storage..."));
            var skinDestDir = Path.Combine(libraryDirectory, "skins", manifest.Id);
            if (Directory.Exists(skinDestDir))
            {
                Directory.Delete(skinDestDir, recursive: true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(skinDestDir)!);
            Directory.Move(tempExtractDir, skinDestDir);

            // 4. Calculate size and resolve preview
            progress?.Report((75, "Indexing assets and calculating hashes..."));
            var totalBytes = Directory.GetFiles(skinDestDir, "*.*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);

            string? previewFullPath = null;
            if (!string.IsNullOrEmpty(manifest.Preview))
            {
                var candidate = Path.Combine(skinDestDir, manifest.Preview);
                if (File.Exists(candidate))
                {
                    previewFullPath = candidate;
                }
            }

            // 5. Create Domain Entity
            progress?.Report((90, "Registering skin in database..."));
            var skin = new Skin(manifest.Id, manifest.Name, manifest.Champion)
            {
                SkinSlot = manifest.SkinSlot,
                Author = manifest.Author,
                Version = manifest.Version,
                Description = manifest.Description,
                GameVersion = manifest.GameVersion,
                Category = manifest.Category,
                FileSizeBytes = totalBytes,
                PreviewImagePath = previewFullPath,
                Screenshots = manifest.Screenshots.Select(s => Path.Combine(skinDestDir, s)).Where(File.Exists).ToList(),
                Tags = manifest.Tags,
                Assets = manifest.Assets,
                Dependencies = manifest.Dependencies,
                IsInstalled = true,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            };

            var dbResult = await _repository.AddAsync(skin, cancellationToken);
            if (!dbResult.IsSuccess)
            {
                // Rollback directory on database failure
                try { Directory.Delete(skinDestDir, recursive: true); } catch { /* ignore */ }
                return OperationResult<Skin>.Failure(ApplicationError.DatabaseError(
                    dbResult.ErrorMessage ?? "Database insertion failed.", "ImportPackage"));
            }

            sw.Stop();
            progress?.Report((100, "Skin imported successfully!"));
            Log.Information("Successfully imported skin '{SkinName}' ({SkinId}) in {Elapsed}ms.", skin.Name, skin.Id, sw.ElapsedMilliseconds);

            if (_auditLogRepository != null)
            {
                await _auditLogRepository.InsertAsync(AuditLogEntry.Create(
                    action: "Import",
                    entityId: skin.Id,
                    entityName: skin.Name,
                    status: "Success",
                    durationMs: sw.ElapsedMilliseconds,
                    correlationId: correlationId
                ), cancellationToken);
            }

            return OperationResult<Skin>.Success(skin);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Unexpected error during import of '{Package}'.", packageFilePath);

            if (_auditLogRepository != null)
            {
                await _auditLogRepository.InsertAsync(AuditLogEntry.Create(
                    action: "Import",
                    entityId: Path.GetFileNameWithoutExtension(packageFilePath),
                    entityName: Path.GetFileName(packageFilePath),
                    status: "Failed",
                    durationMs: sw.ElapsedMilliseconds,
                    correlationId: correlationId,
                    detailsJson: ex.Message
                ), CancellationToken.None);
            }

            return OperationResult<Skin>.Failure(ApplicationError.UnexpectedError(ex, "ImportPackage"));
        }
        finally
        {
            if (Directory.Exists(tempExtractDir))
            {
                try { Directory.Delete(tempExtractDir, recursive: true); } catch { /* ignore */ }
            }
        }
    }
}
