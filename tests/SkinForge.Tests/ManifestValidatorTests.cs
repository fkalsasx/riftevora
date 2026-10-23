using SkinForge.Core.Enums;
using SkinForge.Core.Models;
using SkinForge.Infrastructure.Packaging;
using Xunit;

namespace SkinForge.Tests;

public class ManifestValidatorTests
{
    [Fact]
    public void Validate_ShouldReturnSuccess_ForValidManifest()
    {
        var manifest = new SkinManifest
        {
            Id = "ahri-spirit-blossom",
            Name = "Spirit Blossom Ahri",
            Champion = "Ahri",
            SkinSlot = 0,
            Version = "1.2.0",
            Author = "RiotCommunity",
            Category = ModCategory.FullOverhaul,
            Preview = "preview.png",
            Assets =
            [
                new SkinAsset("assets/characters/ahri/skins/base/ahri.dds", "abc123hash", 1024, LoadOrderLayer.Texture)
            ]
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ShouldFail_WhenIdIsEmptyOrContainsIllegalChars()
    {
        var manifest1 = new SkinManifest { Id = "", Name = "Valid", Champion = "Yasuo" };
        var res1 = ManifestValidator.Validate(manifest1);
        Assert.False(res1.IsSuccess);
        Assert.Contains("'id' is required", res1.ErrorMessage);

        var manifest2 = new SkinManifest { Id = "invalid/id!@#", Name = "Valid", Champion = "Yasuo" };
        var res2 = ManifestValidator.Validate(manifest2);
        Assert.False(res2.IsSuccess);
        Assert.Contains("invalid characters", res2.ErrorMessage);
    }

    [Fact]
    public void Validate_ShouldFail_WhenAssetsListIsEmpty()
    {
        var manifest = new SkinManifest
        {
            Id = "valid-id",
            Name = "Valid Name",
            Champion = "Yasuo",
            Assets = []
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsSuccess);
        Assert.Contains("at least one asset", result.ErrorMessage);
    }
}
