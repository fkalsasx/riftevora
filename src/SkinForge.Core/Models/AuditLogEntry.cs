namespace SkinForge.Core.Models;

public record AuditLogEntry(
    string Id,
    string OperationId,
    string CorrelationId,
    string Action,
    string? EntityId,
    string? EntityName,
    string Status, // "Success", "Failed", "Cancelled", "Warning"
    long DurationMs,
    DateTime Timestamp,
    string? DetailsJson = null
)
{
    public static AuditLogEntry Create(
        string action,
        string? entityId,
        string? entityName,
        string status,
        long durationMs,
        string? correlationId = null,
        string? detailsJson = null)
    {
        return new AuditLogEntry(
            Id: Guid.NewGuid().ToString("N"),
            OperationId: Guid.NewGuid().ToString("N")[..8],
            CorrelationId: correlationId ?? Guid.NewGuid().ToString("N")[..8],
            Action: action,
            EntityId: entityId,
            EntityName: entityName,
            Status: status,
            DurationMs: durationMs,
            Timestamp: DateTime.UtcNow,
            DetailsJson: detailsJson
        );
    }
}
