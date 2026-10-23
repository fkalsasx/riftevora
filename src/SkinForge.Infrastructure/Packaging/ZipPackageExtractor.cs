using System.IO.Compression;
using System.Text.Json;
using Serilog;
using SkinForge.Core.Enums;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.FileSystem;

namespace SkinForge.Infrastructure.Packaging;

public class ZipPackageExtractor
{
    private const long MaxUncompressedSizeBytes = 2L * 1024 * 1024 * 1024; // 2 GB limit (zip bomb defense)
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<OperationResult<(SkinManifest Manifest, string ExtractedDirectory)>> ExtractAndReadManifestAsync(
        string packagePath,
        string targetExtractDir,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            return OperationResult<(SkinManifest, string)>.Failure($"Package file does not exist: {packagePath}");
        }

        Directory.CreateDirectory(targetExtractDir);

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            long totalSize = 0;

            // Step 1: Pre-validation of all zip entries (Traversal & Zip-Bomb check)
            foreach (var entry in archive.Entries)
            {
                totalSize += entry.Length;
                if (totalSize > MaxUncompressedSizeBytes)
                {
                    return OperationResult<(SkinManifest, string)>.Failure(ApplicationError.PackageInvalid("Package uncompressed size exceeds maximum safety limit (2 GB)."));
                }

                if (!string.IsNullOrEmpty(entry.Name))
                {
                    if (!PathValidator.IsSafeRelativePath(entry.FullName, out var err))
                    {
                        return OperationResult<(SkinManifest, string)>.Failure(ApplicationError.PackageInvalid($"Suspicious or unsafe entry in package '{entry.FullName}': {err}"));
                    }
                }
            }

            // Step 2: Extract files safely
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var destinationPath = Path.Combine(targetExtractDir, entry.FullName);
                if (!PathValidator.IsPathInsideBaseDirectory(destinationPath, targetExtractDir))
                {
                    return OperationResult<(SkinManifest, string)>.Failure(ApplicationError.PackageInvalid($"Entry path escapes target directory: '{entry.FullName}'."));
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    // Directory entry
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }

            // Step 3: Locate or synthesize manifest.json
            var manifestPath = Path.Combine(targetExtractDir, "manifest.json");
            SkinManifest? manifest = null;

            if (File.Exists(manifestPath))
            {
                var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                manifest = JsonSerializer.Deserialize<SkinManifest>(json, JsonOptions);
            }
            else
            {
                // Fallback for legacy .fantome / .zip files that have META/info.json
                var fantomeInfoPath = Path.Combine(targetExtractDir, "META", "info.json");
                if (File.Exists(fantomeInfoPath))
                {
                    manifest = await SynthesizeManifestFromFantomeAsync(fantomeInfoPath, targetExtractDir, cancellationToken);
                }
                else
                {
                    manifest = SynthesizeManifestFromArchiveName(Path.GetFileNameWithoutExtension(packagePath), targetExtractDir);
                }
            }

            if (manifest == null)
            {
                return OperationResult<(SkinManifest, string)>.Failure("Failed to deserialize manifest.json from package.");
            }

            // Populate asset entries if empty
            if (manifest.Assets.Count == 0)
            {
                PopulateAssetsFromDirectory(manifest, targetExtractDir);
            }

            var validation = ManifestValidator.Validate(manifest);
            if (!validation.IsSuccess)
            {
                return OperationResult<(SkinManifest, string)>.Failure($"Manifest validation failed: {validation.ErrorMessage}");
            }

            return OperationResult<(SkinManifest, string)>.Success((manifest, targetExtractDir));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to extract package {PackagePath}.", packagePath);
            return OperationResult<(SkinManifest, string)>.Failure($"Failed to extract package: {ex.Message}");
        }
    }

    private static async Task<SkinManifest> SynthesizeManifestFromFantomeAsync(string infoJsonPath, string baseDir, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(infoJsonPath, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var name = root.TryGetProperty("Name", out var n) ? n.GetString() ?? "Custom Skin" : "Custom Skin";
        var author = root.TryGetProperty("Author", out var a) ? a.GetString() ?? "Community" : "Community";
        var version = root.TryGetProperty("Version", out var v) ? v.GetString() ?? "1.0.0" : "1.0.0";
        var champion = root.TryGetProperty("Champion", out var c) ? c.GetString() ?? "Unknown" : "Unknown";
        var desc = root.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";

        var id = $"{PathValidator.SanitizeFileName(champion).ToLowerInvariant()}-{PathValidator.SanitizeFileName(name).ToLowerInvariant()}-{Guid.NewGuid().ToString("N")[..6]}";

        var manifest = new SkinManifest
        {
            Id = id,
            Name = name,
            Author = author,
            Version = version,
            Champion = champion,
            Description = desc,
            Category = ModCategory.FullOverhaul
        };

        var previewPath = Path.Combine(baseDir, "META", "preview.png");
        if (File.Exists(previewPath))
        {
            manifest.Preview = "META/preview.png";
        }

        return manifest;
    }

    private static SkinManifest SynthesizeManifestFromArchiveName(string archiveName, string baseDir)
    {
        var cleanName = PathValidator.SanitizeFileName(archiveName);
        var id = $"mod-{cleanName.ToLowerInvariant()}-{Guid.NewGuid().ToString("N")[..6]}";

        return new SkinManifest
        {
            Id = id,
            Name = cleanName,
            Champion = "Custom",
            Author = "Community",
            Version = "1.0.0",
            Description = "Imported custom skin package.",
            Category = ModCategory.FullOverhaul
        };
    }

    private static void PopulateAssetsFromDirectory(SkinManifest manifest, string baseDir)
    {
        var files = Directory.GetFiles(baseDir, "*.*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
            if (rel.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("META/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (PathValidator.IsSafeRelativePath(rel, out _))
            {
                manifest.Assets.Add(new SkinAsset
                {
                    RelativePath = rel,
                    SizeBytes = new FileInfo(file).Length,
                    Layer = DetermineLayerFromPath(rel)
                });
            }
        }
    }

    private static LoadOrderLayer DetermineLayerFromPath(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains("/sounds/") || lower.EndsWith(".bnk") || lower.EndsWith(".wpk")) return LoadOrderLayer.Sfx;
        if (lower.Contains("/particles/") || lower.Contains("/vfx/")) return LoadOrderLayer.Vfx;
        if (lower.EndsWith(".skn") || lower.EndsWith(".skl")) return LoadOrderLayer.Model;
        if (lower.EndsWith(".dds") || lower.EndsWith(".tga")) return LoadOrderLayer.Texture;
        if (lower.Contains("/hud/") || lower.Contains("/ui/")) return LoadOrderLayer.Ui;
        return LoadOrderLayer.BaseSkin;
    }
}
