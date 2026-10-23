using SkinForge.Core.Models;

namespace SkinForge.Core.Interfaces;

public enum TransactionPhase
{
    Initialized,
    Validated,
    Prepared,
    BackedUp,
    Executed,
    Verified,
    Committed,
    RolledBack
}

public record InFlightTransaction(
    string TransactionId,
    string OperationType,
    string EntityId,
    string? EntityName,
    DateTime StartedAt,
    TransactionPhase CurrentPhase,
    string? BackupSnapshotPath,
    List<string> StagedFiles
);

public record RecoveryReport(
    bool RecoveryNeeded,
    bool Success,
    string Message,
    string? RecoveredTransactionId
);

public interface ITransactionCoordinator
{
    Task<InFlightTransaction> BeginTransactionAsync(string operationType, string entityId, string? entityName);
    Task UpdatePhaseAsync(string transactionId, TransactionPhase newPhase, string? backupSnapshotPath = null, List<string>? stagedFiles = null);
    Task CommitTransactionAsync(string transactionId);
    Task RollbackTransactionAsync(string transactionId, string reason);
    Task<RecoveryReport> CheckAndExecuteStartupRecoveryAsync(CancellationToken cancellationToken = default);
}
