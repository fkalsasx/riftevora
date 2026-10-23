using System.Net.Http;
using System.Runtime.InteropServices;
using SkinForge.Infrastructure.Packaging;
using SkinForge.Infrastructure.SystemInfo;

namespace SkinForge.Infrastructure.Workspace;

public enum HealthStatus { Ok, Info, Warning, Fail }

/// <param name="Id">Stable key the UI translates. <paramref name="Args"/> fill that translation.</param>
public sealed record HealthItem(string Id, HealthStatus Status, string[] Args, string? FixId = null);

public sealed record HealthInputs(
    string DataRoot,
    LocalPackageLibrary Library,
    string? LtkPath,
    string? LtkVersion,
    GameInstallation? Game,
    string CatalogState,
    string CatalogPatch,
    int ChampionCount,
    int SkinCount,
    string ArtworkCacheDir,
    string BundledArtworkDir,
    string LogDir,
    bool Deep = true);

/// <summary>Real checks against the real machine. Nothing here is decorative.</summary>
public static class HealthCheckRunner
{
    public static async IAsyncEnumerable<HealthItem> RunAsync(HealthInputs i, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return WritableCheck(i.DataRoot);
        yield return LinkCheck(i);
        yield return DiskCheck(i.DataRoot);

        yield return i.LtkPath != null && File.Exists(i.LtkPath)
            ? new("ltk", HealthStatus.Ok, [i.LtkVersion ?? "?", i.LtkPath])
            : new("ltk", HealthStatus.Fail, [], "choose-ltk");

        yield return i.Game != null
            ? new("game", HealthStatus.Ok, [i.Game.GameVersion ?? "?", i.Game.RootDirectory])
            : new("game", HealthStatus.Warning, [], "choose-game");

        yield return i.Game?.RiotClientPath != null ? new("riot-client", HealthStatus.Ok, [i.Game.RiotClientPath]) : new("riot-client", HealthStatus.Info, []);
        yield return GameLocator.IsGameRunning() ? new("game-running", HealthStatus.Info, []) : new("game-running", HealthStatus.Ok, []);

        yield return i.CatalogState is "Error" or "Empty"
            ? new("catalog", HealthStatus.Fail, [i.CatalogState], "refresh-catalog")
            : new("catalog", i.CatalogState == "Offline" ? HealthStatus.Warning : HealthStatus.Ok, [i.CatalogPatch, i.ChampionCount.ToString(), i.SkinCount.ToString()], i.CatalogState == "Offline" ? "refresh-catalog" : null);

        yield return BundledArtCheck(i);
        yield return NetworkPolicy.Offline ? new HealthItem("online", HealthStatus.Info, []) : await OnlineCheck(ct);
        yield return await Task.Run(() => CacheCheck(i), ct);
        yield return await Task.Run(() => new HealthItem("logs", HealthStatus.Info, [FormatBytes(DirectorySize(i.LogDir))], "open-logs"), ct);
        yield return new("runtime", HealthStatus.Info, [RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, Environment.Is64BitOperatingSystem ? "x64" : "x86"]);
        yield return await PackageCheck(i.Library, i.Deep, ct);
    }

    private static HealthItem WritableCheck(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok"); File.Delete(probe);
            return new("data-writable", HealthStatus.Ok, [root], "open-data");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new("data-writable", HealthStatus.Fail, [root, ex.Message], "open-data"); }
    }

    private static HealthItem LinkCheck(HealthInputs i)
    {
        try
        {
            foreach (var path in new[] { i.DataRoot, i.Library.Root })
                if (Directory.Exists(path) && new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return new("data-link", HealthStatus.Info, [path]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new("data-link", HealthStatus.Ok, []);
    }

    private static HealthItem DiskCheck(string root)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
            var free = drive.AvailableFreeSpace;
            var status = free < 200L * 1024 * 1024 ? HealthStatus.Fail : free < 1024L * 1024 * 1024 ? HealthStatus.Warning : HealthStatus.Ok;
            return new("disk", status, [FormatBytes(free), drive.Name]);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return new("disk", HealthStatus.Info, []); }
    }

    private static HealthItem BundledArtCheck(HealthInputs i)
    {
        int count = Directory.Exists(i.BundledArtworkDir) ? Directory.EnumerateFiles(i.BundledArtworkDir, "*_0.jpg").Count() : 0;
        return count >= i.ChampionCount && count > 0 ? new("bundled-art", HealthStatus.Ok, [count.ToString()]) : new("bundled-art", HealthStatus.Warning, [count.ToString(), i.ChampionCount.ToString()]);
    }

    private static async Task<HealthItem> OnlineCheck(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://ddragon.leagueoflegends.com/api/versions.json");
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? new("online", HealthStatus.Ok, []) : new("online", HealthStatus.Warning, [((int)response.StatusCode).ToString()]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return new("online", HealthStatus.Warning, [ex.Message]); }
    }

    private static HealthItem CacheCheck(HealthInputs i)
    {
        var size = DirectorySize(i.ArtworkCacheDir);
        return new("art-cache", size > 2L * 1024 * 1024 * 1024 ? HealthStatus.Warning : HealthStatus.Info, [FormatBytes(size)], "clear-image-cache");
    }

    /// <summary>Quick mode only confirms every package file still has its recorded size; deep mode re-hashes the bytes.</summary>
    private static async Task<HealthItem> PackageCheck(LocalPackageLibrary library, bool deep, CancellationToken ct)
    {
        var packages = await library.LoadAsync();
        var broken = new List<string>();
        foreach (var package in packages)
        {
            try
            {
                if (deep) await library.VerifyAsync(package, ct);
                else if (new FileInfo(package.FilePath).Length != package.SizeBytes) broken.Add(package.Name);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { broken.Add(package.Name); }
        }
        return broken.Count == 0 ? new("packages", HealthStatus.Ok, [packages.Count.ToString()]) : new("packages", HealthStatus.Fail, [string.Join(", ", broken.Take(4)), broken.Count.ToString()]);
    }

    public static long DirectorySize(string path)
    {
        try { return Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B"
    };
}
