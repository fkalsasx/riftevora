using Microsoft.Extensions.Logging;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Application.Services;

public class CatalogService
{
    private readonly ICatalogProvider _provider;
    private readonly ILogger<CatalogService> _logger;

    public CatalogState State => _provider.State;

    public CatalogService(ICatalogProvider provider, ILogger<CatalogService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public Task<string> GetCurrentPatchAsync(CancellationToken cancellationToken = default)
    {
        return _provider.GetCurrentPatchAsync(cancellationToken);
    }

    public Task<IReadOnlyList<OfficialChampion>> GetChampionsAsync(CancellationToken cancellationToken = default)
    {
        return _provider.GetChampionsAsync(cancellationToken);
    }

    public Task<OfficialChampion?> GetChampionDetailsAsync(string championId, CancellationToken cancellationToken = default)
    {
        return _provider.GetChampionDetailsAsync(championId, cancellationToken);
    }

    public Task<IReadOnlyList<OfficialSkin>> GetChampionSkinsAsync(string championId, CancellationToken cancellationToken = default)
    {
        return _provider.GetChampionSkinsAsync(championId, cancellationToken);
    }

    public Task<OfficialSkin?> GetSkinAsync(string skinId, CancellationToken cancellationToken = default)
    {
        return _provider.GetSkinAsync(skinId, cancellationToken);
    }

    public Task<IReadOnlyList<ChromaInfo>> GetChromasAsync(string skinId, CancellationToken cancellationToken = default)
    {
        return _provider.GetChromasAsync(skinId, cancellationToken);
    }

    public Task<CatalogPatchInfo> CheckPatchUpdateAsync(CancellationToken cancellationToken = default)
    {
        return _provider.CheckPatchUpdateAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return _provider.RefreshCatalogAsync(cancellationToken);
    }

    public async Task<List<OfficialChampion>> FilterChampionsAsync(string? query, string? role, CancellationToken cancellationToken = default)
    {
        var all = await _provider.GetChampionsAsync(cancellationToken);
        IEnumerable<OfficialChampion> results = all;

        if (!string.IsNullOrWhiteSpace(role) && !role.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            results = results.Where(c => c.Tags.Any(t => t.Equals(role, StringComparison.OrdinalIgnoreCase)) ||
                                         c.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            results = results.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                         c.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return results.ToList();
    }
}
