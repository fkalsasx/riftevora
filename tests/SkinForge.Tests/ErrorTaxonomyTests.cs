using SkinForge.Core.Models;
using Xunit;

namespace SkinForge.Tests;

public class ErrorTaxonomyTests
{
    [Fact]
    public void All_ErrorCodes_E001_Through_E014_ProduceExpectedCodes()
    {
        var e1 = ApplicationError.PackageInvalid("Corrupt zip");
        var e2 = ApplicationError.ManifestMissing("No manifest.json");
        var e3 = ApplicationError.AssetMissing("texture.dds");
        var e4 = ApplicationError.DuplicatePackage("skin-123");
        var e5 = ApplicationError.UnsupportedVersion("0.1.0");
        var e6 = ApplicationError.LtkManagerNotFound(@"C:\Program Files\LTK");
        var e7 = ApplicationError.LtkManagerUnavailable("Service stopped");
        var e8 = ApplicationError.PermissionError(@"C:\Windows", "Access Denied");
        var e9 = ApplicationError.FileAccessError("game.wad", "Locked by process");
        var e10 = ApplicationError.DatabaseError("Disk I/O failure");
        var e11 = ApplicationError.ConflictDetected("Asset collision", "path/file.skn");
        var e12 = ApplicationError.BackupFailed("Disk full");
        var e13 = ApplicationError.RestoreFailed("Checksum mismatch");
        var e14 = ApplicationError.UnexpectedError(new Exception("Unknown bug"));

        Assert.Equal("E001", e1.Code);
        Assert.Equal("E002", e2.Code);
        Assert.Equal("E003", e3.Code);
        Assert.Equal("E004", e4.Code);
        Assert.Equal("E005", e5.Code);
        Assert.Equal("E006", e6.Code);
        Assert.Equal("E007", e7.Code);
        Assert.Equal("E008", e8.Code);
        Assert.Equal("E009", e9.Code);
        Assert.Equal("E010", e10.Code);
        Assert.Equal("E011", e11.Code);
        Assert.Equal("E012", e12.Code);
        Assert.Equal("E013", e13.Code);
        Assert.Equal("E014", e14.Code);
    }

    [Fact]
    public void Errors_Have_Unique_CorrelationIds_And_SuggestedActions()
    {
        var err1 = ApplicationError.PackageInvalid("Bad archive");
        var err2 = ApplicationError.PackageInvalid("Bad archive");

        Assert.NotEmpty(err1.CorrelationId);
        Assert.NotEmpty(err2.CorrelationId);
        Assert.NotEqual(err1.CorrelationId, err2.CorrelationId);
        Assert.NotNull(err1.SuggestedAction);
    }

    [Fact]
    public void OperationResult_Binds_ApplicationError_Accurately()
    {
        var appErr = ApplicationError.ConflictDetected("Asset collision", "particles.vfx");
        var result = OperationResult.Failure(appErr);

        Assert.False(result.IsSuccess);
        Assert.Equal("E011", result.ErrorCode);
        Assert.Equal(appErr.Message, result.ErrorMessage);
        Assert.Same(appErr, result.Error);
    }
}
