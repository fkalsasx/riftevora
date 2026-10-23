using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using SkinForge.Infrastructure.SystemInfo;

namespace SkinForge.Infrastructure.ModProviders;

public sealed record LtkSetupProgress(string Stage, double Percent = 0);
public sealed record LtkInstallerAsset(string Name, Uri Url, long Size, string Sha256);
public sealed class LtkSetupException(string key, Exception? inner = null) : Exception(key, inner);

/// <summary>Downloads only official stable Windows releases and runs the original NSIS installer.</summary>
public sealed class LtkSetupService
{
    public const string ReleaseApi = "https://api.github.com/repos/LeagueToolkit/ltk-manager/releases/latest";
    private const string DownloadPrefix = "https://github.com/LeagueToolkit/ltk-manager/releases/download/";
    private const long MaxInstallerBytes = 256L * 1024 * 1024;
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly HttpClient _http;
    private readonly Func<string, CancellationToken, Task<int>> _runInstaller;

    public LtkSetupService(HttpClient? http = null, Func<string, CancellationToken, Task<int>>? runInstaller = null)
    {
        _http = http ?? DefaultClient;
        _runInstaller = runInstaller ?? RunInstallerAsync;
    }

    public static LtkInstallerAsset SelectInstaller(string releaseJson)
    {
        try { return ParseInstaller(releaseJson); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new LtkSetupException("setup_release_invalid", ex); }
    }

    private static LtkInstallerAsset ParseInstaller(string releaseJson)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new LtkSetupException("setup_release_invalid");
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith("_x64-setup.exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(name) != name) continue;
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            var size = asset.GetProperty("size").GetInt64();
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != "https" || uri.Host != "github.com" || size <= 0 || size > MaxInstallerBytes
                || digest is not { Length: 71 } || !digest.StartsWith("sha256:", StringComparison.Ordinal)
                || !digest[7..].All(Uri.IsHexDigit)) continue;
            return new(name, uri, size, digest[7..]);
        }
        throw new LtkSetupException("setup_release_invalid");
    }

    public async Task<string> DownloadInstallerAsync(string cacheDirectory, IProgress<LtkSetupProgress>? progress = null, CancellationToken ct = default)
    {
        if (NetworkPolicy.Offline) throw new LtkSetupException("setup_offline");
        progress?.Report(new("lookup"));
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
        request.Headers.UserAgent.ParseAdd("Riftevora/3.1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var metadata = await _http.SendAsync(request, ct);
        metadata.EnsureSuccessStatusCode();
        var asset = SelectInstaller(await metadata.Content.ReadAsStringAsync(ct));
        Directory.CreateDirectory(cacheDirectory);
        // A unique path avoids sharing partially downloaded or previously modified executables.
        var destination = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N") + "-" + asset.Name);
        var partial = destination + ".part";
        try
        {
            using var download = new HttpRequestMessage(HttpMethod.Get, asset.Url);
            download.Headers.UserAgent.ParseAdd("Riftevora/3.1.0");
            using var response = await _http.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
                throw new LtkSetupException("setup_integrity_failed");
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) != 0)
                {
                    total += count;
                    if (total > asset.Size) throw new LtkSetupException("setup_integrity_failed");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress?.Report(new("download", total * 100d / asset.Size));
                }
                progress?.Report(new("verify", 100));
                if (total != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new LtkSetupException("setup_integrity_failed");
            }
            ct.ThrowIfCancellationRequested();
            File.Move(partial, destination);
            return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public async Task InstallAsync(string cacheDirectory, IProgress<LtkSetupProgress>? progress = null, CancellationToken ct = default)
    {
        var installer = await DownloadInstallerAsync(cacheDirectory, progress, ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new("install", 100));
            // The installer takes ownership after launch. Do not abandon its process on cancellation.
            var exit = await _runInstaller(installer, CancellationToken.None);
            if (exit != 0) throw new LtkSetupException("setup_install_failed");
        }
        finally { if (File.Exists(installer)) File.Delete(installer); }
    }

    private static async Task<int> RunInstallerAsync(string installer, CancellationToken ct)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(installer, "/S")
            {
                UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(installer)!
            }) ?? throw new LtkSetupException("setup_install_failed");
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new LtkSetupException("setup_uac_cancelled", ex); }
    }
}
