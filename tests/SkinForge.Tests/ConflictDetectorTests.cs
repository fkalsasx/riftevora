using SkinForge.Application.Services;
using SkinForge.Core.Enums;
using SkinForge.Core.Models;
using Xunit;

namespace SkinForge.Tests;

public class ConflictDetectorTests
{
    [Fact]
    public void DetectConflicts_ShouldDetect_SameSkinSlotCollision()
    {
        var detector = new ConflictDetector();

        var skinA = new Skin("ahri-skin-a", "Spirit Blossom Ahri", "Ahri")
        {
            SkinSlot = 0,
            Assets = [new SkinAsset("assets/characters/ahri/skins/base/ahri.dds", "hash1", 500)]
        };

        var skinB = new Skin("ahri-skin-b", "Cyber Fox Ahri", "Ahri")
        {
            SkinSlot = 0,
            Assets = [new SkinAsset("assets/characters/ahri/skins/base/cyber_ahri.dds", "hash2", 500)]
        };

        var conflicts = detector.DetectConflicts([skinA, skinB]);
        Assert.NotEmpty(conflicts);
        Assert.Contains("Skin Slot 0", conflicts[0].Message);
    }

    [Fact]
    public void DetectConflicts_ShouldDetect_ExactAssetPathCollision()
    {
        var detector = new ConflictDetector();

        var skinA = new Skin("mod-a", "Custom Sound Mod", "Yasuo")
        {
            SkinSlot = 0,
            Assets = [new SkinAsset("assets/sounds/wwise2016/yasuo_sfx.bnk", "hash1", 1000)]
        };

        var skinB = new Skin("mod-b", "Another Sound Mod", "Yasuo")
        {
            SkinSlot = 1,
            Assets = [new SkinAsset("assets/sounds/wwise2016/yasuo_sfx.bnk", "hash2", 1000)]
        };

        var conflicts = detector.DetectConflicts([skinA, skinB]);
        Assert.NotEmpty(conflicts);
        Assert.Equal("assets/sounds/wwise2016/yasuo_sfx.bnk", conflicts[0].TargetAssetPath);
    }

    [Fact]
    public void DetectConflicts_ShouldReturnEmpty_WhenNoCollision()
    {
        var detector = new ConflictDetector();

        var skinA = new Skin("ahri-skin", "Ahri Skin", "Ahri")
        {
            SkinSlot = 0,
            Assets = [new SkinAsset("assets/characters/ahri/skins/base/ahri.dds", "hash1", 500)]
        };

        var skinB = new Skin("yasuo-skin", "Yasuo Skin", "Yasuo")
        {
            SkinSlot = 0,
            Assets = [new SkinAsset("assets/characters/yasuo/skins/base/yasuo.dds", "hash2", 500)]
        };

        var conflicts = detector.DetectConflicts([skinA, skinB]);
        Assert.Empty(conflicts);
    }
}
