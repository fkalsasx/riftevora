using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Infrastructure.Database;

public class SqliteAuditLogRepository : IAuditLogRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteAuditLogRepository> _logger;

    public SqliteAuditLogRepository(string connectionString, ILogger<SqliteAuditLogRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InsertAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AuditLogs (Id, OperationId, CorrelationId, Action, EntityId, EntityName, Status, DurationMs, Timestamp, DetailsJson)
                VALUES (@id, @opId, @corrId, @action, @entityId, @entityName, @status, @duration, @timestamp, @details);
            ";

            cmd.Parameters.AddWithValue("@id", entry.Id);
            cmd.Parameters.AddWithValue("@opId", entry.OperationId);
            cmd.Parameters.AddWithValue("@corrId", entry.CorrelationId);
            cmd.Parameters.AddWithValue("@action", entry.Action);
            cmd.Parameters.AddWithValue("@entityId", (object?)entry.EntityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@entityName", (object?)entry.EntityName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", entry.Status);
            cmd.Parameters.AddWithValue("@duration", entry.DurationMs);
            cmd.Parameters.AddWithValue("@timestamp", entry.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@details", (object?)entry.DetailsJson ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert audit log entry for operation '{Action}'", entry.Action);
        }
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetRecentLogsAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        var logs = new List<AuditLogEntry>();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, OperationId, CorrelationId, Action, EntityId, EntityName, Status, DurationMs, Timestamp, DetailsJson
                FROM AuditLogs
                ORDER BY Timestamp DESC
                LIMIT @count;
            ";
            cmd.Parameters.AddWithValue("@count", count);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                logs.Add(new AuditLogEntry(
                    Id: reader.GetString(0),
                    OperationId: reader.GetString(1),
                    CorrelationId: reader.GetString(2),
                    Action: reader.GetString(3),
                    EntityId: reader.IsDBNull(4) ? null : reader.GetString(4),
                    EntityName: reader.IsDBNull(5) ? null : reader.GetString(5),
                    Status: reader.GetString(6),
                    DurationMs: reader.GetInt64(7),
                    Timestamp: DateTime.Parse(reader.GetString(8)),
                    DetailsJson: reader.IsDBNull(9) ? null : reader.GetString(9)
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query recent audit logs.");
        }
        return logs;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetLogsByActionAsync(string action, int count = 50, CancellationToken cancellationToken = default)
    {
        var logs = new List<AuditLogEntry>();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, OperationId, CorrelationId, Action, EntityId, EntityName, Status, DurationMs, Timestamp, DetailsJson
                FROM AuditLogs
                WHERE Action = @action
                ORDER BY Timestamp DESC
                LIMIT @count;
            ";
            cmd.Parameters.AddWithValue("@action", action);
            cmd.Parameters.AddWithValue("@count", count);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                logs.Add(new AuditLogEntry(
                    Id: reader.GetString(0),
                    OperationId: reader.GetString(1),
                    CorrelationId: reader.GetString(2),
                    Action: reader.GetString(3),
                    EntityId: reader.IsDBNull(4) ? null : reader.GetString(4),
                    EntityName: reader.IsDBNull(5) ? null : reader.GetString(5),
                    Status: reader.GetString(6),
                    DurationMs: reader.GetInt64(7),
                    Timestamp: DateTime.Parse(reader.GetString(8)),
                    DetailsJson: reader.IsDBNull(9) ? null : reader.GetString(9)
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query audit logs by action '{Action}'", action);
        }
        return logs;
    }

    public async Task ClearOlderThanAsync(DateTime threshold, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM AuditLogs WHERE Timestamp < @threshold;";
            cmd.Parameters.AddWithValue("@threshold", threshold.ToString("O"));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prune audit logs older than {Threshold}", threshold);
        }
    }
}
