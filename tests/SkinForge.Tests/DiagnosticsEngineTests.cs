using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SkinForge.Application.Services;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.ModProviders;
using Xunit;

namespace SkinForge.Tests;

public class DiagnosticsEngineTests
{
    [Fact]
    public async Task DiagnosticsEngine_Runs_15_Point_Inspection()
    {
        var mockRepo = new Mock<ISkinRepository>();
        var mockLtk = new MockLtkManagerAdapter { CurrentStatus = LtkStatus.Ready };
        var mockCatalog = new Mock<ICatalogProvider>();

        mockRepo.Setup(r => r.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppSettings { GameDirectory = @"C:\Riot Games\League of Legends\Game" });
        mockRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Skin>());
        mockRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Skin>());

        mockCatalog.Setup(c => c.GetCurrentPatchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("14.20.1");

        var engine = new DiagnosticsEngine(
            mockRepo.Object,
            mockLtk,
            mockCatalog.Object,
            libraryPath: Path.GetTempPath(),
            cachePath: Path.GetTempPath(),
            new NullLogger<DiagnosticsEngine>()
        );

        var report = await engine.RunFullDiagnosticsAsync();

        Assert.NotNull(report);
        Assert.Equal(15, report.Checks.Count);
        Assert.Equal("14.20.1", report.LeagueVersion);
        Assert.Equal("2.4.1", report.LtkVersion);
    }
}
