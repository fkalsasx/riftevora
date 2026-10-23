namespace SkinForge.Core.Interfaces;

public record DiagnosticCheckItem(
    string Category,
    string Name,
    bool Passed,
    string Details,
    string? ErrorCode = null
);

public record FullDiagnosticsReport(
    string AppVersion,
    DateTime Timestamp,
    bool AllPassed,
    IReadOnlyList<DiagnosticCheckItem> Checks,
    string DatabaseStatus,
    string LibraryStatus,
    string LtkStatus,
    string StorageStatus,
    string? LastOperation,
    string? LastError,
    string LogDirectoryPath
);

public record QuickHealthReport(
    bool IsHealthy,
    string Message,
    bool DatabaseOk,
    bool StorageOk,
    bool LtkDetected
);

public interface IDiagnosticsService
{
    Task<QuickHealthReport> RunQuickHealthCheckAsync(CancellationToken cancellationToken = default);
    Task<FullDiagnosticsReport> RunFullDiagnosticsAsync(CancellationToken cancellationToken = default);
}
