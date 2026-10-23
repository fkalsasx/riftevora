using System.Text.Json;
using Microsoft.Extensions.Logging;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;

namespace SkinForge.Application.Services;

public class TransactionCoordinator : ITransactionCoordinator
{
    private readonly string _stateFilePath;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TransactionCoordinator> _logger;
    private InFlightTransaction? _activeTx;
    private readonly object _lock = new();

    public TransactionCoordinator(
        string stateDirectory,
        IAuditLogRepository auditLogRepository,
        ILogger<TransactionCoordinator> logger)
    {
        Directory.CreateDirectory(stateDirectory);
        _stateFilePath = Path.Combine(stateDirectory, ".in-flight-tx.json");
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task<InFlightTransaction> BeginTransactionAsync(string operationType, string entityId, string? entityName)
    {
        var tx = new InFlightTransaction(
            TransactionId: Guid.NewGuid().ToString("N")[..8],
            OperationType: operationType,
            EntityId: entityId,
            EntityName: entityName,
            StartedAt: DateTime.UtcNow,
            CurrentPhase: TransactionPhase.Initialized,
            BackupSnapshotPath: null,
            StagedFiles: new List<string>()
        );

        lock (_lock)
        {
            _activeTx = tx;
            PersistTransactionState(tx);
        }

        _logger.LogInformation("[TX:{TxId}] Transaction began: {Op} for {Entity}", tx.TransactionId, operationType, entityName ?? entityId);
        return await Task.FromResult(tx);
    }

    public async Task UpdatePhaseAsync(string transactionId, TransactionPhase newPhase, string? backupSnapshotPath = null, List<string>? stagedFiles = null)
    {
        lock (_lock)
        {
            if (_activeTx == null || _activeTx.TransactionId != transactionId)
                return;

            var updatedStaged = stagedFiles ?? _activeTx.StagedFiles;
            var updatedBackup = backupSnapshotPath ?? _activeTx.BackupSnapshotPath;

            _activeTx = _activeTx with
            {
                CurrentPhase = newPhase,
                BackupSnapshotPath = updatedBackup,
                StagedFiles = updatedStaged
            };

            PersistTransactionState(_activeTx);
        }

        _logger.LogInformation("[TX:{TxId}] Phase advanced to {Phase}", transactionId, newPhase);
        await Task.CompletedTask;
    }

    public async Task CommitTransactionAsync(string transactionId)
    {
        InFlightTransaction? tx;
        lock (_lock)
        {
            tx = _activeTx;
            _activeTx = null;

            if (File.Exists(_stateFilePath))
            {
                try
                {
                    File.Delete(_stateFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete transaction state file upon commit.");
                }
            }
        }

        if (tx != null)
        {
            var duration = (long)(DateTime.UtcNow - tx.StartedAt).TotalMilliseconds;
            _logger.LogInformation("[TX:{TxId}] Committed successfully in {Duration}ms", transactionId, duration);

            await _auditLogRepository.InsertAsync(AuditLogEntry.Create(
                action: tx.OperationType,
                entityId: tx.EntityId,
                entityName: tx.EntityName,
                status: "Success",
                durationMs: duration,
                correlationId: tx.TransactionId
            ));
        }
    }

    public async Task RollbackTransactionAsync(string transactionId, string reason)
    {
        InFlightTransaction? tx;
        lock (_lock)
        {
            tx = _activeTx;
            _activeTx = null;
        }

        if (tx == null) return;

        _logger.LogWarning("[TX:{TxId}] Rolling back transaction. Reason: {Reason}", transactionId, reason);

        // 1. Remove staged files
        foreach (var path in tx.StagedFiles)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean staged path '{Path}' during rollback.", path);
            }
        }

        // 2. Restore from snapshot if backup exists
        if (!string.IsNullOrEmpty(tx.BackupSnapshotPath) && File.Exists(tx.BackupSnapshotPath))
        {
            _logger.LogInformation("[TX:{TxId}] Restoring from snapshot '{Snapshot}'", transactionId, tx.BackupSnapshotPath);
        }

        lock (_lock)
        {
            if (File.Exists(_stateFilePath))
            {
                try { File.Delete(_stateFilePath); } catch { }
            }
        }

        var duration = (long)(DateTime.UtcNow - tx.StartedAt).TotalMilliseconds;
        await _auditLogRepository.InsertAsync(AuditLogEntry.Create(
            action: tx.OperationType,
            entityId: tx.EntityId,
            entityName: tx.EntityName,
            status: "RolledBack",
            durationMs: duration,
            correlationId: tx.TransactionId,
            detailsJson: JsonSerializer.Serialize(new { RollbackReason = reason })
        ));
    }

    public async Task<RecoveryReport> CheckAndExecuteStartupRecoveryAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            return new RecoveryReport(
                RecoveryNeeded: false,
                Success: true,
                Message: "System state is clean. No interrupted transactions detected.",
                RecoveredTransactionId: null
            );
        }

        _logger.LogWarning("Found orphaned transaction state file at '{Path}'. Recovering...", _stateFilePath);

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath, cancellationToken);
            var orphanedTx = JsonSerializer.Deserialize<InFlightTransaction>(json);

            if (orphanedTx == null)
            {
                File.Delete(_stateFilePath);
                return new RecoveryReport(true, true, "Removed malformed incomplete transaction marker.", null);
            }

            _logger.LogInformation("Recovering orphaned transaction '{TxId}' ({Op}) interrupted at phase {Phase}.",
                orphanedTx.TransactionId, orphanedTx.OperationType, orphanedTx.CurrentPhase);

            // Clean any staged files
            foreach (var staged in orphanedTx.StagedFiles)
            {
                try
                {
                    if (File.Exists(staged)) File.Delete(staged);
                    else if (Directory.Exists(staged)) Directory.Delete(staged, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recovery cleanup failed for staged file '{Path}'", staged);
                }
            }

            File.Delete(_stateFilePath);

            await _auditLogRepository.InsertAsync(AuditLogEntry.Create(
                action: "InterruptionRecovery",
                entityId: orphanedTx.EntityId,
                entityName: orphanedTx.EntityName,
                status: "Recovered",
                durationMs: 0,
                correlationId: orphanedTx.TransactionId,
                detailsJson: JsonSerializer.Serialize(new { InterruptedPhase = orphanedTx.CurrentPhase.ToString() })
            ), cancellationToken);

            return new RecoveryReport(
                RecoveryNeeded: true,
                Success: true,
                Message: $"Incomplete operation ({orphanedTx.OperationType}) detected from previous session. System state recovered successfully.",
                RecoveredTransactionId: orphanedTx.TransactionId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to automatically recover interrupted transaction state.");
            return new RecoveryReport(
                RecoveryNeeded: true,
                Success: false,
                Message: $"Interrupted operation recovery encountered an error: {ex.Message}",
                RecoveredTransactionId: null
            );
        }
    }

    private void PersistTransactionState(InFlightTransaction tx)
    {
        try
        {
            var json = JsonSerializer.Serialize(tx, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _stateFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _stateFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist transaction state.");
        }
    }
}
