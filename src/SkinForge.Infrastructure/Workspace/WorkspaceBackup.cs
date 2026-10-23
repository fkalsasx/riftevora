using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkinForge.Infrastructure.Packaging;

namespace SkinForge.Infrastructure.Workspace;

public sealed record BackupManifest(string App, int Format, DateTime CreatedUtc, bool IncludesPackages, int PackageCount, string WorkspaceSha256);

public sealed record RestoreSummary(WorkspaceData Workspace, int PackagesRestored, int PackagesSkipped);

/// <summary>Portable zip of settings, favorites, collections and optionally the mod library.</summary>
public static class WorkspaceBackup
{
    private const string Marker = "SkinForge";
    private const int FormatVersion = 1;
    private static readonly string[] PackageExtensions = [".fantome", ".modpkg"];

    public static async Task<BackupManifest> CreateAsync(string zipPath, WorkspaceStore store, string packagesDir, bool includePackages, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        store.Flush();
        var workspaceBytes = await File.ReadAllBytesAsync(store.FilePath, ct);
        var temp = zipPath + ".tmp";
        var files = includePackages && Directory.Exists(packagesDir)
            ? Directory.EnumerateFiles(packagesDir).Where(IsLibraryFile).ToList() : [];
        var packageCount = files.Count(f => PackageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        var manifest = new BackupManifest(Marker, FormatVersion, DateTime.UtcNow, includePackages, packageCount, Hex(SHA256.HashData(workspaceBytes)));
        try
        {
            await using (var output = File.Create(temp))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                await WriteEntry(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest), ct);
                await WriteEntry(zip, "workspace.json", workspaceBytes, ct);
                for (int i = 0; i < files.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = zip.CreateEntry("Packages/" + Path.GetFileName(files[i]), CompressionLevel.NoCompression);
                    await using var entryStream = entry.Open();
                    await using var input = File.OpenRead(files[i]);
                    await input.CopyToAsync(entryStream, ct);
                    progress?.Report((i + 1d) / files.Count);
                }
            }
            File.Move(temp, zipPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return manifest;
    }

    public static async Task<BackupManifest> ReadManifestAsync(string zipPath, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Bu dosya bir SkinForge yedeği değil.");
        if (entry.Length > 65536) throw new InvalidDataException("Yedek bilgisi geçersiz.");
        await using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: ct);
        if (manifest is not { App: Marker } || manifest.Format > FormatVersion) throw new InvalidDataException("Bu yedek desteklenmeyen bir sürümden.");
        return manifest;
    }

    /// <summary>Validates everything before it touches disk: names, hashes and the workspace document.</summary>
    public static async Task<RestoreSummary> RestoreAsync(string zipPath, string packagesDir, bool restorePackages, CancellationToken ct = default)
    {
        var manifest = await ReadManifestAsync(zipPath, ct);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains("..")) throw new InvalidDataException("Yedek güvensiz dosya yolları içeriyor.");
        }
        var workspaceEntry = zip.GetEntry("workspace.json") ?? throw new InvalidDataException("Yedekte çalışma alanı dosyası yok.");
        if (workspaceEntry.Length > 32 * 1024 * 1024) throw new InvalidDataException("Çalışma alanı dosyası çok büyük.");
        byte[] workspaceBytes;
        await using (var stream = workspaceEntry.Open()) { using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ct); workspaceBytes = buffer.ToArray(); }
        if (Hex(SHA256.HashData(workspaceBytes)) != manifest.WorkspaceSha256) throw new InvalidDataException("Yedek bozulmuş: çalışma alanı özeti uyuşmuyor.");
        var workspace = JsonSerializer.Deserialize<WorkspaceData>(workspaceBytes) ?? throw new InvalidDataException("Çalışma alanı okunamadı.");

        int restored = 0, skipped = 0;
        if (restorePackages && manifest.IncludesPackages)
        {
            Directory.CreateDirectory(packagesDir);
            var root = Path.GetFullPath(packagesDir);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) && e.Name.Length > 0))
            {
                ct.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                var id = Path.GetFileNameWithoutExtension(entry.Name).ToLowerInvariant();
                if (id.Length != 64 || id.Any(c => !Uri.IsHexDigit(c))) { skipped++; continue; }
                if (!PackageExtensions.Contains(extension)) continue; // index and preview files are rebuilt below
                var destination = Path.Combine(root, id + extension);
                if (File.Exists(destination)) { skipped++; continue; }
                var temp = destination + ".tmp";
                try
                {
                    await using (var input = entry.Open()) await using (var output = File.Create(temp)) await input.CopyToAsync(output, ct);
                    await using (var check = File.OpenRead(temp))
                        if (Hex(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant() != id) { skipped++; continue; }
                    File.Move(temp, destination, true);
                    await RebuildIndexAsync(zip, root, id, destination, ct);
                    restored++;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        return new RestoreSummary(workspace, restored, skipped);
    }

    // The saved index stores absolute paths from the machine that made the backup; point them at this machine.
    private static async Task RebuildIndexAsync(ZipArchive zip, string root, string id, string packagePath, CancellationToken ct)
    {
        var indexEntry = zip.GetEntry($"Packages/{id}.json");
        if (indexEntry == null || indexEntry.Length > 1048576) return;
        LocalPackage? original;
        await using (var stream = indexEntry.Open()) original = await JsonSerializer.DeserializeAsync<LocalPackage>(stream, cancellationToken: ct);
        if (original == null) return;
        string? preview = null;
        var previewEntry = zip.GetEntry($"Packages/{id}.png");
        if (previewEntry is { Length: > 0 and < 8388608 })
        {
            preview = Path.Combine(root, id + ".png");
            await using var input = previewEntry.Open(); await using var output = File.Create(preview);
            await input.CopyToAsync(output, ct);
        }
        var rebuilt = original with { FilePath = packagePath, PreviewPath = preview };
        var index = Path.Combine(root, id + ".json");
        await File.WriteAllTextAsync(index + ".tmp", JsonSerializer.Serialize(rebuilt), ct);
        File.Move(index + ".tmp", index, true);
    }

    private static bool IsLibraryFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".fantome" or ".modpkg" or ".json" or ".png";
    private static async Task WriteEntry(ZipArchive zip, string name, byte[] bytes, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, ct);
    }
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
}
