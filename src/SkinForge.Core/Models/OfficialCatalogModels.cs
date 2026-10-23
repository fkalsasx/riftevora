namespace SkinForge.Core.Models;

public record OfficialChampion(
    string Id,
    string Key,
    string Name,
    string Title,
    string Role,
    string Blurb,
    string IconUrl,
    string SplashUrl,
    int SkinCount,
    IReadOnlyList<string> Tags
);

public record ChromaInfo(
    string Id,
    string Name,
    string ChromaPath,
    IReadOnlyList<string> Colors
);

public record OfficialSkin(
    string Id,
    int Num,
    string Name,
    string ChampionId,
    string ChampionName,
    int ChromaCount,
    IReadOnlyList<ChromaInfo> Chromas,
    string SplashUrl,
    string LoadingUrl,
    string Rarity,
    string ReleaseInfo,
    string Source = "Riot Data Dragon",
    string Patch = "14.20.1",
    DateTime? LastUpdatedUtc = null
);

public record CatalogPatchInfo(
    string CurrentPatch,
    string LatestOnlinePatch,
    bool IsUpdateAvailable,
    DateTime LastCheckedUtc
);

public enum CatalogState
{
    Loading,
    Loaded,
    Empty,
    Offline,
    Error,
    Stale
}
