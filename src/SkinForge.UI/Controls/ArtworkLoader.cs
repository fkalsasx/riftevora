using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using SkinForge.Infrastructure.SystemInfo;

namespace SkinForge.UI.Controls;

/// <summary>
/// One place that turns an artwork URL into a decoded bitmap. Order of preference: memory, bundled portrait,
/// disk cache, network. Never blocks the UI thread, never waits on a dead network for long, never throws.
/// </summary>
public static class ArtworkLoader
{
    private const long MemoryBudgetBytes = 110L * 1024 * 1024;
    private const long MaxDownloadBytes = 16L * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly SemaphoreSlim Downloads = new(6);
    private static readonly ConcurrentDictionary<string, Task<BitmapSource?>> InFlight = new();
    private static readonly Dictionary<string, LinkedListNode<Entry>> Index = new();
    private static readonly LinkedList<Entry> Lru = new();
    private static readonly object CacheGate = new();
    private static readonly ConcurrentDictionary<string, DateTime> Failures = new();
    private static long _bytes;
    private static int _consecutiveNetworkFailures;
    private static long _offlineUntilTicks;

    public static string CacheDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkinForge", "Artwork");
    public static string BundledDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Resources", "Artwork");

    private sealed record Entry(string Key, BitmapSource Image, long Bytes);

    public static bool TryGetCached(string url, int width, out BitmapSource? image)
    {
        var key = Key(url, width);
        lock (CacheGate)
        {
            if (Index.TryGetValue(key, out var node)) { Lru.Remove(node); Lru.AddFirst(node); image = node.Value.Image; return true; }
        }
        image = null;
        return false;
    }

    public static Task<BitmapSource?> GetAsync(string url, int width, CancellationToken ct = default)
    {
        var key = Key(url, width);
        if (TryGetCached(url, width, out var cached)) return Task.FromResult(cached);
        if (Failures.TryGetValue(key, out var until) && until > DateTime.UtcNow) return Task.FromResult<BitmapSource?>(null);
        // Share one load between every card that asks for the same picture; callers cancel by ignoring the result.
        return InFlight.GetOrAdd(key, _ => Task.Run(async () =>
        {
            try
            {
                var image = await LoadAsync(url, width);
                if (image != null) Remember(key, image); else Failures[key] = DateTime.UtcNow.AddSeconds(25);
                return image;
            }
            finally { InFlight.TryRemove(key, out Task<BitmapSource?>? _); }
        }));
    }

    public static void RetryFailures() => Failures.Clear();

    private static async Task<BitmapSource?> LoadAsync(string url, int width)
    {
        try
        {
            var path = await ResolveAsync(url);
            if (path == null && url.Contains("/splash/", StringComparison.Ordinal))
                path = await ResolveAsync(url.Replace("/splash/", "/loading/", StringComparison.Ordinal)); // offline: the bundled portrait is better than a hole
            return path == null ? null : Decode(path, width);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or HttpRequestException or TaskCanceledException or FileFormatException)
        {
            return null;
        }
    }

    private static async Task<string?> ResolveAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.IsFile) return File.Exists(uri.LocalPath) ? uri.LocalPath : null;
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "ddragon.leagueoflegends.com") return null;

        var name = Path.GetFileName(uri.LocalPath);
        // Bundled portraits are "loading" art; they never need the network.
        if (uri.LocalPath.Contains("/loading/", StringComparison.Ordinal))
        {
            var bundled = Path.Combine(BundledDirectory, name);
            if (File.Exists(bundled)) return bundled;
        }
        Directory.CreateDirectory(CacheDirectory);
        var file = Path.Combine(CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
        if (File.Exists(file)) { TryTouch(file); return file; }
        if (NetworkPolicy.Offline || Environment.TickCount64 < Interlocked.Read(ref _offlineUntilTicks)) return null;

        await Downloads.WaitAsync();
        try
        {
            if (File.Exists(file)) return file;
            if (Environment.TickCount64 < Interlocked.Read(ref _offlineUntilTicks)) return null;
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxDownloadBytes) return null;
            await using var input = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + count > MaxDownloadBytes) return null;
                buffer.Write(chunk, 0, count);
            }
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, buffer.ToArray());
            File.Move(temp, file, true);
            Interlocked.Exchange(ref _consecutiveNetworkFailures, 0);
            return file;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Three failures in a row means we are offline: stop queueing doomed requests for a while.
            if (Interlocked.Increment(ref _consecutiveNetworkFailures) >= 3)
                Interlocked.Exchange(ref _offlineUntilTicks, Environment.TickCount64 + 15_000);
            return null;
        }
        finally { Downloads.Release(); }
    }

    private static BitmapSource Decode(string path, int width)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = Math.Clamp(width, 64, 1800);
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void Remember(string key, BitmapSource image)
    {
        var bytes = (long)image.PixelWidth * image.PixelHeight * 4;
        lock (CacheGate)
        {
            if (Index.ContainsKey(key)) return;
            Index[key] = Lru.AddFirst(new Entry(key, image, bytes));
            _bytes += bytes;
            while (_bytes > MemoryBudgetBytes && Lru.Last != null)
            {
                var last = Lru.Last;
                Lru.RemoveLast();
                Index.Remove(last.Value.Key);
                _bytes -= last.Value.Bytes;
            }
        }
    }

    private static string Key(string url, int width) => width + "|" + url;
    private static void TryTouch(string file) { try { File.SetLastAccessTimeUtc(file, DateTime.UtcNow); } catch (IOException) { } }

    public static long DiskCacheBytes()
    {
        try { return Directory.Exists(CacheDirectory) ? new DirectoryInfo(CacheDirectory).EnumerateFiles().Sum(f => f.Length) : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    public static int ClearDiskCache()
    {
        int removed = 0;
        try
        {
            if (!Directory.Exists(CacheDirectory)) return 0;
            foreach (var file in Directory.EnumerateFiles(CacheDirectory))
            {
                try { File.Delete(file); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        lock (CacheGate) { Index.Clear(); Lru.Clear(); _bytes = 0; }
        Failures.Clear();
        return removed;
    }

    /// <summary>Old splash downloads are cheap to fetch again; keep the folder from growing without bound.</summary>
    public static void PruneOlderThan(TimeSpan age)
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;
            var limit = DateTime.UtcNow - age;
            foreach (var file in new DirectoryInfo(CacheDirectory).EnumerateFiles())
            {
                if (file.LastAccessTimeUtc < limit && file.LastWriteTimeUtc < limit) { try { file.Delete(); } catch (IOException) { } }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
