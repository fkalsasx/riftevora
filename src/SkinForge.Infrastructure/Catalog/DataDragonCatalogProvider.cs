using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Infrastructure.Catalog;

public sealed class CatalogSnapshot
{
    public string Patch { get; set; } = "";
    public List<OfficialChampion> Champions { get; set; } = [];
    public Dictionary<string, List<OfficialSkin>> Skins { get; set; } = [];
}

public class DataDragonCatalogProvider : ICatalogProvider
{
    private readonly HttpClient _http;
    private readonly string _cacheFile;
    private readonly ILogger<DataDragonCatalogProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CatalogSnapshot _snapshot = new();
    private bool _loaded;
    public CatalogState State { get; private set; } = CatalogState.Loading;

    public DataDragonCatalogProvider(string cacheDirectory, ILogger<DataDragonCatalogProvider> logger, HttpClient? httpClient = null)
    {
        Directory.CreateDirectory(cacheDirectory);
        _cacheFile = Path.Combine(cacheDirectory, "catalog-v2.json");
        _logger = logger;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    private async Task EnsureAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_loaded) return;
            foreach (var file in new[] { _cacheFile, Path.Combine(AppContext.BaseDirectory, "Resources", "catalog-v2.json") })
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    var cached = JsonSerializer.Deserialize<CatalogSnapshot>(await File.ReadAllTextAsync(file, ct));
                    if (cached is { Champions.Count: > 0 } && !string.IsNullOrEmpty(cached.Patch))
                    {
                        _snapshot = cached;
                        foreach (var id in _snapshot.Skins.Keys.ToArray()) _snapshot.Skins[id] = SkinArtworkMapper.Map(_snapshot.Skins[id]);
                        _loaded = true;
                        State = CatalogState.Stale;
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Cannot read catalog snapshot"); }
            }
        }
        finally { _gate.Release(); }
        await RefreshCatalogAsync(ct);
    }

    public async Task RefreshCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (SkinForge.Infrastructure.SystemInfo.NetworkPolicy.Offline) { State = _snapshot.Champions.Count > 0 ? CatalogState.Offline : CatalogState.Error; return; }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var versions = await _http.GetFromJsonAsync<List<string>>("https://ddragon.leagueoflegends.com/api/versions.json", cancellationToken);
            var patch = versions?.FirstOrDefault() ?? throw new InvalidDataException("Yama bilgisi alınamadı.");
            using var doc = await GetJsonAsync($"https://ddragon.leagueoflegends.com/cdn/{patch}/data/en_US/champion.json", cancellationToken);
            var old = _snapshot;
            var champions = doc.RootElement.GetProperty("data").EnumerateObject().Select(p =>
            {
                var c = p.Value;
                var id = c.GetProperty("id").GetString()!;
                var title = c.GetProperty("title").GetString()!;
                var tags = c.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray();
                return new OfficialChampion(id, c.GetProperty("key").GetString()!, c.GetProperty("name").GetString()!,
                    char.ToUpperInvariant(title[0]) + title[1..], tags.FirstOrDefault() ?? "", c.GetProperty("blurb").GetString()!,
                    $"https://ddragon.leagueoflegends.com/cdn/{patch}/img/champion/{id}.png",
                    $"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{id}_0.jpg",
                    old.Patch == patch && old.Skins.TryGetValue(id, out var skins) ? skins.Count : 0, tags);
            }).OrderBy(c => c.Name).ToList();
            _snapshot = new CatalogSnapshot { Patch = patch, Champions = champions, Skins = old.Patch == patch ? old.Skins : [] };
            _loaded = true;
            State = CatalogState.Loaded;
            await SaveAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Catalog refresh failed; keeping verified snapshot");
            State = _snapshot.Champions.Count > 0 ? CatalogState.Offline : CatalogState.Error;
        }
        finally { _gate.Release(); }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        if (SkinForge.Infrastructure.SystemInfo.NetworkPolicy.Offline) throw new HttpRequestException("Offline mode is on.");
        using var stream = await _http.GetStreamAsync(url, ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var temp = _cacheFile + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(_snapshot), ct);
        File.Move(temp, _cacheFile, true);
    }

    public async Task<string> GetCurrentPatchAsync(CancellationToken cancellationToken = default) { await EnsureAsync(cancellationToken); return _snapshot.Patch; }
    public async Task<IReadOnlyList<OfficialChampion>> GetChampionsAsync(CancellationToken cancellationToken = default) { await EnsureAsync(cancellationToken); return _snapshot.Champions.ToArray(); }
    public async Task<OfficialChampion?> GetChampionDetailsAsync(string championId, CancellationToken cancellationToken = default)
        => (await GetChampionsAsync(cancellationToken)).FirstOrDefault(c => c.Id.Equals(championId, StringComparison.OrdinalIgnoreCase));

    public Task<IReadOnlyList<OfficialSkin>> GetChampionSkinsAsync(string championId, CancellationToken cancellationToken = default)
        => LoadChampionSkinsAsync(championId, true, cancellationToken);

    private async Task<IReadOnlyList<OfficialSkin>> LoadChampionSkinsAsync(string championId, bool persist, CancellationToken cancellationToken)
    {
        await EnsureAsync(cancellationToken);
        if (!Regex.IsMatch(championId, "^[A-Za-z0-9]+$")) return [];
        OfficialChampion? champion; string patch;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot.Skins.TryGetValue(championId, out var cached)) return cached.ToArray();
            champion = _snapshot.Champions.FirstOrDefault(c => c.Id == championId);
            if (champion == null) return [];
            patch = _snapshot.Patch;
        }
        finally { _gate.Release(); }
        try
        {
            // The network call runs outside the gate so one slow champion never freezes the rest of the catalog.
            using var doc = await GetJsonAsync($"https://ddragon.leagueoflegends.com/cdn/{patch}/data/en_US/champion/{championId}.json", cancellationToken);
            var skins = doc.RootElement.GetProperty("data").GetProperty(championId).GetProperty("skins").EnumerateArray().Select(s =>
            {
                var num = s.GetProperty("num").GetInt32();
                return new OfficialSkin(s.GetProperty("id").GetString()!, num,
                    num == 0 ? "Original" : s.GetProperty("name").GetString()!, championId, champion.Name, 0, [],
                    $"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{championId}_{num}.jpg",
                    $"https://ddragon.leagueoflegends.com/cdn/img/champion/loading/{championId}_{num}.jpg", "", "", Patch: patch);
            }).ToList();
            // Color variants use their parent skin's artwork. Their distinct
            // entry remains selectable and is labelled as a chroma in the UI.
            skins = SkinArtworkMapper.Map(skins);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_snapshot.Patch != patch) return skins;
                _snapshot.Skins[championId] = skins;
                var i = _snapshot.Champions.FindIndex(c => c.Id == championId);
                if (i >= 0) _snapshot.Champions[i] = _snapshot.Champions[i] with { SkinCount = skins.Count };
                if (persist) await SaveAsync(cancellationToken);
            }
            finally { _gate.Release(); }
            return skins.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cannot load skins for {Champion}", championId);
            State = CatalogState.Offline;
            return [];
        }
    }

    /// <summary>Every skin and chroma currently known, in champion order. Cheap: reads memory only.</summary>
    public async Task<IReadOnlyList<OfficialSkin>> GetAllSkinsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try { return _snapshot.Champions.SelectMany(c => _snapshot.Skins.TryGetValue(c.Id, out var s) ? s : []).ToArray(); }
        finally { _gate.Release(); }
    }

    /// <summary>After a patch bump the skin lists are empty; fetch the missing ones in the background, a few at a time.</summary>
    public async Task EnsureAllSkinsAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);
        string[] missing;
        await _gate.WaitAsync(cancellationToken);
        try { missing = _snapshot.Champions.Where(c => !_snapshot.Skins.ContainsKey(c.Id)).Select(c => c.Id).ToArray(); }
        finally { _gate.Release(); }
        if (missing.Length == 0) return;
        int done = 0;
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (id, ct) =>
        {
            await LoadChampionSkinsAsync(id, false, ct);
            progress?.Report((Interlocked.Increment(ref done), missing.Length));
        });
        await _gate.WaitAsync(cancellationToken);
        try { await SaveAsync(cancellationToken); } finally { _gate.Release(); }
    }

    public async Task<OfficialSkin?> GetSkinAsync(string skinId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try { return _snapshot.Skins.Values.SelectMany(s => s).FirstOrDefault(s => s.Id == skinId); }
        finally { _gate.Release(); }
    }
    public async Task<IReadOnlyList<ChromaInfo>> GetChromasAsync(string skinId, CancellationToken cancellationToken = default)
        => (await GetSkinAsync(skinId, cancellationToken))?.Chromas ?? [];
    public async Task<CatalogPatchInfo> CheckPatchUpdateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);
        try
        {
            var versions = await _http.GetFromJsonAsync<List<string>>("https://ddragon.leagueoflegends.com/api/versions.json", cancellationToken);
            var latest = versions?.FirstOrDefault() ?? _snapshot.Patch;
            return new(_snapshot.Patch, latest, latest != _snapshot.Patch, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(_snapshot.Patch, _snapshot.Patch, false, DateTime.UtcNow); }
    }
}

