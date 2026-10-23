using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public interface IAuditLogRepository
{
    Task InsertAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditLogEntry>> GetRecentLogsAsync(int count = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditLogEntry>> GetLogsByActionAsync(string action, int count = 50, CancellationToken cancellationToken = default);
    Task ClearOlderThanAsync(DateTime threshold, CancellationToken cancellationToken = default);
}
