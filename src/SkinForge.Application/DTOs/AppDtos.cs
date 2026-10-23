using SkinForge.Core.Enums;

namespace SkinForge.Application.DTOs;

public record ImportProgressDto(int Percentage, string Message, string? CurrentFile = null);

public record DashboardStatsDto(int InstalledCount, int ActiveCount, int ChampionCount, string TotalStorageFormatted);

public class SkinFilterCriteriaDto
{
    public string? Champion { get; set; }
    public ModCategory? Category { get; set; }
    public string? Author { get; set; }
    public bool OnlyInstalled { get; set; } = false;
    public bool OnlyFavorites { get; set; } = false;
    public bool OnlyActive { get; set; } = false;
    public string? SearchQuery { get; set; }
    public string SortBy { get; set; } = "Name"; // Name, RecentlyAdded, RecentlyUsed, FileSize, Champion
    public bool SortAscending { get; set; } = true;
}

public enum ConflictResolutionAction
{
    KeepA = 0,
    KeepB = 1,
    DisableBoth = 2
}

public record ConflictResolutionDto(string ConflictId, ConflictResolutionAction Action);
