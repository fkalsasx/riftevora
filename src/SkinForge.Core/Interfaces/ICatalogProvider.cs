using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public interface ICatalogProvider
{
    CatalogState State { get; }

    Task<string> GetCurrentPatchAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfficialChampion>> GetChampionsAsync(CancellationToken cancellationToken = default);

    Task<OfficialChampion?> GetChampionDetailsAsync(string championId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfficialSkin>> GetChampionSkinsAsync(string championId, CancellationToken cancellationToken = default);

    Task<OfficialSkin?> GetSkinAsync(string skinId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChromaInfo>> GetChromasAsync(string skinId, CancellationToken cancellationToken = default);

    Task<CatalogPatchInfo> CheckPatchUpdateAsync(CancellationToken cancellationToken = default);

    Task RefreshCatalogAsync(CancellationToken cancellationToken = default);
}
