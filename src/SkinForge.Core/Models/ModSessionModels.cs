namespace SkinForge.Core.Models;

public enum SessionStatus
{
    Pending,
    WaitingForGame,
    LeagueDetected,
    PreparingMod,
    Patching,
    Active,
    Stopping,
    Stopped,
    Failed
}

public record ModSession(
    string SessionId,
    string SkinId,
    string SkinName,
    string ChampionName,
    string EngineName,
    DateTime StartedAt,
    SessionStatus Status,
    bool GameProcessDetected,
    long ElapsedSeconds,
    string? ErrorMessage,
    IReadOnlyList<string> ActiveModNames
);

public record ApplyRiskNotice(
    bool Accepted,
    DateTime AcknowledgedAtUtc
);
