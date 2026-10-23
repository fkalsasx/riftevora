using System.IO.Compression;

namespace SkinForge.Infrastructure.Packaging;

/// <summary>What a Fantome package touches, read from its archive index only. Nothing is extracted.</summary>
public sealed record PackageInspection(
    IReadOnlyList<string> WadNames,
    IReadOnlyList<string> Champions,
    IReadOnlySet<string> EntryPaths,
    int EntryCount,
    long UncompressedBytes,
    bool CanInspect)
{
    public static PackageInspection Opaque { get; } = new([], [], new HashSet<string>(), 0, 0, false);
}

public enum ConflictSeverity { None, SameWad, SharedFiles }

public sealed record PackageConflict(
    LocalPackage First,
    LocalPackage Second,
    ConflictSeverity Severity,
    IReadOnlyList<string> SharedWads,
    int SharedFileCount,
    IReadOnlyList<string> SampleFiles);

public static class PackageInspector
{
    public static async Task<PackageInspection> InspectAsync(LocalPackage package, CancellationToken ct = default)
    {
        if (!package.FilePath.EndsWith(".fantome", StringComparison.OrdinalIgnoreCase) || !File.Exists(package.FilePath))
            return PackageInspection.Opaque;
        return await Task.Run(() => Inspect(package.FilePath, ct), ct);
    }

    internal static PackageInspection Inspect(string path, CancellationToken ct = default)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var wads = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var champions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long bytes = 0;
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/')) continue;
                bytes += entry.Length;
                var parts = name.Split('/');
                if (parts.Length >= 2 && parts[0].Equals("WAD", StringComparison.OrdinalIgnoreCase))
                {
                    wads.Add(parts[1]);
                    if (parts.Length > 2) paths.Add(name.ToLowerInvariant());
                    var wad = parts[1];
                    var dot = wad.IndexOf(".wad", StringComparison.OrdinalIgnoreCase);
                    if (dot > 0) champions.Add(wad[..dot]);
                }
                else if (parts.Length >= 2 && parts[0].Equals("RAW", StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(name.ToLowerInvariant());
                    var index = Array.FindIndex(parts, p => p.Equals("characters", StringComparison.OrdinalIgnoreCase));
                    if (index >= 0 && index + 1 < parts.Length - 1) champions.Add(parts[index + 1]);
                }
            }
            return new PackageInspection(wads.ToArray(), champions.ToArray(), paths, zip.Entries.Count, bytes, true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return PackageInspection.Opaque;
        }
    }

    /// <summary>Two packages conflict when LTK would have them rewrite the same game file.</summary>
    public static PackageConflict? Compare(LocalPackage first, PackageInspection a, LocalPackage second, PackageInspection b)
    {
        if (!a.CanInspect || !b.CanInspect) return null;
        var shared = a.EntryPaths.Count <= b.EntryPaths.Count ? a.EntryPaths.Where(b.EntryPaths.Contains).ToList() : b.EntryPaths.Where(a.EntryPaths.Contains).ToList();
        if (shared.Count > 0)
        {
            var wads = shared.Select(p => p.Split('/')[1]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new PackageConflict(first, second, ConflictSeverity.SharedFiles, wads, shared.Count, shared.Take(6).ToArray());
        }
        var sameWads = a.WadNames.Intersect(b.WadNames, StringComparer.OrdinalIgnoreCase).ToArray();
        return sameWads.Length > 0 ? new PackageConflict(first, second, ConflictSeverity.SameWad, sameWads, 0, []) : null;
    }
}
