using Microsoft.Extensions.Logging.Abstractions;
using SkinForge.Core.Interfaces;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.ModProviders;
using Xunit;

namespace SkinForge.Tests;

public class LtkAdapterTests
{
    [Fact]
    public async Task Unsupported_Management_Actions_Never_Report_Success()
    {
        var real = new RealLtkManagerAdapter(NullLogger<RealLtkManagerAdapter>.Instance, []);
        Assert.False((await real.EnableModAsync("test")).IsSuccess);
        Assert.False((await real.DisableModAsync("test")).IsSuccess);
        Assert.False((await real.RunPatcherAsync(["test"])).IsSuccess);
        Assert.False((await real.StopPatcherAsync()).IsSuccess);
        Assert.False((await real.RestoreAsync()).IsSuccess);
    }
    [Fact]
    public async Task MockAdapter_Reflects_Configured_Status()
    {
        var mock = new MockLtkManagerAdapter
        {
            CurrentStatus = LtkStatus.Ready,
            MockVersion = "2.4.1"
        };

        var status = await mock.GetStatusAsync();
        Assert.Equal(LtkStatus.Ready, status.Status);
        Assert.True(status.IsInstalled);
        Assert.Equal("2.4.1", status.Version);
    }

    [Fact]
    public async Task RealAdapter_Returns_NotDetected_When_Binary_Does_Not_Exist()
    {
        var logger = new NullLogger<RealLtkManagerAdapter>();
        var real = new RealLtkManagerAdapter(logger, []);

        // When testing with non-existent path
        var detected = await real.DetectInstallationAsync(@"C:\NonExistent\FakeLtk.exe");
        Assert.False(detected);

        var status = await real.GetStatusAsync();
        Assert.Equal(LtkStatus.NotDetected, status.Status);
        Assert.False(status.IsInstalled);
        Assert.Null(status.Version);
    }

    [Fact]
    public async Task RealAdapter_Fails_Safely_On_ApplyMod_When_Not_Detected()
    {
        var logger = new NullLogger<RealLtkManagerAdapter>();
        var real = new RealLtkManagerAdapter(logger, []);

        var skin = new Skin("skin-test", "Project Yasuo", "Yasuo");
        var result = await real.ApplyModAsync(skin);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal("E006", result.Error.Code);
    }
}
