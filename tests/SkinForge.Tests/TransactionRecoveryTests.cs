using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SkinForge.Application.Services;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;
using Xunit;

namespace SkinForge.Tests;

public class TransactionRecoveryTests : IDisposable
{
    private readonly string _testStateDir;
    private readonly Mock<IAuditLogRepository> _mockAudit;
    private readonly TransactionCoordinator _coordinator;

    public TransactionRecoveryTests()
    {
        _testStateDir = Path.Combine(Path.GetTempPath(), "SkinForge_TxTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testStateDir);

        _mockAudit = new Mock<IAuditLogRepository>();
        _mockAudit.Setup(a => a.InsertAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _coordinator = new TransactionCoordinator(
            _testStateDir,
            _mockAudit.Object,
            new NullLogger<TransactionCoordinator>()
        );
    }

    [Fact]
    public async Task InFlight_Transaction_Cleans_Up_On_Commit()
    {
        var tx = await _coordinator.BeginTransactionAsync("Activate", "skin-1", "High Noon Yasuo");
        var stateFile = Path.Combine(_testStateDir, ".in-flight-tx.json");
        Assert.True(File.Exists(stateFile));

        await _coordinator.CommitTransactionAsync(tx.TransactionId);
        Assert.False(File.Exists(stateFile));
    }

    [Fact]
    public async Task Interrupted_Transaction_Is_Recovered_On_Startup()
    {
        // 1. Simulate an interrupted transaction by creating an in-flight file and a staged file
        var stagedFile = Path.Combine(_testStateDir, "staged_asset.dds");
        File.WriteAllText(stagedFile, "dummy staged bytes");

        var tx = await _coordinator.BeginTransactionAsync("Activate", "skin-interrupted", "Spirit Blossom Ahri");
        await _coordinator.UpdatePhaseAsync(tx.TransactionId, TransactionPhase.Executed, stagedFiles: new List<string> { stagedFile });

        // 2. Simulate application reboot / startup check
        var recoveryReport = await _coordinator.CheckAndExecuteStartupRecoveryAsync();

        Assert.True(recoveryReport.RecoveryNeeded);
        Assert.True(recoveryReport.Success);
        Assert.False(File.Exists(stagedFile)); // Staged file cleaned
        Assert.False(File.Exists(Path.Combine(_testStateDir, ".in-flight-tx.json"))); // State file cleaned
    }

    public void Dispose()
    {
        if (Directory.Exists(_testStateDir))
        {
            try { Directory.Delete(_testStateDir, true); } catch { }
        }
    }
}
