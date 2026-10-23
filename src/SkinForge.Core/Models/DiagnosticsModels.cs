namespace SkinForge.Core.Models;

public enum DiagnosticStatus
{
    Pass,
    Warning,
    Fail,
    Unknown
}

public enum FixActionType
{
    None,
    ClearCache,
    ChooseFolder,
    EnableMod,
    ReviewConflict,
    DownloadLtk,
    RestartSession
}

public record DiagnosticCheck(
    string Id,
    string Category,
    string Name,
    DiagnosticStatus Status,
    string Message,
    string? TechnicalDetails = null,
    FixActionType FixAction = FixActionType.None,
    string? FixButtonText = null
);

public record ComprehensiveDiagnostics(
    DateTime Timestamp,
    bool OverallHealthy,
    IReadOnlyList<DiagnosticCheck> Checks,
    string LeagueVersion,
    string LtkVersion,
    string StorageUsed,
    string? MostLikelyCause = null
);
