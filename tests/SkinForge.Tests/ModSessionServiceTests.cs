using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SkinForge.Application.Services;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.ModProviders;
using Xunit;

namespace SkinForge.Tests;

public class ModSessionServiceTests
{
    private readonly Mock<ISkinRepository> _mockRepo;
    private readonly Mock<IAuditLogRepository> _mockAudit;
    private readonly MockLtkManagerAdapter _mockLtk;
    private readonly ModSessionService _service;

    public ModSessionServiceTests()
    {
        _mockRepo = new Mock<ISkinRepository>();
        _mockAudit = new Mock<IAuditLogRepository>();
        _mockLtk = new MockLtkManagerAdapter { CurrentStatus = LtkStatus.Ready };

        _mockRepo.Setup(r => r.GetByIdAsync("skin-spirit-ahri", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Skin("skin-spirit-ahri", "Spirit Blossom Ahri", "Ahri"));

        _mockAudit.Setup(a => a.InsertAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new ModSessionService(
            _mockLtk,
            _mockRepo.Object,
            _mockAudit.Object,
            new NullLogger<ModSessionService>()
        );
    }

    [Fact]
    public async Task StartSession_Initializes_In_WaitingForGame_State()
    {
        var result = await _service.StartSessionAsync("skin-spirit-ahri");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(SessionStatus.WaitingForGame, result.Data.Status);
        Assert.True(_service.IsSessionActive);

        // Clean up
        await _service.StopSessionAsync();
    }

    [Fact]
    public async Task Cannot_Start_Multiple_Concurrent_Sessions()
    {
        await _service.StartSessionAsync("skin-spirit-ahri");
        var second = await _service.StartSessionAsync("skin-spirit-ahri");

        Assert.False(second.IsSuccess);

        await _service.StopSessionAsync();
    }

    [Fact]
    public async Task StopSession_Restores_Clean_State()
    {
        await _service.StartSessionAsync("skin-spirit-ahri");
        var stopResult = await _service.StopSessionAsync();

        Assert.True(stopResult.IsSuccess);
        Assert.False(_service.IsSessionActive);
        Assert.Equal(SessionStatus.Stopped, _service.CurrentSession?.Status);
    }
}
