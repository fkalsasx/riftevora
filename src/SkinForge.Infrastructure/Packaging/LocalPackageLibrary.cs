using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SkinForge.Infrastructure.Packaging;

public enum ImportStage { Reading, Validating, Scanning, Analyzing, Indexing, Ready }

public record LocalPackage(string Id, string Name, string Author, string Version, string FilePath, string? PreviewPath, long SizeBytes, DateTime? ImportedUtc = null)
{
    public string SizeLabel => SizeBytes switch { >= 1L << 30 => $"{SizeBytes / (double)(1L << 30):0.0} GB", >= 1L << 20 => $"{SizeBytes / 1048576d:0.0} MB", >= 1L << 10 => $"{SizeBytes / 1024d:0} KB", _ => $"{SizeBytes} B" };
    public string Format => Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant();
}

/// <summary>Keeps original packages intact; never extracts or executes game assets.</summary>
public sealed class LocalPackageLibrary
{
    private readonly string _root;
    public LocalPackageLibrary(string root) { _root = Path.GetFullPath(root); Directory.CreateDirectory(_root); }
    public string Root => _root;
    public async Task<IReadOnlyList<LocalPackage>> LoadAsync()
    {
        var packages = new List<LocalPackage>();
        foreach (var metadata in Directory.EnumerateFiles(_root, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize<LocalPackage>(await File.ReadAllTextAsync(metadata));
                if (item != null && IsInside(item.FilePath) && File.Exists(item.FilePath)) packages.Add(item);
            }
            catch (Exception ex) when (ex is JsonException or IOException) { }
        }
        return packages.OrderBy(p => p.Name).ToArray();
    }

    public async Task<LocalPackage> ImportAsync(string path, CancellationToken ct = default, IProgress<ImportStage>? progress = null)
    {
        progress?.Report(ImportStage.Reading);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".fantome" or ".modpkg")) throw new InvalidDataException("Yalnızca .fantome ve .modpkg paketleri desteklenir.");
        var info = new FileInfo(path);
        if (info.Length < 16 || info.Length > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Paket boş, geçersiz veya 2 GB sınırını aşıyor.");
        string name = Path.GetFileNameWithoutExtension(path), author = "Belirtilmemiş", version = "";
        byte[]? preview = null;
        // Lock the source while validating, hashing and copying the same bytes.
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        progress?.Report(ImportStage.Validating);
        if (ext == ".fantome")
        {
            using var zip = new ZipArchive(source, ZipArchiveMode.Read, true);
            if (zip.Entries.Count > 100000) throw new InvalidDataException("Paket çok fazla dosya içeriyor.");
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                var normalized = entry.FullName.Replace('\\', '/');
                if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Contains(".."))
                    throw new InvalidDataException("Paket güvensiz dosya yolları içeriyor.");
                expanded = checked(expanded + entry.Length);
                if (expanded > 8L * 1024 * 1024 * 1024) throw new InvalidDataException("Paketin açılmış boyutu çok büyük.");
                if (new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".com", ".scr", ".msi" }.Contains(Path.GetExtension(normalized).ToLowerInvariant()))
                    throw new InvalidDataException("Paket çalıştırılabilir dosya içeriyor.");
            }
            progress?.Report(ImportStage.Analyzing);
            var manifest = zip.Entries.FirstOrDefault(e => e.FullName.Equals("META/info.json", StringComparison.OrdinalIgnoreCase));
            if (manifest == null || manifest.Length > 524288) throw new InvalidDataException("Fantome META/info.json bilgisi eksik veya çok büyük.");
            using (var stream = manifest.Open())
            using (var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct))
            {
                string Read(string field, string fallback) => doc.RootElement.EnumerateObject()
                    .FirstOrDefault(p => p.Name.Equals(field, StringComparison.OrdinalIgnoreCase)).Value is var value
                    && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
                name = Read("Name", name); author = Read("Author", author); version = Read("Version", version);
            }
            var image = zip.Entries.FirstOrDefault(e => e.FullName.Equals("META/image.png", StringComparison.OrdinalIgnoreCase));
            if (image is { Length: > 0 and < 8388608 })
            {
                using var buffer = new MemoryStream();
                using var stream = image.Open();
                await stream.CopyToAsync(buffer, ct);
                preview = buffer.ToArray();
            }
        }
        // MODPKG is a binary format: LTK performs its authoritative validation.
        progress?.Report(ImportStage.Scanning);
        source.Position = 0;
        var id = Convert.ToHexString(await SHA256.HashDataAsync(source, ct)).ToLowerInvariant();
        var dest = Path.Combine(_root, id + ext);
        source.Position = 0;
        if (!File.Exists(dest))
        {
            var temp = dest + ".tmp";
            try
            {
                await using (var output = File.Create(temp)) await source.CopyToAsync(output, ct);
                File.Move(temp, dest, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        string? imagePath = null;
        if (preview != null) { imagePath = Path.Combine(_root, id + ".png"); await File.WriteAllBytesAsync(imagePath, preview, ct); }
        progress?.Report(ImportStage.Indexing);
        var package = new LocalPackage(id, name, author, version, dest, imagePath, info.Length, DateTime.UtcNow);
        var index = Path.Combine(_root, id + ".json");
        await File.WriteAllTextAsync(index + ".tmp", JsonSerializer.Serialize(package), ct);
        File.Move(index + ".tmp", index, true);
        progress?.Report(ImportStage.Ready);
        return package;
    }

    /// <summary>Deletes only the library's own copy; the user's original file is never touched.</summary>
    public Task RemoveAsync(LocalPackage package)
    {
        foreach (var file in new[] { package.FilePath, package.PreviewPath, Path.Combine(_root, package.Id + ".json") })
        {
            if (string.IsNullOrEmpty(file) || !IsInside(file) || !File.Exists(file)) continue;
            File.Delete(file);
        }
        return Task.CompletedTask;
    }

    public async Task VerifyAsync(LocalPackage package, CancellationToken ct = default)
    {
        if (!IsInside(package.FilePath)) throw new InvalidDataException("Paket kütüphane klasörünün dışında.");
        await using var stream = File.OpenRead(package.FilePath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        if (hash != package.Id) throw new InvalidDataException("Paket dosyası içe aktarıldıktan sonra değişmiş. Yeniden içe aktarın.");
    }
    private bool IsInside(string path) => Path.GetFullPath(path).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
